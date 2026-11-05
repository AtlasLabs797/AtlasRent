using Microsoft.Extensions.DependencyInjection;

namespace AtlasRent.Api.Services;

/// <summary>
/// Claves de partición de los limitadores de peticiones.
/// <para>
/// El límite global se lleva por usuario cuando la petición está autenticada y por IP cuando la
/// petición no lo está (login, refresco, salud). Por eso <c>UseRateLimiter</c> va después de
/// <c>UseAuthentication</c>: es el punto en el que ya se sabe quién va en la petición.
/// </para>
/// </summary>
public static class ClavesLimite
{
    /// <summary>Límite global: por usuario si hay sesión, por IP si la petición es anónima.</summary>
    public static string Global(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated == true)
        {
            return $"usuario:{http.User.FindFirst("sub")?.Value ?? "?"}";
        }

        return $"ip:{Ip(http)}";
    }

    /// <summary>
    /// Límite de login: por IP, y más estrecho que el global.
    /// <para>
    /// docs/SEGURIDAD.md A.2 pide el login "por IP y por usuario". La parte de usuario no se cuenta
    /// aquí porque el control por usuario ya es más fuerte y está en la base de datos: el bloqueo de
    /// cuenta tras 5 intentos. La clave del limitador sale del cuerpo de la petición (por IP y por
    /// nombre de acceso), pero el framework solo admite particiones síncronas y leer el cuerpo de
    /// forma síncrona está prohibido en Kestrel: haría falta un middleware por delante del
    /// limitador solo para eso.
    /// </para>
    /// <para>
    /// TODO(A#): si algún día hace falta el contador por nombre de acceso además del bloqueo de
    /// cuenta, se añade ese middleware y la clave pasa a ser IP + nombre.
    /// </para>
    /// </summary>
    public static string Login(HttpContext http) => $"login:{Ip(http)}";

    // El resolutor se pide por la petición porque los limitadores se configuran al arrancar y no
    // pueden coger un servicio con ámbito de petición.
    private static string Ip(HttpContext http) =>
        http.RequestServices.GetRequiredService<IResolutorIpPeticion>().Resolver(http);
}

/// <summary>Nombres de las políticas de límite, compartidos por <c>Program.cs</c> y los controladores.</summary>
public static class PoliticasLimite
{
    /// <summary>Login: límite por IP, más estrecho que el global.</summary>
    public const string Login = "login";
}