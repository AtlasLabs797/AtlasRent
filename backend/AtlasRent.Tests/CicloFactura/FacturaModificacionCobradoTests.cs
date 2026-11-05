using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// BUG-021: modificar una factura emitida no puede bajar su neto por debajo de lo ya cobrado, porque
/// el exceso quedaría de sobrecobro sin reflejo en ningún sitio.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaModificacionCobradoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task NoSePuedeBajarElNetoPorDebajoDeLoCobrado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);
        await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 1_000m, formaPagoId, cuentaId, ct, (facturaId, 1_000m));

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        var lineaId = leida.GetProperty("lineas")[0].GetProperty("id").GetGuid();

        object Cuerpo(decimal precio) => new
        {
            lineas = new[]
            {
                new
                {
                    lineaId,
                    tipo = nameof(TipoLineaFactura.Renta),
                    concepto = "Renta corregida",
                    cantidad = 1m,
                    precioUnitario = precio,
                },
            },
        };

        var baja = await sesion.PutAsync($"/api/v1/facturas/{facturaId:D}/modificar", Cuerpo(600m), ct);
        baja.StatusCode.Should().Be(HttpStatusCode.Conflict, await baja.Cuerpo());
        (await baja.Cuerpo()).Should().Contain("1000", "el 409 dice el importe ya cobrado");

        (await AyudaCiclo.FilaAsync(bd, facturaId, ct)).Total.Should().Be(1_000m, "no se guardó nada");

        var igual = await sesion.PutAsync($"/api/v1/facturas/{facturaId:D}/modificar", Cuerpo(1_000m), ct);
        igual.StatusCode.Should().Be(HttpStatusCode.OK, await igual.Cuerpo());

        var sube = await sesion.PutAsync($"/api/v1/facturas/{facturaId:D}/modificar", Cuerpo(1_200m), ct);
        sube.StatusCode.Should().Be(HttpStatusCode.OK, "subir el importe sí se puede");
    }
}
