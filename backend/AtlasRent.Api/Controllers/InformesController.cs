using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Informes económicos y dashboard (F5-01, docs/ESPECIFICACION.md §3.15).
/// <para>
/// <b>Los dos endpoints piden <c>informe.ver</c></b>, y no <c>factura.ver</c> como la vista única: aquí
/// no hay lista de documentos sino agregados de dinero, y el empleado no recibe agregados por ningún
/// endpoint (AGENTS.md §6). En la vista única los totales vienen en <c>null</c> para el empleado; aquí el
/// endpoint entero no existe para él, que es la misma regla escrita donde no se puede esquivar.
/// </para>
/// <para>
/// Los bloques se calculan sobre las filas de la vista única de §3.13, así que el informe y la tabla
/// dicen lo mismo. La consolidación en dólares usa el tipo de cambio congelado de cada documento
/// (D9, §3.12) y nunca el del día.
/// </para>
/// <para>
/// <b>F5-02 añadió la exportación</b> a este mismo controller y con el mismo permiso: es el mismo
/// informe en otra forma, y por eso se calculan sobre las mismas filas.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/informes")]
public sealed class InformesController(
    ServicioInformes informes,
    ServicioExportacionInformes exportacion) : ControllerBase
{
    /// <summary>
    /// El resumen económico de §3.15: proformado, facturado, cobrado, retenido, pendiente, vencido,
    /// fianzas retenidas y diferencias de cambio, más la serie mensual del gráfico.
    /// <para>
    /// <b>Filtros:</b> <c>desde</c>, <c>hasta</c>, <c>serieId</c>, <c>moneda</c>, <c>estado</c>,
    /// <c>clienteId</c>, <c>unidadId</c>, <c>contratoId</c>, <c>inmuebleId</c>, <c>propietariaId</c> y
    /// <c>facturadoraId</c>. Los que también acepta la vista única se aplican en ella; <c>contratoId</c>,
    /// que no es filtro de la vista, se aplica sobre las filas. <c>hasta</c> es además la fecha de corte
    /// de lo vencido, y sale en la respuesta como <c>fechaCorte</c> para que se sepa contra qué día se
    /// calculó.
    /// </para>
    /// </summary>
    [HttpGet("resumen")]
    [Accion(Acciones.InformeVer)]
    public async Task<IActionResult> Resumen(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? serieId,
        [FromQuery] Moneda? moneda,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] Guid? contratoId,
        [FromQuery] Guid? inmuebleId,
        [FromQuery] Guid? propietariaId,
        [FromQuery] Guid? facturadoraId,
        CancellationToken ct) =>
        Ok(await informes.ResumenAsync(
            new FiltroInformes(
                desde, hasta, serieId, moneda, estado, clienteId, unidadId, contratoId, inmuebleId,
                propietariaId, facturadoraId),
            ct));

    /// <summary>
    /// El dashboard de §3.15: unidades disponibles, reservadas, ocupadas y en mantenimiento, contratos
    /// activos y los mismos bloques económicos del resumen, con los mismos filtros.
    /// </summary>
    /// <param name="fecha">Día del corte. Por defecto, hoy en Santo Domingo.</param>
    [HttpGet("dashboard")]
    [Accion(Acciones.InformeVer)]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateOnly? fecha,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? serieId,
        [FromQuery] Moneda? moneda,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] Guid? contratoId,
        [FromQuery] Guid? inmuebleId,
        [FromQuery] Guid? propietariaId,
        [FromQuery] Guid? facturadoraId,
        CancellationToken ct) =>
        Ok(await informes.DashboardAsync(
            new FiltroInformes(
                desde, hasta, serieId, moneda, estado, clienteId, unidadId, contratoId, inmuebleId,
                propietariaId, facturadoraId),
            fecha,
            ct));

    /// <summary>
    /// La exportación a Excel o CSV de §3.15: el detalle de facturas, proformas y fianzas del
    /// informe, con los mismos once filtros que el resumen.
    /// </summary>
    /// <param name="formato">
    /// <c>xlsx</c> (libro de Excel con una hoja de detalle y otra de resumen económico) o <c>csv</c>
    /// (UTF-8 con BOM y separador <c>;</c>). Cualquier otro valor es un 400 de validación.
    /// </param>
    /// <remarks>
    /// Es un adjunto: por eso el permiso es <c>informe.ver</c> y no <c>factura.ver</c>, igual que el
    /// resumen. Lo que sale son las filas de la vista única, con los importes como números y las
    /// fechas como fechas, y con todo el texto externo neutralizado para que no se convierta en
    /// fórmula al abrir el archivo (SEC-005).
    /// </remarks>
    [HttpGet("exportar")]
    [Accion(Acciones.InformeVer)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv")]
    public async Task<IActionResult> Exportar(
        [FromQuery] FormatoInforme formato,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? serieId,
        [FromQuery] Moneda? moneda,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] Guid? contratoId,
        [FromQuery] Guid? inmuebleId,
        [FromQuery] Guid? propietariaId,
        [FromQuery] Guid? facturadoraId,
        CancellationToken ct)
    {
        var archivo = await exportacion.ExportarAsync(
            new FiltroInformes(
                desde, hasta, serieId, moneda, estado, clienteId, unidadId, contratoId, inmuebleId,
                propietariaId, facturadoraId),
            formato,
            ct);

        return File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
    }
}