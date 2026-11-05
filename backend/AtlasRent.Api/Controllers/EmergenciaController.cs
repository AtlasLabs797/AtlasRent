using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Acceso de emergencia (break-glass, F1-06): la vía para recuperar el sistema cuando un
/// administrador pierde el dispositivo y sus códigos de recuperación.
/// <para>
/// Pedirlo y aprobarlo exigen sesión y son dos administradores distintos. Usarlo es lo único que
/// va con el token provisional del segundo factor, porque el usuario que lo usa es el que no puede
/// pasar ese segundo factor.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/auth/emergencia")]
public sealed class EmergenciaController(
    AtlasRentDbContext contexto,
    ServicioEmergencia emergencia,
    CookiesSesion cookies,
    IPolicyService politica,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    /// <summary>Solicitudes de acceso de emergencia, por defecto las pendientes. Nunca lleva el código.</summary>
    [HttpGet("solicitudes")]
    [Accion(Acciones.SeguridadAccesoEmergencia)]
    public async Task<IActionResult> Solicitudes([FromQuery] string estado = "pendiente", CancellationToken ct = default)
    {
        if (!ServicioEmergencia.EstadosSolicitud.Contains(estado))
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                "El estado tiene que ser pendiente, aprobada, caducada, usada o todas.");
        }

        var datos = await emergencia.ListarSolicitudesAsync(estado, ct);
        return Ok(new { data = datos, total = datos.Count, page = 1, pageSize = datos.Count, totalPages = 1 });
    }

    /// <summary>Primer paso: un administrador pide el acceso para otro que está bloqueado.</summary>
    [HttpPost("solicitar")]
    [Accion(Acciones.SeguridadAccesoEmergencia)]
    public async Task<IActionResult> Solicitar([FromBody] EmergenciaSolicitarPeticion peticion)
    {
        var solicitante = await UsuarioDeLaPeticionAsync();
        if (solicitante is null)
        {
            return Denegada();
        }

        if (peticion.UsuarioObjetivoId == Guid.Empty)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                "Falta el usuario al que se le pide el acceso de emergencia.");
        }

        var id = await emergencia.SolicitarAsync(
            solicitante,
            peticion.UsuarioObjetivoId,
            peticion.Motivo,
            Ip(),
            HttpContext.RequestAborted);

        return id is null
            ? Denegada()
            : Ok(new RespuestaAprobacionEmergencia(id.Value, Caduca: null, CodigoAcceso: null));
    }

    /// <summary>
    /// Segundo paso: otro administrador lo aprueba y recibe el código de acceso de un solo uso. Es
    /// la segunda aprobación; el que lo pidió no puede aprobarlo.
    /// </summary>
    [HttpPost("aprobar")]
    [Accion(Acciones.SeguridadAccesoEmergencia)]
    public async Task<IActionResult> Aprobar([FromBody] EmergenciaAprobarPeticion peticion)
    {
        var aprobador = await UsuarioDeLaPeticionAsync();
        if (aprobador is null)
        {
            return Denegada();
        }

        if (peticion.SolicitudId == Guid.Empty)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                "Falta la solicitud de acceso de emergencia que se quiere aprobar.");
        }

        var aprobacion = await emergencia.AprobarAsync(
            aprobador, peticion.SolicitudId, Ip(), HttpContext.RequestAborted);

        return aprobacion is null
            ? Denegada()
            : Ok(new RespuestaAprobacionEmergencia(
                peticion.SolicitudId,
                aprobacion.Caduca,
                aprobacion.CodigoAcceso));
    }

    /// <summary>
    /// El administrador bloqueado entra con el código aprobado, sin segundo factor. La sesión que
    /// sale queda marcada como de emergencia.
    /// </summary>
    [HttpPost("entrar")]
    [AllowAnonymous]
    [SaltarCsrf]
    [EnableRateLimiting(PoliticasLimite.Login)]
    public async Task<IActionResult> Entrar([FromBody] SegundoFactorPeticion peticion)
    {
        var sesion = await emergencia.EntrarAsync(
            Request.Cookies[NombresCookies.DesafioMfa] ?? string.Empty,
            peticion.Codigo,
            Ip(),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } agente ? agente : null,
            HttpContext.RequestAborted);

        if (sesion is null)
        {
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.MfaInvalido,
                "Acceso de emergencia no válido",
                "El código no es válido, ya se usó o caducó.");
        }

        cookies.BorrarDesafioMfa(Response);
        cookies.FijarSesion(Response, sesion);
        return Ok(IdentidadRespuesta.Desde(
            sesion,
            await politica.PermisosDeAsync(sesion.UsuarioId, HttpContext.RequestAborted)));
    }

    private async Task<Usuario?> UsuarioDeLaPeticionAsync() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id)
            ? await contexto.Usuario.SingleOrDefaultAsync(u => u.Id == id && u.Activo && u.DeletedAt == null, HttpContext.RequestAborted)
            : null;

    private IActionResult Denegada() =>
        Problemas.Construir(
            StatusCodes.Status403Forbidden,
            Problemas.EmergenciaDenegada,
            "Acceso de emergencia denegado",
            "El acceso de emergencia no se puede pedir ni aprobar así.");

    private string Ip() => resolutorIp.Resolver(HttpContext);
}