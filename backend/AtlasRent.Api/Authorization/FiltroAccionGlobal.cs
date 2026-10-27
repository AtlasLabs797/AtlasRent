using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AtlasRent.Api.Authorization;

/// <summary>
/// Política global de denegar por omisión (docs/DOC_TECNICA.md §6): todo endpoint de controlador
/// tiene que declarar su <c>[Accion]</c>, y el que no la declara se rechaza.
/// <para>
/// Es lo que evita que el próximo endpoint que se añada quede abierto porque nadie se acordó del
/// permiso. Los <c>[AllowAnonymous]</c> (login, refresco y segundo factor) se saltan la comprobación
/// porque en ese camino todavía no hay sesión que validar.
/// </para>
/// </summary>
public sealed class FiltroAccionGlobal(IPolicyService politica) : IAsyncAuthorizationFilter
{
    /// <summary>Acción con la que se audita un endpoint que no declara ninguna.</summary>
    public const string SinAccion = "endpoint.sin_accion";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var metadatos = contexto.ActionDescriptor.EndpointMetadata;
        if (metadatos.Any(metadata => metadata is AllowAnonymousAttribute))
        {
            return;
        }

        var recurso = $"{contexto.HttpContext.Request.Method} {contexto.ActionDescriptor.AttributeRouteInfo?.Template}";
        // Los metadatos van de lo general a lo particular (clase y después método): manda el último.
        var declaracion = metadatos.OfType<AccionAttribute>().LastOrDefault();

        try
        {
            // Sin declaración no hay acciones que exigir, y eso es justo el caso que se rechaza: un
            // endpoint sin [Accion] no tiene permiso para nada.
            var acciones = declaracion?.Acciones ?? [];
            if (acciones.Length == 0)
            {
                throw new PermisoDenegadoException(SinAccion, recurso);
            }

            await politica.CargarAsync(contexto.HttpContext.RequestAborted);
            foreach (var accion in acciones)
            {
                politica.Exigir(accion, recurso);
            }
        }
        catch (PermisoDenegadoException denegado)
        {
            await politica.AuditarDenegadoAsync(denegado.Accion, recurso, contexto.HttpContext.RequestAborted);

            contexto.Result = Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
    }
}