using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// BUG-033: «Rehacer en otra moneda» anula la factura periódica y crea otra; si la nueva no hereda el
/// periodo, la periódica vuelve a facturar ese mes.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaRehacerMonedaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task RehacerEnOtraMonedaHeredaElPeriodoYLaPeriodicaNoRefactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieUsd = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var serieDop = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.DOP, ct);

        var primera = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieUsd, [contratoId]), ct);
        var originalId = primera.GetProperty("facturas")[0].GetProperty("facturaId").GetGuid();

        var rehecha = await admin.PostAsync(
            $"/api/v1/facturas/{originalId:D}/rehacer-moneda",
            new { moneda = nameof(Moneda.DOP), serieId = serieDop, total = 59_500m, motivo = "Moneda equivocada" },
            ct);
        rehecha.StatusCode.Should().Be(HttpStatusCode.OK, await rehecha.Cuerpo());

        var facturas = await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct);
        facturas.Should().HaveCount(2, "la anulada y la rehecha, que hereda el periodo");
        facturas.Count(f => f.Estado != EstadoFactura.Anulada).Should().Be(1);

        var segunda = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieUsd, [contratoId]), ct);

        segunda.GetProperty("creadas").GetInt32().Should().Be(0, "ese mes ya está facturado por la rehecha");
        segunda.GetProperty("omitidas").GetInt32().Should().Be(1);
    }
}
