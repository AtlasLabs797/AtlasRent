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
/// Segundo factor (F1-06): alta del MFA, confirmación, paso del TOTP y entrada con código de
/// recuperación.
/// <para>
/// Los endpoints del segundo factor son <c>[AllowAnonymous]</c> a propósito: quien está a mitad de
/// un login todavía no tiene sesión, y sin ella el middleware de validación los cortaría. Cada uno
/// comprueba por su cuenta qué credencial trae (token provisional o sesión) y responde 401 si no
/// trae ninguna.
/// </para>
/// <para>
/// También se saltan la comprobación de CSRF por lo mismo: en el camino del token provisional no
/// hay cookie de CSRF que leer. Las cookies de sesión son <c>SameSite=Strict</c>, así que un POST
/// desde otro sitio no las lleva.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/auth/mfa")]
public sealed class MfaController(
    AtlasRentDbContext contexto,
    ServicioMfa mfa,
    CookiesSesion cookies,
    IPolicyService politica,
    ServicioSesiones sesiones,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    /// <summary>
    /// Empieza el alta del MFA y devuelve la URI <c>otpauth://</c> que el frontend convierte en QR.
    /// <para>
    /// El secreto sale en esta respuesta y en ninguna otra: después solo se usa desde el servidor.
    /// </para>
    /// </summary>
    [HttpPost("activar")]
    [AllowAnonymous]
    [SaltarCsrf]
    public async Task<IActionResult> Activar()
    {
        var usuario = await UsuarioQueEstaActivandoAsync(HttpContext.RequestAborted);
        if (usuario is null)
        {
            return SinCredencial();
        }

        var datos = await mfa.IniciarAsync(usuario, Ip(), HttpContext.RequestAborted);
        if (datos is null)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.MfaInvalido,
                "El MFA ya está activo",
                "Este usuario ya tiene el segundo factor activado.");
        }

        return Ok(new RespuestaActivacionMfa(datos.Uri, datos.Digitos, datos.PeriodoSegundos));
    }

    /// <summary>
    /// Confirma el alta con un código TOTP válido. Hasta que esto pasa, el MFA no está activo y el
    /// usuario entra con un aviso de que le falta el segundo factor.
    /// </summary>
    [HttpPost("confirmar")]
    [AllowAnonymous]
    [SaltarCsrf]
    public async Task<IActionResult> Confirmar([FromBody] MfaConfirmarPeticion peticion)
    {
        var usuario = await UsuarioQueEstaActivandoAsync(HttpContext.RequestAborted);
        if (usuario is null)
        {
            return SinCredencial();
        }

        var confirmacion = await mfa.ConfirmarAsync(usuario, peticion.Codigo, Ip(), HttpContext.RequestAborted);
        if (confirmacion is null)
        {
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.MfaInvalido,
                "Código no válido",
                "El código no es válido o el alta no está pendiente.");
        }

        // Los códigos de recuperación en claro se ven aquí y en ningún otro sitio.
        return Ok(new RespuestaConfirmacionMfa(confirmacion.CodigosRecuperacion));
    }

    /// <summary>
    /// Login en dos pasos: el token provisional más el código TOTP dan la sesión completa.
    /// </summary>
    [HttpPost("verificar")]
    [AllowAnonymous]
    [SaltarCsrf]
    [EnableRateLimiting(PoliticasLimite.Login)]
    public async Task<IActionResult> Verificar([FromBody] SegundoFactorPeticion peticion)
    {
        var sesion = await mfa.VerificarSegundoFactorAsync(
            TokenProvisional(),
            peticion.Codigo,
            Ip(),
            Agente(),
            HttpContext.RequestAborted);

        return await ResponderSesion(sesion);
    }

    /// <summary>
    /// Entra con un código de recuperación, para cuando se perdió el dispositivo. El código se
    /// gasta al usarlo.
    /// </summary>
    [HttpPost("recuperacion")]
    [AllowAnonymous]
    [SaltarCsrf]
    [EnableRateLimiting(PoliticasLimite.Login)]
    public async Task<IActionResult> Recuperacion([FromBody] SegundoFactorPeticion peticion)
    {
        var sesion = await mfa.VerificarCodigoRecuperacionAsync(
            TokenProvisional(),
            peticion.Codigo,
            Ip(),
            Agente(),
            HttpContext.RequestAborted);

        return await ResponderSesion(sesion);
    }

    /// <summary>
    /// Usuario que puede dar de alta el MFA: el del token provisional si lo trae (que es el caso
    /// obligatorio del administrador) o el de la sesión (MFA opcional para el resto).
    /// </summary>
    private async Task<Usuario?> UsuarioQueEstaActivandoAsync(CancellationToken ct)
    {
        var desafio = await mfa.BuscarDesafioAsync(TokenProvisional(), ct);
        if (desafio is not null)
        {
            return await contexto.Usuario
                .SingleOrDefaultAsync(u => u.Id == desafio.UsuarioId && u.Activo && u.DeletedAt == null, ct);
        }

        // Camino de sesión (MFA opcional del resto de roles). El endpoint es [AllowAnonymous], así que
        // el middleware no ha validado la sesión: se hace aquí contra la base de datos, igual que
        // en cualquier otro endpoint. Un JWT firmado pero de sesión revocada (logout), caducada o de
        // un usuario desactivado no vale (SEC-013).
        if (User.Identity?.IsAuthenticated != true
            || UsuarioDeLaSesion() is not { } usuarioId
            || !Guid.TryParse(User.FindFirst("sid")?.Value, out var sesionId))
        {
            return null;
        }

        // [SaltarCsrf] sigue siendo necesario por el camino del token provisional (no hay cookie de
        // CSRF todavía); en el camino de sesión la comprobación se hace aquí, a mano.
        if (!CsrfCorrecto())
        {
            return null;
        }

        if (!(await sesiones.ValidarAsync(sesionId, ct)).Valida)
        {
            return null;
        }

        return await contexto.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId && u.Activo && u.DeletedAt == null, ct);
    }

    private bool CsrfCorrecto()
    {
        var cabecera = Request.Headers["X-CSRF-Token"].ToString();
        var cookie = Request.Cookies[NombresCookies.Csrf];

        return !string.IsNullOrEmpty(cookie)
            && !string.IsNullOrEmpty(cabecera)
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(cabecera), System.Text.Encoding.UTF8.GetBytes(cookie));
    }

    private async Task<IActionResult> ResponderSesion(ResultadoSesion? sesion)
    {
        if (sesion is null)
        {
            // El mismo código para token caducado, gastado o código incorrecto.
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.MfaInvalido,
                "Segundo factor no válido",
                "El código no es válido o el token provisional caducó.");
        }

        cookies.BorrarDesafioMfa(Response);
        cookies.FijarSesion(Response, sesion);
        return Ok(IdentidadRespuesta.Desde(
            sesion,
            await politica.PermisosDeAsync(sesion.UsuarioId, HttpContext.RequestAborted)));
    }

    private IActionResult SinCredencial() =>
        Problemas.Construir(
            StatusCodes.Status401Unauthorized,
            Problemas.MfaNoVerificado,
            "No autenticado",
            "Hace falta una sesión o un token provisional de segundo factor.");

    /// <summary>Token provisional de la cookie. Vacío si no la hay.</summary>
    private string TokenProvisional() => Request.Cookies[NombresCookies.DesafioMfa] ?? string.Empty;

    /// <summary>Usuario del JWT de la petición, si lo hay. El middleware no lo exige aquí.</summary>
    private Guid? UsuarioDeLaSesion() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    private string Ip() => resolutorIp.Resolver(HttpContext);

    private string? Agente() =>
        Request.Headers.UserAgent.ToString() is { Length: > 0 } agente ? agente : null;
}