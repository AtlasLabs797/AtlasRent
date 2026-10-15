using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// F3-02: el cálculo por línea y la reescritura del borrador.
/// <para>
/// El caso que importa es el del ITBIS: dos líneas con el mismo porcentaje dan un resultado
/// distinto al que sale de redondear el subtotal entero, y la diferencia se ve en los céntimos.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaCalculoYLineasTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// El ITBIS de la factura es la suma de los ITBIS de línea ya redondeados, no el redondeo del
    /// subtotal (D27, §3.8).
    /// <para>
    /// Las dos líneas van al 18 % y están elegidas para que las dos cosas den números distintos:
    /// línea a línea, 18,00 + 0,05 = 18,05; sobre el subtotal, 100,31 × 18 % = 18,0558 → 18,06.
    /// El céntimo de diferencia es justo el que se pierde si el impuesto se calcula sobre el total.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElItbisDeLaFacturaEsLaSumaDeLosItbisDeLineaYaRedondeados()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var tipoItbis = await AyudaFacturas.CrearTipoItbisAsync(sesion, 18m, ct);

        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        await AyudaFacturas.AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, 100.005m, tipoItbis, ct);
        var factura = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Suministro, 1m, 0.30m, tipoItbis, ct);

        factura.GetProperty("subtotal").GetDecimal().Should().Be(100.31m);
        factura.GetProperty("itbis").GetDecimal().Should().Be(18.05m);
        factura.GetProperty("total").GetDecimal().Should().Be(118.36m);

        // Lo que habría dado calcular el impuesto sobre el subtotal entero. Se escribe para que se
        // vea la diferencia: si algún día el total sale 18,06, el cálculo ha vuelto a la factura
        // completa y esta prueba lo canta.
        var sobreElSubtotal = Math.Round(100.31m * 0.18m, 2, MidpointRounding.AwayFromZero);
        sobreElSubtotal.Should().Be(18.06m);
        sobreElSubtotal.Should().NotBe(18.05m, "por eso esta prueba existe");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var lineas = await contexto.FacturaLinea.AsNoTracking()
            .Where(l => l.FacturaId == facturaId)
            .OrderBy(l => l.Posicion)
            .Select(l => new { l.ItbisImporte, l.PorcentajeItbis })
            .ToListAsync(ct);

        lineas.Select(l => l.ItbisImporte).Should().Equal(18.00m, 0.05m);
        lineas.Select(l => l.PorcentajeItbis).Should().Equal(18m, 18m);

        // El subtotal es la suma algebraica de los importes: un descuento entra con su signo.
        (await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct)).Total
            .Should().Be(118.36m);
    }

    /// <summary>
    /// El descuento es una línea de importe negativo y el subtotal es la suma algebraica
    /// (docs/ESPECIFICACION.md §3.8). Un descuento con cantidad positiva se rechaza: el signo lo
    /// pone el tipo de la línea, no el que se le ocurra a quien la escribe.
    /// </summary>
    [Fact]
    public async Task UnDescuentoRestaDelSubtotalYNoPuedeSubirlo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);

        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        await AyudaFacturas.AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        var conDescuento = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Descuento, 1m, -10m, null, ct);

        conDescuento.GetProperty("subtotal").GetDecimal().Should().Be(90m);
        conDescuento.GetProperty("itbis").GetDecimal().Should().Be(0m);
        conDescuento.GetProperty("total").GetDecimal().Should().Be(90m);

        var linea = conDescuento.GetProperty("lineas")[1];
        linea.GetProperty("tipo").GetString().Should().Be("Descuento");
        linea.GetProperty("importe").GetDecimal().Should().Be(-10m);

        var descuentoEnPositivo = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas",
            new { tipo = "Descuento", concepto = "Descuento al revés", cantidad = 1m, precioUnitario = 10m },
            ct);

        descuentoEnPositivo.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// En borrador la factura se reescribe entera, incluidas sus líneas: una factura en borrador no
    /// tiene número, ni comprobante, ni deuda, así que no hay nada que perder por corregirla.
    /// Emitida, cualquier cambio es un 409: lo que se hace con una factura emitida es anularla
    /// (F3-08), no dejarla descuadrada.
    /// </summary>
    [Fact]
    public async Task EnBorradorLasLineasSeEditanYSeBoranYEmitidaNoSeTocan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        var conDosLineas = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        conDosLineas = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Servicio, 2m, 25m, null, ct);
        conDosLineas.GetProperty("total").GetDecimal().Should().Be(150m);

        var primera = conDosLineas.GetProperty("lineas")[0].GetProperty("id").GetGuid();

        var editada = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas/{primera:D}",
            new { tipo = "Renta", concepto = "Renta del mes", cantidad = 1m, precioUnitario = 200m },
            ct);

        editada.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpoEditado = await editada.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoEditado.GetProperty("total").GetDecimal().Should().Be(250m);

        var borrada = await sesion.DeleteAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas/{primera:D}", ct);

        borrada.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpoBorrado = await borrada.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoBorrado.GetProperty("lineas").GetArrayLength().Should().Be(1);
        cuerpoBorrado.GetProperty("total").GetDecimal().Should().Be(50m);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK);

        var editarEmitida = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas/{primera:D}",
            new { tipo = "Renta", concepto = "Cambio tardío", cantidad = 1m, precioUnitario = 999m },
            ct);
        editarEmitida.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var editarCabecera = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}",
            new
            {
                clienteId,
                serieId,
                moneda = "USD",
                fechaVencimiento = hoy.AddDays(60).ToString("yyyy-MM-dd"),
                concepto = "Cambio tardío de la cabecera",
            },
            ct);
        editarCabecera.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        fila.Total.Should().Be(50m, "los intentos de cambio no tocaron la factura");
        fila.Estado.Should().Be(EstadoFactura.Emitida);
    }
}
