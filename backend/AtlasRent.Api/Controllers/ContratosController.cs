using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Contratos (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Permisos, sin crear ninguno nuevo. Cada transición lleva el que le toca según la matriz de §2:
/// <list type="bullet">
/// <item>
/// <c>contrato.borrador</c> ("crear y editar contratos en borrador", fila 5) para crear, editar y
/// <b>enviar a aprobación</b>: es el camino del empleado.
/// </item>
/// <item>
/// <c>contrato.estado</c> (fila 6, "aprobar, activar, modificar activo, dar de baja y liquidar") para
/// aprobar, rechazar, activar, modificar, dar de baja, anular la baja, finalizar y liquidar. La fila
/// es de gerente y administrador, así que el empleado recibe 403 en todas ellas.
/// </item>
/// <item>
/// <c>cliente.ver</c> para leer. La fila 2 de §2 es literalmente "Ver clientes, unidades, reservas y
/// contratos", así que el mismo identificador que protege ver una reserva protege ver un contrato.
/// <c>contrato.borrador</c> no es un permiso de lectura sino de escritura sobre el borrador, y
/// usarlo para listar dejaría al gerente sin poder ver contratos si a alguien se le quitara.
/// </item>
/// </list>
/// </para>
/// <para>
/// El 403 del filtro global y el del servicio son la misma regla por dos caminos: el atributo
/// protege la entrada por HTTP y <c>ServicioContratos</c> vuelve a mirar el techo de permisos de quien
/// llama antes de aprobar.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/contratos")]
public sealed class ContratosController(
    ServicioContratos contratos,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] EstadoContrato? estado,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] string? q,
        CancellationToken ct) =>
        await EnvoltorioAsync(contratos.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), q, estado, clienteId, unidadId, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => contratos.LeerAsync(id, ct));

    /// <summary>Alta en borrador (docs/ESPECIFICACION.md §2: el empleado crea borradores).</summary>
    [HttpPost]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Crear(ContratoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/contratos");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => contratos.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct), creado: true);
    }

    /// <summary>
    /// Edición de un borrador o de un contrato pendiente de aprobación. Editar uno pendiente
    /// <b>caduca su solicitud</b>: lo que cambió ya no es lo que se mandó a aprobar, así que hay que
    /// volver a enviarlo. Un contrato vigente se modifica por <c>/cambios</c>, que crea versión.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Actualizar(Guid id, ContratoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/contratos/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => contratos.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Cambio en un contrato vigente. Exige <c>contrato.estado</c> y genera versión nueva (§3.5).
    /// </summary>
    [HttpPost("{id:guid}/cambios")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Cambios(Guid id, ContratoCambioPeticion peticion, CancellationToken ct) =>
        await ConIdempotenciaAsync(peticion, $"/api/v1/contratos/{id:D}/cambios", ct,
            m => contratos.AplicarCambiosAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Envío a aprobación: abre la solicitud con la huella del contenido que se manda.</summary>
    [HttpPost("{id:guid}/solicitud")]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> EnviarAprobacion(Guid id, ContratoEnvioPeticion peticion, CancellationToken ct) =>
        await ConIdempotenciaAsync(peticion, $"/api/v1/contratos/{id:D}/solicitud", ct,
            m => contratos.EnviarAprobacionAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>
    /// Aprobación. Solo gerente y administrador; un empleado recibe 403 del filtro global antes de
    /// llegar aquí, y si algún camino llegara, el servicio lo vuelve a comprobar.
    /// </summary>
    [HttpPost("{id:guid}/aprobacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Aprobar(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(new { id }, $"/api/v1/contratos/{id:D}/aprobacion", ct,
            m => contratos.AprobarAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Rechazo: vuelve a borrador y exige comentario (§3.5).</summary>
    [HttpPost("{id:guid}/rechazo")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Rechazar(Guid id, ContratoRechazoPeticion peticion, CancellationToken ct) =>
        await ConIdempotenciaAsync(peticion, $"/api/v1/contratos/{id:D}/rechazo", ct,
            m => contratos.RechazarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Activación: el contrato toma la unidad y copia su inventario (F2-08).</summary>
    [HttpPost("{id:guid}/activacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Activar(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(new { id }, $"/api/v1/contratos/{id:D}/activacion", ct,
            m => contratos.ActivarAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Baja programada con su fecha efectiva de fin. El contrato sigue bloqueando la unidad.</summary>
    [HttpPost("{id:guid}/baja")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Baja(Guid id, ContratoBajaPeticion peticion, CancellationToken ct) =>
        await ConIdempotenciaAsync(peticion, $"/api/v1/contratos/{id:D}/baja", ct,
            m => contratos.DarBajaAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Anula una baja que aún no ha llegado a su fecha.</summary>
    [HttpPost("{id:guid}/anular-baja")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> AnularBaja(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(new { id }, $"/api/v1/contratos/{id:D}/anular-baja", ct,
            m => contratos.AnularBajaAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Fin de la relación: libera la unidad.</summary>
    [HttpPost("{id:guid}/finalizacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Finalizar(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(new { id }, $"/api/v1/contratos/{id:D}/finalizacion", ct,
            m => contratos.FinalizarAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Liquidación (F2-09 cierra inventario de salida, fianza y saldos).</summary>
    [HttpPost("{id:guid}/liquidacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Liquidar(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(new { id }, $"/api/v1/contratos/{id:D}/liquidacion", ct,
            m => contratos.LiquidarAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Idempotencia para los botones de transición, que no llevan cuerpo o cuyo cuerpo es el propio
    /// identificador: la clave sale de la cabecera <c>Idempotency-Key</c> y de la ruta.
    /// </summary>
    private async Task<IActionResult> ConIdempotenciaAsync(
        object cuerpo,
        string ruta,
        CancellationToken ct,
        Func<MutacionIdempotente?, Task<ContratoResumen>> operacion)
    {
        var mutacion = Mutar(cuerpo, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => operacion(mutacion));
    }

    /// <summary>
    /// Traduce los fallos de la mutación. Un <see cref="DbUpdateException"/> con un SQLSTATE de
    /// ocupación es el candado de la unidad: al activar un contrato sobre una unidad ocupada sale un
    /// 409 de disponibilidad y la transacción se deshace entera, así que no queda bloqueada sin
    /// contrato.
    /// </summary>
    private async Task<IActionResult> GuardarAsync(Func<Task<ContratoResumen>> operacion, bool creado = false)
    {
        try
        {
            var resultado = await operacion();
            return creado ? StatusCode(StatusCodes.Status201Created, resultado) : Ok(resultado);
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
        catch (Exception e) when (EsFalloDeOcupacion(e) || EsFalloDeOcupacion(e.InnerException))
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.UnidadNoDisponible,
                "Unidad no disponible",
                "La unidad no está disponible para esas fechas: la tiene un contrato, otra reserva "
                + "o ya está completo el día (2 medios días o 1 día completo).");
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
        catch (DbUpdateException)
        {
            return Conflicto("No se pudo guardar el contrato.");
        }
    }

    private static bool EsFalloDeOcupacion(Exception? e) =>
        e is PostgresException
        {
            SqlState: ServicioDisponibilidad.SqlstateSolapamiento or ServicioDisponibilidad.SqlstateOcupacion,
        };

    private async Task<IActionResult> ResponderAsync<T>(Func<Task<T>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
    }
}