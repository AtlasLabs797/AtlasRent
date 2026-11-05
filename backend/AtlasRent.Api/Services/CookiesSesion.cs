using Microsoft.AspNetCore.Http;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cookies de sesión y del segundo factor (F1-06).
/// <para>
/// Todas van <c>HttpOnly; Secure; SameSite=Strict</c>, salvo la de CSRF, que es double-submit y el
/// navegador tiene que poder leerla. El token de refresco y el token provisional del segundo factor
/// además limitan su <c>Path</c> a las rutas de autenticación: no viajan en cada petición.
/// </para>
/// </summary>
public sealed class CookiesSesion(TimeProvider reloj)
{
    private const string RutaAuth = "/api/v1/auth";

    /// <summary>Cookies que abren sesión: acceso, refresco y CSRF.</summary>
    public void FijarSesion(HttpResponse respuesta, ResultadoSesion resultado)
    {
        var caduca = reloj.GetUtcNow().AddHours(ServicioSesiones.MaximoHoras);

        respuesta.Cookies.Append(NombresCookies.Acceso, resultado.Tokens.JwtAcceso, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            // La cookie de acceso caduca con el JWT que lleva dentro, no con la sesión.
            Expires = resultado.Tokens.CaducaAcceso,
        });

        // El token de refresco solo se manda a las rutas de autenticación.
        respuesta.Cookies.Append(NombresCookies.Refresco, resultado.Tokens.TokenRefresco, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RutaAuth,
            Expires = caduca,
        });

        // Sin HttpOnly a propósito: el frontend lo lee de document.cookie y lo devuelve en
        // X-CSRF-Token (doble envío).
        respuesta.Cookies.Append(NombresCookies.Csrf, resultado.Tokens.TokenCsrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = caduca,
        });
    }

    /// <summary>
    /// Token provisional del segundo factor. No abre sesión: solo habilita los endpoints del
    /// segundo factor, y solo durante <see cref="ServicioMfa.MinutosDesafio"/> minutos.
    /// </summary>
    public void FijarDesafioMfa(HttpResponse respuesta, DesafioMfa desafio) =>
        respuesta.Cookies.Append(NombresCookies.DesafioMfa, desafio.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RutaAuth,
            Expires = desafio.CaducaAt,
        });

    /// <summary>Se borra en cuanto el segundo factor pasa: ya no sirve para nada.</summary>
    public void BorrarDesafioMfa(HttpResponse respuesta) =>
        respuesta.Cookies.Delete(NombresCookies.DesafioMfa, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RutaAuth,
        });
}