using System.Security.Cryptography;
using System.Text;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Middleware;

/// <summary>
/// Valida la sesión contra la base de datos en cada petición y fija el usuario y el rol que leen
/// las políticas de RLS y el interceptor de transacción (docs/DOC_TECNICA.md §4.3 y §7).
/// <para>
/// Solo actúa sobre endpoints de controlador. Los marcados con <c>[AllowAnonymous]</c>
/// (login y refresco) pasan: el login no tiene sesión todavía y el refresco llega con el JWT de
/// acceso ya caducado, que es justo el caso que tiene que resolver.
/// </para>
/// </summary>
public sealed class ValidacionSesionMiddleware(RequestDelegate siguiente)
{
    public async Task InvokeAsync(
        HttpContext http,
        ServicioSesiones sesiones,
        IPolicyService politica,
        ContextoSesion contextoSesion)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ApiControllerAttribute>() is null
            || endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>() is not null)
        {
            await siguiente(http);
            return;
        }

        // F6-01: si la petición venía con un token de agente, el usuario y el rol ya están puestos
        // con los del propietario y no hay sesión de cookie que validar. La cabecera manda: llevar
        // las dos a la vez no es una vía para saltarse nada, porque el techo se comprueba igual.
        if (contextoSesion.EsAgente)
        {
            await siguiente(http);
            return;
        }

        var principal = http.User;
        var sid = principal.FindFirst("sid")?.Value;
        if (principal.Identity?.IsAuthenticated != true
            || sid is null
            || !Guid.TryParse(sid, out var sesionId))
        {
            await Problemas.EscribirAsync(http, Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.SesionNoValida,
                "No autenticado",
                "Falta la cookie de sesión o no es válida."));
            return;
        }

        // El 'sub' ya viene firmado por el manejador del JWT, así que la base de datos lo ve en
        // app.usuario_id desde la primera escritura de la petición (la de última actividad).
        var principalUserId = Guid.TryParse(principal.FindFirst("sub")?.Value, out var usuarioId)
            ? usuarioId
            : (Guid?)null;
        contextoSesion.UsuarioId = principalUserId;

        var resultado = await sesiones.ValidarAsync(sesionId, http.RequestAborted);
        if (!resultado.Valida)
        {
            contextoSesion.UsuarioId = null;
            await Problemas.EscribirAsync(http, Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                resultado.CodigoReferencia!,
                "No autenticado",
                resultado.Detalle!));
            return;
        }

        // SEC-015: con el cambio de contraseña pendiente solo se permite lo necesario para hacerlo.
        if (resultado.DebeCambiarPassword && !AccionesDeCuenta.Son(endpoint))
        {
            await Problemas.EscribirAsync(http, Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.CambioPasswordRequerido,
                "Cambio de contraseña pendiente",
                "Debes cambiar tu contraseña antes de seguir usando la aplicación."));
            return;
        }

        // El rol sale de la tabla del usuario, no del claim: si el rol cambia, el cambio vale desde la
        // siguiente petición, sin esperar a que caduque el token. Es lo que lee RLS en app.rol.
        contextoSesion.Rol = principalUserId is { } idConRol
            ? await politica.RolDeAsync(idConRol, http.RequestAborted) ?? string.Empty
            : string.Empty;

        await siguiente(http);
    }
}

/// <summary>
/// Endpoints permitidos con el cambio de contraseña pendiente: identidad, cierre de sesión y el
/// propio cambio. Un endpoint sin <c>[Accion]</c> no cuenta (lo rechaza el filtro global).
/// </summary>
internal static class AccionesDeCuenta
{
    private static readonly HashSet<string> Permitidas =
        [Acciones.SesionVer, Acciones.SesionCerrar, Acciones.SesionPassword];

    public static bool Son(Endpoint endpoint)
    {
        var acciones = endpoint.Metadata.GetMetadata<AccionAttribute>()?.Acciones ?? [];
        return acciones.Length > 0 && acciones.All(Permitidas.Contains);
    }
}

/// <summary>
/// Comprueba el doble envío del token de CSRF: la cabecera <c>X-CSRF-Token</c> tiene que coincidir
/// con la cookie <c>atlas-rent-csrf</c> en los métodos que modifican estado (docs/SEGURIDAD.md A.2).
/// <para>
/// F6-01: se salta cuando la petición viene con un token de agente resuelto. No es una excepción
/// global ni un atributo de endpoint: solo afecta a peticiones que no llevan cookie de sesión,
/// que es justamente lo que el token de CSRF protege (una petición de navegador que se aprovecha
/// de la sesión del usuario). La cabecera <c>Authorization</c> no la puede poner sola un sitio de
/// terceros, y el token de agente no vive en una cookie sino en el llamante.
/// </para>
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate siguiente)
{
    private static readonly string[] MetodosSeguros = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task InvokeAsync(HttpContext http, ContextoSesion contextoSesion)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ApiControllerAttribute>() is not null
            && endpoint.Metadata.GetMetadata<SaltarCsrfAttribute>() is null
            && !contextoSesion.EsAgente
            && !MetodosSeguros.Contains(http.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            var cabecera = http.Request.Headers["X-CSRF-Token"].ToString();
            var cookie = http.Request.Cookies[NombresCookies.Csrf];

            if (string.IsNullOrEmpty(cookie)
                || string.IsNullOrEmpty(cabecera)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(cabecera), Encoding.UTF8.GetBytes(cookie)))
            {
                await Problemas.EscribirAsync(http, Problemas.Construir(
                    StatusCodes.Status403Forbidden,
                    Problemas.CsrfInvalido,
                    "Petición rechazada",
                    "La cabecera X-CSRF-Token no coincide con la cookie de CSRF."));
                return;
            }
        }

        await siguiente(http);
    }
}

/// <summary>
/// Marca el único endpoint que se salta la comprobación de CSRF: el login. No hay token de CSRF
/// que leer porque todavía no hay sesión ni cookie; exigirlo dejaría el primer acceso imposible.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SaltarCsrfAttribute : Attribute;