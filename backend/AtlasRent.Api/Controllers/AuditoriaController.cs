using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Lectura de la auditoría (docs/ESPECIFICACION.md §2: administrador y gerente). Solo lectura; no se
/// audita a sí misma (no hay convención de auditar lecturas).
/// </summary>
[ApiController]
[Route("api/v1/auditoria")]
public sealed class AuditoriaController(
    ServicioLecturaAuditoria lectura,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.AuditoriaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? usuarioId,
        [FromQuery] string? entidad,
        [FromQuery] string? entidadId,
        [FromQuery] string? accion,
        CancellationToken ct)
    {
        if (desde is { } d && hasta is { } h && d > h)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Rango de fechas no válido",
                "'desde' no puede ser posterior a 'hasta'.");
        }

        return await EnvoltorioAsync(lectura.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir ?? (sortBy is null ? ConsultaLista.OrdenDescendente : null)),
            desde,
            hasta,
            usuarioId,
            entidad,
            entidadId,
            accion,
            ct));
    }
}
