using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lecturas mínimas de configuración para que quien factura o cobra pueda elegir, sin darle acceso a
/// la pantalla de configuración. Solo elementos activos.
/// </summary>
public sealed class ServicioCatalogos(AtlasRentDbContext contexto, ServicioConfiguracion configuracion)
{
    private static readonly Dictionary<string, Func<IQueryable<Serie>, bool, IOrderedQueryable<Serie>>> OrdenesSerie =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["numero"] = (q, desc) => desc ? q.OrderByDescending(s => s.Numero) : q.OrderBy(s => s.Numero),
        };

    private static readonly Dictionary<string, Func<IQueryable<FormaPago>, bool, IOrderedQueryable<FormaPago>>> OrdenesFormaPago =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(f => f.Nombre) : q.OrderBy(f => f.Nombre),
        };

    private static readonly Dictionary<string, Func<IQueryable<CuentaReceptora>, bool, IOrderedQueryable<CuentaReceptora>>> OrdenesCuenta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(c => c.Nombre) : q.OrderBy(c => c.Nombre),
        };

    private static readonly Dictionary<string, Func<IQueryable<Subempresa>, bool, IOrderedQueryable<Subempresa>>> OrdenesSubempresa =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(s => s.Nombre) : q.OrderBy(s => s.Nombre),
        };

    public Task<Pagina<SerieCatalogo>> SeriesAsync(ConsultaLista p, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.Serie.AsNoTracking().Where(s => s.Activa), OrdenesSerie, "numero", p,
            s => new SerieCatalogo(s.Id, s.Numero, s.Prefijo, s.Moneda, s.Tipo, s.GeneraComprobante), ct);

    public Task<Pagina<FormaPagoCatalogo>> FormasPagoAsync(ConsultaLista p, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.FormaPago.AsNoTracking().Where(f => f.Activa), OrdenesFormaPago, "nombre", p,
            f => new FormaPagoCatalogo(f.Id, f.Nombre), ct);

    public Task<Pagina<CuentaReceptoraCatalogo>> CuentasReceptorasAsync(ConsultaLista p, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.CuentaReceptora.AsNoTracking().Where(c => c.Activa), OrdenesCuenta, "nombre", p,
            c => new CuentaReceptoraCatalogo(c.Id, c.Nombre, c.Banco, c.Moneda), ct);

    public Task<Pagina<SubempresaCatalogo>> SubempresasAsync(ConsultaLista p, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.Subempresa.AsNoTracking().Where(s => s.Activa), OrdenesSubempresa, "nombre", p,
            s => new SubempresaCatalogo(s.Id, s.Nombre), ct);

    /// <summary>Misma fuente que <c>ServicioCobros</c>: <c>ajuste_general</c> vía <see cref="ServicioConfiguracion"/>.</summary>
    public async Task<ParametrosCobroRespuesta> ParametrosCobroAsync(CancellationToken ct)
    {
        var ajustes = await configuracion.LeerAjustesAsync(ct);

        return new ParametrosCobroRespuesta(ajustes.ToleranciaCambioUsd, ajustes.ToleranciaCambioDop);
    }

    public async Task<ModoRecordatoriosRespuesta> ModoRecordatoriosAsync(CancellationToken ct) =>
        new((await configuracion.LeerAjustesAsync(ct)).ModoRecordatorios);
}
