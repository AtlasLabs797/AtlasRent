using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Bandeja de alertas (F2-10, docs/ESPECIFICACION.md §3.16). Todos los roles tienen <c>cliente.ver</c>;
/// lo que cada uno ve y puede cerrar lo decide <see cref="ServicioBandejaAlertas"/> por el tipo de
/// alerta.
/// </summary>
[ApiController]
[Route("api/v1/alertas")]
public sealed class AlertasController(
    ServicioBandejaAlertas alertas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Lista paginada, de la más reciente a la más antigua. <c>estado</c>: <c>abiertas</c> (por defecto),
    /// <c>cerradas</c> o <c>todas</c>. Un <c>tipo</c> que el usuario no puede ver da lista vacía.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? tipo,
        [FromQuery] string? estado,
        CancellationToken ct)
    {
        var estadoNormal = string.IsNullOrWhiteSpace(estado)
            ? ServicioBandejaAlertas.EstadoAbiertas
            : estado.Trim().ToLowerInvariant();
        if (!ServicioBandejaAlertas.EstadoValido(estadoNormal))
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Estado no válido",
                "El estado es abiertas, cerradas o todas.");
        }

        return await EnvoltorioAsync(alertas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir ?? (sortBy is null ? ConsultaLista.OrdenDescendente : null)),
            tipo,
            estadoNormal,
            ct));
    }

    /// <summary>Cierra la alerta. Repetirlo es un 200 que no cambia nada.</summary>
    [HttpPost("{id:guid}/cerrar")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Cerrar(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await alertas.CerrarAsync(id, UsuarioDeLaPeticion(), Ip(), ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
    }
}
