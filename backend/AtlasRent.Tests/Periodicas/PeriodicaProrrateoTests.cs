using System.Globalization;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: el prorrateo de la renta mensual (docs/ESPECIFICACION.md §3.8).
/// <para>
/// <b>Misma renta, mismo día de inicio y mismo día de fin pactado en los cuatro meses.</b> Cada caso
/// tiene su contrato porque un contrato no puede empezar el día 20 de cuatro meses distintos a la
/// vez: lo pactado es «empieza el día 20» y «dura un bloque de doce meses», y lo único que cambia de
/// un mes a otro es cuántos días tiene ese mes. Por eso los cuatro importes tienen que salir
/// distintos: es el criterio de aceptación del plan, «prorrateo correcto en meses 28-31», y una
/// prueba con un solo mes no lo cumpliría.
/// </para>
/// <para>
/// Los números esperados están escritos a mano, no calculados con la fórmula del código: si la
/// fórmula cambiara, la prueba también.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaProrrateoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Los cuatro casos del criterio de aceptación, con los números que salen a mano:
    /// <list type="bullet">
    /// <item>febrero de 2024, 29 días, facturables del 20 al 29: 1000 / 29 × 10 = 344,83</item>
    /// <item>enero de 2026, 31 días, del 20 al 31: 1000 / 31 × 12 = 387,10</item>
    /// <item>febrero de 2026, 28 días, del 20 al 28: 1000 / 28 × 9 = 321,43</item>
    /// <item>abril de 2026, 30 días, del 20 al 30: 1000 / 30 × 11 = 366,67</item>
    /// </list>
    /// <para>
    /// Todos redondean medio hacia arriba (D27) y ninguno de los cuatro coincide con otro: si el
    /// prorrateo usara 30 días fijos, el de febrero saldría distinto de lo que dice aquí.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(2024, 2, 29, 10, 344.83)]
    [InlineData(2026, 1, 31, 12, 387.10)]
    [InlineData(2026, 2, 28, 9, 321.43)]
    [InlineData(2026, 4, 30, 11, 366.67)]
    public async Task ElProrrateoSaleDeLosDiasRealesDeCadaMes(
        int anio,
        int mes,
        int diasDelMes,
        int diasFacturables,
        decimal importeEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        // Un contrato por mes, todos con la misma renta y todos empezando el día 20: lo pactado no
        // cambia, el divisor sí.
        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, new DateOnly(anio, mes, 20), duracionMeses: 12);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var periodo = new DateOnly(anio, mes, 1);

        var simulacion = await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(periodo, serieId, [contratoId]), ct);

        var item = AyudaPeriodicas.UnItem(simulacion);
        item.GetProperty("diasDelMes").GetInt32().Should().Be(diasDelMes);
        item.GetProperty("diasFacturables").GetInt32().Should().Be(diasFacturables);
        item.GetProperty("prorrateado").GetBoolean().Should().BeTrue();
        item.GetProperty("importe").LeerDecimal().Should().Be(importeEsperado);

        // Y lo que sale de verdad, leído de la factura en la base.
        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(periodo, serieId, [contratoId]), ct);

        var facturas = await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, periodo, ct);
        var factura = facturas.Should().ContainSingle("un mes, una factura").Subject;

        factura.Total.Should().Be(importeEsperado);
        factura.Estado.Should().Be(EstadoFactura.Emitida);
        factura.Subtotal.Should().Be(importeEsperado, "la renta no lleva impuesto por línea todavía");
    }

    /// <summary>
    /// Un mes entero se factura entero: no se prorratea y el importe es la renta pactada, sin pasar
    /// por la división. Un contrato que empieza el día 1 y acaba pasado el mes tiene los 31 días.
    /// </summary>
    [Fact]
    public async Task UnContratoDeMesCompletoPagaLaRentaIntegra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var renta = 4_321.99m;
        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, new DateOnly(2026, 1, 1), renta: renta);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var periodo = new DateOnly(2026, 1, 1);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(periodo, serieId, [contratoId]), ct));

        item.GetProperty("diasFacturables").GetInt32().Should().Be(31);
        item.GetProperty("diasDelMes").GetInt32().Should().Be(31);
        item.GetProperty("prorrateado").GetBoolean().Should().BeFalse("no hay nada que prorratear");
        item.GetProperty("importe").LeerDecimal().Should().Be(renta);

        await AyudaPeriodicas.GenerarAsync(admin, AyudaPeriodicas.Peticion(periodo, serieId, [contratoId]), ct);

        var factura = (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, periodo, ct))
            .Should().ContainSingle().Subject;

        factura.Total.Should().Be(renta);
    }

    /// <summary>
    /// El día de pago cae dentro del periodo facturado, y el mes se recorta cuando el día de pago
    /// no existe en él: el 31 de un mes de 30 vence el último día de ese mes.
    /// </summary>
    [Fact]
    public async Task ElVencimientoEsElDiaDePagoDentroDelPeriodoYSeRecortaAlFinDelMes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        // Un contrato que empieza el día 1 y sigue vigente: el mes va entero y el día de pago 31
        // tiene que recortarse al último día del mes que se está facturando.
        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24, diaPago: 31);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct));

        var vencimiento = DateOnly.Parse(
            item.GetProperty("vencimiento").GetString()!, CultureInfo.InvariantCulture);

        vencimiento.Should().Be(mes.Fin,
            "el día de pago del contrato se recorta al último día del mes que se factura");
    }

    /// <summary>
    /// Facturar un mes con el día de pago ya pasado no entrega una factura que vence dentro del mes
    /// facturado: vence en el ciclo siguiente, el mismo día de pago en el mes que viene. Es la regla
    /// que el orquestador fijó para F3-05, la misma que §3.8 aplica a los suministros.
    /// <para>
    /// <b>Y ahí se queda.</b> Un ciclo solo avanza un mes: si además se factura con más de un mes de
    /// retraso, la factura nace con el vencimiento ya pasado. La regla evita el caso normal —se factura
    /// el mes siguiente al que terminó— y no pretende arreglar la facturación muy tardía, que es una
    /// decisión de negocio que todavía no está escrita.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FacturarUnMesConElDiaDePagoPasadoVenceEnElCicloSiguiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);
        var mesPasado = PeriodoFacturacion.DelMes(hoy.AddMonths(-1));

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mesPasado.Inicio, duracionMeses: 24, diaPago: 5);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mesPasado.Inicio, serieId, [contratoId]), ct));

        var vencimiento = DateOnly.Parse(
            item.GetProperty("vencimiento").GetString()!, CultureInfo.InvariantCulture);

        vencimiento.Should().Be(new DateOnly(mes.Inicio.Year, mes.Inicio.Month, 5),
            "el mismo día de pago, en el mes que viene: el del periodo facturado ya pasó");
        vencimiento.Should().NotBe(new DateOnly(mesPasado.Inicio.Year, mesPasado.Inicio.Month, 5),
            "vencer el día que ya pasó sería entregar la factura con la mora puesta de salida");

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mesPasado.Inicio, serieId, [contratoId]), ct);

        var factura = (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mesPasado.Inicio, ct))
            .Should().ContainSingle().Subject;

        factura.FechaVencimiento.Should().Be(vencimiento);
    }

    /// <summary>
    /// Un contrato cuyo plazo no toca el mes no es candidato (§3.8 factura los contratos del mes
    /// natural): no sale en la simulación ni entre los omitidos, y no factura nada. Ni una factura de
    /// cero ni una línea de ruido en la lista de omitidos.
    /// </summary>
    [Fact]
    public async Task UnContratoQueNoCubreElMesNiApareceNiSeFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy.AddMonths(6));

        // El contrato empieza el mes siguiente al que se factura: no le toca ni un día, porque el
        // primer día del mes siguiente ya está fuera.
        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Fin.AddDays(1), duracionMeses: 12);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var resultado = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        resultado.GetProperty("creadas").GetInt32().Should().Be(0);
        resultado.GetProperty("omitidas").GetInt32().Should().Be(0,
            "no es candidato: su plazo no toca el mes, así que no hay nada que explicar");

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Una serie con la moneda fijada no admite contratos de otra (D7), y la simulación lo dice en
    /// vez de emitir un 409 a mitad del lote.
    /// </summary>
    [Fact]
    public async Task UnaSerieDeMonedaFijaDejaFueraElContratoDeOtraMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24, moneda: Moneda.USD);

        var seriePesos = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.DOP, ct);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, seriePesos, [contratoId]), ct));

        item.GetProperty("seEmite").GetBoolean().Should().BeFalse();
        item.GetProperty("motivoOmision").GetString().Should().Contain("D7");

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct))
            .Should().BeEmpty("una serie de pesos no factura un contrato en dólares");
    }
}