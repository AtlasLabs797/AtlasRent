using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AtlasRent.Api.Middleware;

/// <summary>
/// F6-01: resuelve el token de agente que viaja en <c>Authorization: Bearer</c> y fija el
/// <see cref="ContextoSesion"/> con el usuario propietario y su rol (docs/DOC_TECNICA.md §7).
/// <para>
/// El agente corre <b>como su propio usuario</b>, no como un rol nuevo: por eso el contexto que fija
/// es el mismo que fija <see cref="ValidacionSesionMiddleware"/> y por eso RLS se comporta igual que
/// en la sesión del propietario. No hay <c>[AllowAnonymous]</c> en ninguna ruta de agente: el filtro
/// global de acciones y las políticas siguen aplicando.
/// </para>
/// <para>
/// Con el token resuelto se hace un tercer comprobación aparte, la de la lista de acciones del token
/// (<see cref="FiltroAccionAgente"/>). Las otras dos, techo del rol y recorte por usuario, las hace
/// <see cref="IPolicyService"/> como en cualquier otra petición.
/// </para>
/// <para>
/// <b>CSRF: no aplica y no se salta nada.</b> La autenticación del agente no es la cookie, así que no
/// hay cabecera <c>X-CSRF-Token</c> que comparar ni peticiones de navegador que falsificar:
/// <see cref="CsrfMiddleware"/> deja pasar estas peticiones y solo estas, sin tocar las que van con
/// cookie. Que hoy las rutas de agente sean de lectura es consecuencia del alcance de F6-01, no una
/// garantía: cuando F6-03 añada las propuestas, estas rutas seguirán exigiendo acción, techo de rol
/// y lista del token.
/// </para>
/// </summary>
public sealed class AutenticacionAgenteMiddleware(RequestDelegate siguiente)
{
    private const string Prefijo = "Bearer ";

    public async Task InvokeAsync(
        HttpContext http,
        ContextoSesion contextoSesion,
        ServicioAgentes agentes,
        ServicioBloqueos bloqueos,
        IPolicyService politica,
        IResolutorIpPeticion resolutorIp)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ApiControllerAttribute>() is null)
        {
            await siguiente(http);
            return;
        }

        var cabecera = http.Request.Headers.Authorization.ToString();
        if (!cabecera.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase))
        {
            await siguiente(http);
            return;
        }

        var ct = http.RequestAborted;
        var agente = await agentes.ResolverAsync(cabecera[Prefijo.Length..].Trim(), ct);
        if (agente is null)
        {
            // Desconocido, revocado, caducado o de un usuario que ya no puede entrar: el mismo 401
            // para los cuatro, sin decir en cuál falló y sin dejar rastro del valor recibido.
            // SEC-029: cuenta como fallo de la IP (el mismo contador que el login), sin usuario ni
            // nombre: ni el token ni nada derivado de él se guarda.
            await bloqueos.RegistrarIntentoFallidoAsync(null, null, resolutorIp.Resolver(http), ct);
            await Problemas.EscribirAsync(http, Problemas.Construir(
                StatusCodes.Status401Unauthorized,
                Problemas.TokenAgenteInvalido,
                "No autenticado",
                "El token de agente no es válido."));
            return;
        }

        contextoSesion.UsuarioId = agente.UsuarioId;
        // El rol sale de la tabla del usuario, igual que en la sesión: nunca de un claim.
        contextoSesion.Rol = await politica.RolDeAsync(agente.UsuarioId, ct) ?? string.Empty;
        contextoSesion.EsAgente = true;
        contextoSesion.AgenteTokenId = agente.TokenId;
        contextoSesion.AgenteAcciones = agente.Acciones;

        // SEC-029: la fila de auditoría se escribe aunque el resto del pipeline lance; si lanza, la
        // petición se audita como denegada (el 500 aún no está en la respuesta).
        var fallo = true;
        try
        {
            await siguiente(http);
            fallo = false;
        }
        finally
        {
            var resultado = !fallo && http.Response.StatusCode < 400 ? "permitido" : "denegado";
            var ruta = endpoint.Metadata.GetMetadata<RouteEndpoint>()?.RoutePattern.RawText
                ?? http.Request.Path.Value
                ?? string.Empty;

            // CancellationToken.None: si el cliente cortó, la petición igualmente ocurrió.
            await agentes.MarcarUsoAsync(agente.TokenId, CancellationToken.None);
            await agentes.AuditarPeticionAsync(
                agente.TokenId,
                agente.UsuarioId,
                http.Request.Method,
                ruta,
                resultado,
                resolutorIp.Resolver(http),
                CancellationToken.None);
        }
    }
}
