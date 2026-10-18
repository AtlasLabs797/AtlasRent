using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AtlasRent.Api.Authorization;

/// <summary>
/// F6-01: tercera mitad del doble techo de una petición hecha con token de agente.
/// <para>
/// <see cref="FiltroAccionGlobal"/> ya exige que el propietario tenga la acción por su rol y dentro
/// de su techo. Aquí se exige además que la acción esté en la lista del propio token: el token
/// recorta, nunca amplía. Fuera de una petición de agente este filtro no hace nada, así que la
/// sesión normal sigue dependiendo solo de <see cref="FiltroAccionGlobal"/>.
/// </para>
/// </summary>
public sealed class FiltroAccionAgente(IPolicyService politica, ContextoSesion sesion) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        if (!sesion.EsAgente)
        {
            return;
        }

        var metadatos = contexto.ActionDescriptor.EndpointMetadata;
        if (metadatos.Any(metadata => metadata is AllowAnonymousAttribute))
        {
            return;
        }

        var acciones = metadatos.OfType<AccionAttribute>().FirstOrDefault()?.Acciones ?? [];
        var denegada = acciones.FirstOrDefault(accion => !sesion.AgenteAcciones.Contains(accion));
        if (denegada is null)
        {
            return;
        }

        var recurso = $"{contexto.HttpContext.Request.Method} {contexto.ActionDescriptor.AttributeRouteInfo?.Template}";
        await politica.AuditarDenegadoAsync(denegada, recurso, contexto.HttpContext.RequestAborted);

        contexto.Result = Problemas.Construir(
            StatusCodes.Status403Forbidden,
            Problemas.PermisoDenegado,
            "Permiso denegado",
            "Este token de agente no tiene la acción que pide la operación.");
    }
}