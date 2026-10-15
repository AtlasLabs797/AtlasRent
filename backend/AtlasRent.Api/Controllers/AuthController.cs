using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Autenticación: login, refresco, cierre de sesión e identidad (docs/DOC_TECNICA.md §6).
/// <para>
/// Las sesiones viajan en cookies <c>HttpOnly; Secure; SameSite=Strict</c>; la de CSRF va sin
/// <c>HttpOnly</c> porque es double-submit y el navegador tiene que poder leerla.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    ServicioAutenticacion autenticacion,
    ContextoSesion contextoSesion,
    TimeProvider reloj) : ControllerBase
{
    /// <summary>Solo se mandan los datos que el frontend necesita tras entrar.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [SaltarCsrf]
    public async Task<IActionResult> Login([FromBody] LoginPeticion peticion)
    {
        var resultado = await autenticacion.LoginAsync(
            peticion.NombreAcceso,
            peticion.Contrasena,
            IpDeLaPeticion(),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } agente ? agente : null,
            HttpContext.RequestAborted);

        if (resultado is null)
        {
            // Una sola respuesta para usuario inexistente, contraseña incorrecta y cuenta
            // desactivada: no se le dice a nadie qué usernames existen.
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.CredencialesInvalidas,
                "No autenticado",
                "Usuario o contraseña incorrectos.");
        }

        FijarCookies(resultado);
        return Ok(new IdentidadRespuesta(
            resultado.UsuarioId,
            resultado.NombreAcceso,
            resultado.Nombre,
            resultado.Email,
            Roles.Valor,
            resultado.DebeCambiarPassword,
            resultado.MfaActivado,
            resultado.UltimoAcceso));
    }

    /// <summary>
    /// Rota el token de refresco. Es <c>[AllowAnonymous]</c> porque el JWT de acceso puede estar
    /// caducado justo cuando toca refrescar, que es el caso para el que existe.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refrescar()
    {
        var token = Request.Cookies[NombresCookies.Refresco];
        if (string.IsNullOrEmpty(token))
        {
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.RefrescoInvalido,
                "No autenticado",
                "Falta el token de refresco.");
        }

        var resultado = await autenticacion.RefrescarAsync(
            token,
            IpDeLaPeticion(),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } agente ? agente : null,
            HttpContext.RequestAborted);

        if (resultado is null)
        {
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.RefrescoInvalido,
                "No autenticado",
                "El token de refresco no es válido.");
        }

        FijarCookies(resultado);
        return Ok(new IdentidadRespuesta(
            resultado.UsuarioId,
            resultado.NombreAcceso,
            resultado.Nombre,
            resultado.Email,
            Roles.Valor,
            resultado.DebeCambiarPassword,
            resultado.MfaActivado,
            resultado.UltimoAcceso));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await autenticacion.CerrarAsync(SesionIdDeLaPeticion(), HttpContext.RequestAborted);
        BorrarCookies();

        // El cierre de sesión se responde 204 aunque el token ya no existiera: al cliente le da
        // igual y así no se le dice si su sesión estaba viva.
        return NoContent();
    }

    /// <summary>Identidad del usuario de la petición.</summary>
    [HttpGet("yo")]
    public async Task<IActionResult> Yo()
    {
        var usuario = await autenticacion.IdentidadAsync(contextoSesion.UsuarioId!.Value, HttpContext.RequestAborted);
        if (usuario is null)
        {
            return Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.SesionNoValida,
                "No autenticado",
                "El usuario de la sesión ya no existe.");
        }

        return Ok(IdentidadRespuesta.Desde(usuario));
    }

    private Guid SesionIdDeLaPeticion() => Guid.Parse(User.FindFirst("sid")!.Value);

    /// <summary>IP de origen. El orden del pipeline de F1-13 decide si viene de un proxy de confianza.</summary>
    private string IpDeLaPeticion() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";

    private void FijarCookies(ResultadoSesion resultado)
    {
        var caduca = reloj.GetUtcNow().AddHours(ServicioSesiones.MaximoHoras);

        Response.Cookies.Append(NombresCookies.Acceso, resultado.Tokens.JwtAcceso, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            // La cookie de acceso caduca con el JWT que lleva dentro, no con la sesión.
            Expires = resultado.Tokens.CaducaAcceso,
        });

        // El token de refresco solo se manda a las rutas de autenticación.
        Response.Cookies.Append(NombresCookies.Refresco, resultado.Tokens.TokenRefresco, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = caduca,
        });

        // Sin HttpOnly a propósito: el frontend lo lee de document.cookie y lo devuelve en
        // X-CSRF-Token (doble envío).
        Response.Cookies.Append(NombresCookies.Csrf, resultado.Tokens.TokenCsrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = caduca,
        });
    }

    private void BorrarCookies()
    {
        foreach (var nombre in new[] { NombresCookies.Acceso, NombresCookies.Refresco, NombresCookies.Csrf })
        {
            Response.Cookies.Delete(nombre, new CookieOptions
            {
                HttpOnly = nombre != NombresCookies.Csrf,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = nombre == NombresCookies.Refresco ? "/api/v1/auth" : "/",
            });
        }
    }
}