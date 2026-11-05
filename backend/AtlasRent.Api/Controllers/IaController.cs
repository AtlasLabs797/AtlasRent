using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Asistente: consultas de solo lectura (F6-02) y propuestas que un humano aprueba (F6-03), con
/// sesión normal de navegador.
/// <para>
/// Comparte servicio y contrato con <c>POST /api/v1/agente/consulta</c>: la diferencia entre las
/// dos rutas es la credencial, no los datos. Un token de agente no ve nada más por aquí que lo que
/// ve su propietario con la sesión, porque el doble techo de F6-01 y la comprobación de alcance del
/// servicio son los mismos en las dos.
/// </para>
/// <para>
/// <b>Las propuestas solo se proponen aquí.</b> Aprobar y rechazar son de otra acción
/// (<c>mensajeria.masivo</c>, la del envío) y van por el servicio que ejecuta la acción: proponer
/// recordatorio y poder mandarlo son dos poderes distintos.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/ia")]
public sealed class IaController(
    ServicioConsultaIa consultas,
    ServicioPropuestasAgente propuestas,
    ServicioAprobaciones aprobaciones,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Carga los <c>{ pregunta, alcance }</c> del contrato (<c>frontend/src/ia/api.ts</c>) y devuelve
    /// los <c>{ respuesta, alcance, proveedor, modelo }</c>.
    /// <para>
    /// Los tres fallos que el frontend ya distingue tienen su camino propio: <b>400</b> si el alcance
    /// no existe, <b>403</b> si falta un permiso, <b>429</b> si se pasó un límite y <b>503</b> si el
    /// proveedor no está disponible. Nunca un 500 por una clave que falta.
    /// </para>
    /// </summary>
    [HttpPost("consulta")]
    [Accion(Acciones.IaConsultar)]
    public async Task<IActionResult> Consultar(ConsultaIaPeticion peticion, CancellationToken ct)
    {
        try
        {
            return Ok(await consultas.ConsultarAsync(peticion.Pregunta, peticion.Alcance, Ip(), ct));
        }
        catch (ArgumentException invalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                invalida.Message);
        }
        catch (PermisoDenegadoException)
        {
            // La excepción la lanza el servicio y llega hasta aquí sin pasar por el filtro global,
            // que solo convierte las suyas. La denegación ya está auditada.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para consultar este alcance.");
        }
        catch (LimiteIaExcedidoException pasado)
        {
            return Problemas.Construir(
                StatusCodes.Status429TooManyRequests,
                Problemas.LimiteConsultasIa,
                "Límite de consultas alcanzado",
                pasado.Message);
        }
        catch (ProveedorIaException noDisponible)
        {
            return Problemas.Construir(
                StatusCodes.Status503ServiceUnavailable,
                Problemas.ProveedorIaNoDisponible,
                "Asistente no disponible",
                noDisponible.Message);
        }
    }

    // ---------------------------------------------------------------- propuestas (F6-03)

    /// <summary>
    /// Propone una acción sin ejecutarla: deja la solicitud en la bandeja y ya está. La respuesta es
    /// la propuesta, con su huella y su pendiente, que es lo mismo que va a ver el aprobador.
    /// </summary>
    [HttpPost("propuesta")]
    [Accion(Acciones.IaProponer)]
    public async Task<IActionResult> Proponer(PropuestaIaPeticion peticion, CancellationToken ct)
    {
        try
        {
            return Ok(await propuestas.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), ct));
        }
        catch (ArgumentException invalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                invalida.Message);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para proponer acciones.");
        }
    }

    /// <summary>
    /// Las propuestas que esperan decisión. <b>Aquí sí sale el nombre real del cliente</b>: es una
    /// pantalla interna para quien aprueba, y una propuesta sin saber a quién se refiere no se puede
    /// aprobar. En el payload y en la auditoría va el seudónimo.
    /// </summary>
    [HttpGet("propuestas")]
    [Accion(Acciones.IaProponer)]
    public async Task<IActionResult> Propuestas(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct)
    {
        try
        {
            return await EnvoltorioAsync(propuestas.ListarAsync(
                Paginacion(page, pageSize, sortBy, sortDir), UsuarioDeLaPeticion(), ct));
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para ver las propuestas.");
        }
    }

    /// <summary>
    /// Aprueba la propuesta y manda el aviso. <b>No caduca con un 409</b>: si el saldo cambió desde
    /// que se propuso, la respuesta llega con <c>resultado: "Caducada"</c> y el motivo, y no se manda
    /// nada. Un 409 haría creer a quien aprueba que el sistema falló, cuando lo que pasó es que la
    /// propuesta ya no valía.
    /// </summary>
    [HttpPost("propuestas/{id:guid}/aprobacion")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Aprobar(Guid id, CancellationToken ct) =>
        await ConIdempotenciaAsync(
            new { id },
            $"/api/v1/solicitudes/{id:D}/aprobacion",
            ct,
            m => aprobaciones.AprobarAsync(id, UsuarioDeLaPeticion(), Ip(), m, ct));

    /// <summary>Rechaza la propuesta. No ejecuta nada y deja el comentario, que es obligatorio.</summary>
    [HttpPost("propuestas/{id:guid}/rechazo")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Rechazar(
        Guid id,
        PropuestaIaRechazoPeticion peticion,
        CancellationToken ct) =>
        await ConIdempotenciaAsync(
            peticion,
            $"/api/v1/solicitudes/{id:D}/rechazo",
            ct,
            m => aprobaciones.RechazarAsync(id, peticion.Comentario, UsuarioDeLaPeticion(), Ip(), m, ct));

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Idempotencia de las decisiones. La ruta que se guarda es la de la <b>solicitud</b> y no la del
    /// endpoint, a propósito: aprobar un contrato y aprobar una propuesta son la misma operación
    /// sobre la misma tabla, así que un reintento con la misma clave se come la respuesta guardada en
    /// vez de mandar dos avisos.
    /// </summary>
    private async Task<IActionResult> ConIdempotenciaAsync(
        object cuerpo,
        string ruta,
        CancellationToken ct,
        Func<MutacionIdempotente?, Task<object>> operacion)
    {
        var mutacion = Mutar(cuerpo, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await operacion(mutacion));
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "No tienes permiso para decidir esta propuesta.");
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