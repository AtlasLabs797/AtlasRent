using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    ServicioMfa mfa,
    CookiesSesion cookies,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    /// <summary>Solo se mandan los datos que el frontend necesita tras entrar.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [SaltarCsrf]
    [EnableRateLimiting(PoliticasLimite.Login)]
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

        // La contraseña es correcta pero falta el segundo factor: no hay sesión todavía, solo el
        // token provisional (obligatorio para el administrador, opcional para el resto).
        if (resultado.RequiereMfa)
        {
            var desafio = await mfa.EmitirDesafioAsync(resultado.Usuario, IpDeLaPeticion(), HttpContext.RequestAborted);
            FijarCookieDesafio(desafio);

            return Ok(new RespuestaDesafioMfa(
                RequiereMfa: true,
                RequiereConfiguracion: desafio.RequiereConfiguracion,
                NombreAcceso: desafio.NombreAcceso,
                Caduca: desafio.CaducaAt));
        }

        FijarCookies(resultado.Sesion!);
        return Ok(IdentidadRespuesta.Desde(resultado.Sesion!));
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
        return Ok(IdentidadRespuesta.Desde(resultado));
    }

    [HttpPost("logout")]
    [Accion(Acciones.SesionCerrar)]
    public async Task<IActionResult> Logout()
    {
        await autenticacion.CerrarAsync(SesionIdDeLaPeticion(), HttpContext.RequestAborted);
        BorrarCookies();
        BorrarCookieDesafio();

        // El cierre de sesión se responde 204 aunque el token ya no existiera: al cliente le da
        // igual y así no se le dice si su sesión estaba viva.
        return NoContent();
    }

    /// <summary>Identidad del usuario de la petición.</summary>
    [HttpGet("yo")]
    [Accion(Acciones.SesionVer)]
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

        // El rastro de una sesión de emergencia viene de la fila de sesión, no del usuario.
        var emergencia = await autenticacion.AccesoEmergenciaDeLaSesionAsync(
            SesionIdDeLaPeticion(), HttpContext.RequestAborted);

        return Ok(IdentidadRespuesta.Desde(usuario, emergencia, contextoSesion.Rol));
    }

    private Guid SesionIdDeLaPeticion() => Guid.Parse(User.FindFirst("sid")!.Value);

    /// <summary>IP de origen. El orden del pipeline de F1-13 decide si viene de un proxy de confianza.</summary>
    private string IpDeLaPeticion() => resolutorIp.Resolver(HttpContext);

    private void FijarCookies(ResultadoSesion resultado) => cookies.FijarSesion(Response, resultado);

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

    /// <summary>
    /// Token provisional del segundo factor (F1-06). HttpOnly como los de sesión y limitado a las
    /// rutas de autenticación: con él no se abre sesión, solo se pasa el segundo factor.
    /// </summary>
    internal void FijarCookieDesafio(DesafioMfa desafio) => cookies.FijarDesafioMfa(Response, desafio);

    /// <summary>Se borra en cuanto el segundo factor pasa: ya no sirve para nada.</summary>
    internal void BorrarCookieDesafio() => cookies.BorrarDesafioMfa(Response);
}