using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Catálogos de solo lectura para elegir al facturar y cobrar. Los endpoints de <c>api/v1/config</c>
/// siguen siendo del administrador; aquí cada lista pide la acción mínima de quien la usa y solo
/// devuelve elementos activos con los campos justos para elegir.
/// <para>
/// Sin <c>pageSize</c> devuelven hasta 200 elementos (el máximo), que cubre cualquier catálogo real.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/catalogos")]
public sealed class CatalogosController(
    ServicioCatalogos catalogos,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Series activas. Acción <c>factura.borrador</c>: es la más baja de los endpoints que reciben un
    /// serieId (crear borrador, <c>factura.borrador</c>; periódica y emitir, <c>factura.emitir</c>;
    /// convertir proforma y rehacer, <c>factura.anular</c>). Todo el que tiene alguna de las demás
    /// la tiene también en el techo del rol.
    /// </summary>
    [HttpGet("series")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Series(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(catalogos.SeriesAsync(Lista(page, pageSize, sortBy, sortDir), ct));

    [HttpGet("formas-pago")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> FormasPago(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(catalogos.FormasPagoAsync(Lista(page, pageSize, sortBy, sortDir), ct));

    [HttpGet("cuentas-receptoras")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> CuentasReceptoras(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(catalogos.CuentasReceptorasAsync(Lista(page, pageSize, sortBy, sortDir), ct));

    /// <summary>Id y nombre, para mostrar propietaria/facturadora en la vista de cuentas.</summary>
    [HttpGet("subempresas")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Subempresas(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(catalogos.SubempresasAsync(Lista(page, pageSize, sortBy, sortDir), ct));

    [HttpGet("parametros-cobro")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> ParametrosCobro(CancellationToken ct) =>
        Ok(await catalogos.ParametrosCobroAsync(ct));

    private static ConsultaLista Lista(int? page, int? pageSize, string? sortBy, string? sortDir) =>
        Paginacion(page, pageSize ?? ConsultaLista.PageSizeMaximo, sortBy, sortDir);
}
