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
/// Reservas (docs/ESPECIFICACION.md §3.4): mensual y corta.
/// <para>
/// Los permisos no son nuevos: <c>cliente.ver</c> para leer, que en la matriz de §2 es literalmente
/// "Clientes, prospectos y reservas", y <c>cliente.editar</c> para crear, editar y cancelar, que
/// es lo mismo que hacer una alta o una baja en la cartera.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio (una reserva que no se puede guardar) y el de disponibilidad
/// (<see cref="Problemas.UnidadNoDisponible"/>): los dos son la misma respuesta para quien está
/// delante de la pantalla, con la diferencia de que el segundo lleva el motivo del choque.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/reservas")]
public sealed class ReservasController(
    ServicioReservas reservas,
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
        [FromQuery] EstadoReserva? estado,
        [FromQuery] TipoReserva? tipo,
        [FromQuery] Guid? unidadId,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? prospectoId,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] string? q,
        [FromQuery] bool incluirInactivas,
        CancellationToken ct) =>
        await EnvoltorioAsync(reservas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir),
            new ReservaFiltro
            {
                Estado = estado,
                Tipo = tipo,
                UnidadId = unidadId,
                ClienteId = clienteId,
                ProspectoId = prospectoId,
                Desde = desde,
                Hasta = hasta,
                Texto = q,
                IncluirInactivas = incluirInactivas,
            },
            ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => reservas.LeerAsync(id, ct));

    /// <summary>
    /// Alta de la reserva. Devuelve 201 y no 200 porque aquí, a diferencia del resto de la API, la
    /// fila creada es el criterio de aceptación de la prueba de concurrencia: dos reservas
    /// solapadas tienen que dar exactamente una 201 y una 409.
    /// </summary>
    [HttpPost]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Crear(ReservaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/reservas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => reservas.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct), creado: true);
    }

    /// <summary>Edición de una reserva activa. Lo que no está activa se rechaza con 409.</summary>
    [HttpPut("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Actualizar(Guid id, ReservaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/reservas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => reservas.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Baja lógica: la reserva queda cancelada y suelta el bloqueo de la unidad. La fila y sus días
    /// se quedan, porque una reserva cancelada es historial.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/reservas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => reservas.CancelarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos. Un <see cref="DbUpdateException"/> es un 409 de
    /// disponibilidad: en este servicio, después de validar, lo único que la base puede rechazar es
    /// el bloqueo (la restricción de exclusión o el trigger de capacidad de F2-01). El mensaje que
    /// sale es el de negocio, no el de PostgreSQL.
    /// </summary>
    private async Task<IActionResult> GuardarAsync<TResultado>(
        Func<Task<TResultado>> operacion,
        bool creado = false)
    {
        try
        {
            var resultado = await operacion();
            return creado ? StatusCode(StatusCodes.Status201Created, resultado) : Ok(resultado);
        }
        catch (Exception e) when (EsFalloDeOcupacion(e) || EsFalloDeOcupacion(e.InnerException))
        {
            return NoDisponible(
                "La unidad no está disponible para esas fechas: la tienen un contrato, otra reserva "
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
            return Conflicto("No se pudo guardar la reserva.");
        }
    }

    /// <summary>
    /// Los dos SQLSTATE que F2-01 levanta cuando el bloqueo no cabe: la restricción de exclusión
    /// del rango (23P01) y el trigger de capacidad del día (23001). Se mira en la excepción y en su
    /// causa, porque <see cref="ServicioDisponibilidad"/> traduce el fallo de la base a un
    /// <see cref="InvalidOperationException"/> que se guarda el <see cref="PostgresException"/>
    /// original.
    /// </summary>
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

    private IActionResult NoDisponible(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status409Conflict,
            Problemas.UnidadNoDisponible,
            "Unidad no disponible",
            detalle);
}