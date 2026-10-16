using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// F3-04 sobre la API de facturas: la línea de retención, los totales nuevos y el indicador
/// (docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// Lo que se comprueba aquí es lo que la base de datos no puede decir por sí sola: que la base la
/// elige el tipo configurado, que un cliente que no es agente de retención no puede tener una línea
/// de retención, y que una factura corriente no paga nada por todo esto.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RetencionFacturaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Los tres criterios de cálculo por base, sobre la misma factura salvo el tipo. El porcentaje es
    /// uno solo y el que la prueba decide; lo que cambia es <c>base</c>, que es lo que §3.9 deja en
    /// manos de la empresa.
    /// </summary>
    [Fact]
    public async Task LaRetencionSaleDeLaBaseQueDiceElTipoConfigurado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        const decimal porcentaje = 12.5m;

        var tipoItbis = await AyudaFacturas.CrearTipoItbisAsync(sesion, 18m, ct);

        var sobreSubtotal = await ImporteRetenidoAsync(sesion, BaseRetencion.Subtotal, porcentaje, tipoItbis, ct);
        var sobreItbis = await ImporteRetenidoAsync(sesion, BaseRetencion.Itbis, porcentaje, tipoItbis, ct);
        var sobreTotal = await ImporteRetenidoAsync(sesion, BaseRetencion.Total, porcentaje, tipoItbis, ct);

        // La misma factura vale 1.000 de subtotal y 1.180 de total, así que sobre el subtotal el importe
        // retenido tiene que ser el mismo que sobre el total y el ITBIS solo aparece en uno de los tres.
        sobreSubtotal.Should().Be(125m, "1.000 por el 12,5% del subtotal");
        sobreItbis.Should().Be(22.5m, "1.180 menos 1.000 son 180 de ITBIS, y el 12,5% de eso es esto");
        sobreTotal.Should().Be(147.5m, "1.180 por el 12,5% del total");

        sobreSubtotal.Should().BeLessThan(sobreTotal, "sobre el subtotal se retiene menos que sobre el total");
    }

    /// <summary>
    /// El neto a cobrar cuadra con los tres: total menos retención, y el total no baja por retener.
    /// Sin esto, una factura con retención podría verse más corta de lo que es.
    /// </summary>
    [Fact]
    public async Task ElNetoACobrarEsElTotalMenosLaRetencionYElTotalNoBaja()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 7.5m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK);
        var factura = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);

        var subtotal = factura.GetProperty("subtotal").GetDecimal();
        var itbis = factura.GetProperty("itbis").GetDecimal();
        var total = factura.GetProperty("total").GetDecimal();
        var retencion = factura.GetProperty("retencion").GetDecimal();
        var neto = factura.GetProperty("netoACobrar").GetDecimal();

        total.Should().Be(subtotal + itbis, "el total es la factura entera");
        total.Should().Be(1_000m, "retener no baja el total: eso es lo que se le descuenta al cliente después");
        retencion.Should().Be(75m, "el 7,5% del total sobre una base de total");
        neto.Should().Be(total - retencion, "el neto a cobrar es el total menos lo retenido");

        var (retencionGuardada, netoGuardado) = await AyudaRetenciones.TotalesAsync(bd, facturaId, ct);
        retencionGuardada.Should().Be(retencion, "y no es solo lo que devuelve la API: está en la cabecera");
        netoGuardado.Should().Be(neto);
    }

    /// <summary>
    /// La línea de retención es una línea más, en negativo y con el porcentaje congelado. El importe
    /// sale del tipo, no de lo que se escriba, así que el precio que venga en la petición se ignora.
    /// </summary>
    [Fact]
    public async Task LaLineaDeRetencionEsUnaLineaMasEnNegativoConSuTipoYSuBase()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Itbis, 3m, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var tipoItbis = await AyudaFacturas.CrearTipoItbisAsync(sesion, 10m, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 500m, tipoItbis, ct);

        var agregada = await AyudaRetenciones.AgregarLineaRetencionAsync(sesion, facturaId, tipoRetencion, ct);
        agregada.StatusCode.Should().Be(HttpStatusCode.OK);

        var linea = (await agregada.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("lineas").EnumerateArray().Single(l => l.GetProperty("tipo").GetString() == nameof(TipoLineaFactura.Retencion));

        linea.GetProperty("tipoRetencionId").GetGuid().Should().Be(tipoRetencion);
        linea.GetProperty("porcentajeRetencion").GetDecimal().Should().Be(3m, "el porcentaje queda congelado en la línea");
        linea.GetProperty("baseRetencion").GetString().Should().Be(nameof(BaseRetencion.Itbis));
        linea.GetProperty("importe").GetDecimal().Should().Be(-1.5m, "1,50 de ITBIS por el 3%, y en negativo");
        linea.GetProperty("itbisImporte").GetDecimal().Should().Be(0m, "una retención no lleva ITBIS encima");
    }

    /// <summary>
    /// El importe retenido se recalcula cuando cambian los totales de la factura: la base es un
    /// importe de la factura, no de la línea. Si no se recalculara, añadir una línea de renta después de
    /// retener dejaría el neto mal y nadie se enteraría hasta que la factura no cuadrara.
    /// </summary>
    [Fact]
    public async Task AnadirUnaLineaDespuesDeRetenerRecalculaLoRetenido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Subtotal, 10m, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 1_000m, null, ct);
        await AyudaRetenciones.AgregarLineaRetencionAsync(sesion, facturaId, tipoRetencion, ct);

        // Ahora entra una línea más, que dobla la base de la retención.
        var conMasRenta = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 1_000m, null, ct);

        conMasRenta.GetProperty("subtotal").GetDecimal().Should().Be(2_000m);
        conMasRenta.GetProperty("retencion").GetDecimal()
            .Should().Be(200m, "la retención se recalcula sobre el subtotal nuevo, no sobre el de antes");
        conMasRenta.GetProperty("netoACobrar").GetDecimal().Should().Be(1_800m);
    }

    /// <summary>
    /// <b>Un cliente que no es agente de retención no admite la línea</b> (§3.9, D14). No se ignora y
    /// no se guarda: se rechaza con un 409 que dice qué hacer.
    /// </summary>
    [Fact]
    public async Task UnClienteQueNoEsAgenteDeRetencionNoAdmiteLaLineaYEl409DiceQueHacer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: false);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        var agregada = await AyudaRetenciones.AgregarLineaRetencionAsync(sesion, facturaId, tipoRetencion, ct);

        agregada.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "una factura de un cliente que no es agente de retención no puede decir que retuvo");

        var cuerpo = await agregada.Content.ReadAsStringAsync(ct);
        cuerpo.Should().Contain("agente de retención", "el 409 dice en concepto de qué se trata");
        cuerpo.Should().Contain("Clientes", "el mensaje dice dónde se marca, no solo que falta el requisito");

        // Y la línea no está: el rechazo es de verdad, no un aviso.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.FacturaLinea.AsNoTracking().CountAsync(l => l.FacturaId == facturaId, ct))
            .Should().Be(0);
    }

    /// <summary>
    /// Una línea corriente con un tipo de retención no vale: la retención es un tipo de línea, no un
    /// adorno que se pone a cualquier línea.
    /// </summary>
    [Fact]
    public async Task UnaLineaCorrienteNoAceptaTipoDeRetencion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        var agregada = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas",
            new
            {
                tipo = nameof(TipoLineaFactura.Renta),
                concepto = "Renta con retención",
                cantidad = 1m,
                precioUnitario = 100m,
                tipoRetencionId = tipoRetencion,
            },
            ct);

        agregada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await agregada.Content.ReadAsStringAsync(ct)).Should().Contain("Retencion");
    }

    /// <summary>
    /// Una factura corriente, sin una sola línea de retención, no paga nada por esta tarea: no deja fila
    /// en <c>comprobante_retencion</c>, no lleva el indicador y sus totales nuevos son los mismos de
    /// siempre.
    /// </summary>
    [Fact]
    public async Task UnaFacturaSinRetencionNoCreaComprobanteNiLlevaElIndicador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieId, Moneda.USD, ct);

        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        emitida.GetProperty("retencion").GetDecimal().Should().Be(0m);
        emitida.GetProperty("netoACobrar").GetDecimal().Should().Be(100m, "sin retención, el neto es el total");
        emitida.GetProperty("retencionSinComprobante")
            .GetBoolean().Should().BeFalse("no hay retención, así que no hay comprobante que falte");

        (await AyudaRetenciones.ComprobantesAsync(bd, facturaId, ct))
            .Should().Be(0, "una factura sin retención no deja fila de comprobante");

        // Y tampoco aparece en la lista, que es la otra mitad del indicador. Se filtra por el cliente
        // de la prueba porque la base es compartida y la orden por defecto es ascendente: la factura
        // recién creada quedaría en la última página.
        var listado = await sesion.GetAsync($"/api/v1/facturas?clienteId={clienteId:D}", ct);
        var fila = (await listado.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("data").EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == facturaId);

        fila.GetProperty("retencion").GetDecimal().Should().Be(0m);
        fila.GetProperty("netoACobrar").GetDecimal().Should().Be(100m);
        fila.GetProperty("retencionSinComprobante").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Una factura en borrador que ya retiene tampoco lleva el indicador: el comprobante se crea al
    /// emitir, así que antes de eso no hay nada pendiente.
    /// </summary>
    [Fact]
    public async Task UnBorradorQueRetieneTodaviaNoLlevaElIndicador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        var factura = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);

        factura.GetProperty("retencion").GetDecimal().Should().Be(100m);
        factura.GetProperty("retencionSinComprobante").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Monta una factura con retención e ITBIS y devuelve lo retenido, para el criterio de las tres
    /// bases. Todos los datos son de la prueba; el porcentaje sale de la configuración.
    /// </summary>
    private async Task<decimal> ImporteRetenidoAsync(
        SesionCliente sesion,
        BaseRetencion baseCalculo,
        decimal porcentaje,
        Guid tipoItbisId,
        CancellationToken ct)
    {
        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, baseCalculo, porcentaje, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(
            sesion, tipoRetencion, ct, precioUnitario: 1_000m, tipoItbisId: tipoItbisId);

        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("itbis").GetDecimal()
            .Should().Be(180m, "la misma línea con ITBIS en las tres facturas, para que solo cambie la base");

        return cuerpo.GetProperty("retencion").GetDecimal();
    }
}