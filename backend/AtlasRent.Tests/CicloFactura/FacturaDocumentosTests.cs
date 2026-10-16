using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Retenciones;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// F3-08: duplicar una factura y convertir una proforma en factura (docs/ESPECIFICACION.md §3.8, D7).
/// <para>
/// Duplicar copia el documento y no su historia: la copia nace en borrador, sin número, sin
/// comprobante y sin cobros, aunque la original se haya emitido y cobrado.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaDocumentosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// La copia lleva las mismas líneas con su ITBIS y su retención, y nada de lo que ya pasó: ni
    /// número, ni comprobante, ni cobros, ni referencia de e-factura, ni el comprobante de retención.
    /// </summary>
    [Fact]
    public async Task LaCopiaLlevaLasLineasYNingunaDeLasCosasQueYaPasaron()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        const decimal porcentajeItbis = 18m;
        const decimal porcentajeRetencion = 3m;

        var tipoItbis = await AyudaFacturas.CrearTipoItbisAsync(sesion, porcentajeItbis, ct);
        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, porcentajeRetencion, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(
            sesion, tipoRetencion, ct, cantidad: 2m, precioUnitario: 1_000m, tipoItbisId: tipoItbis);

        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);
        var original = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);

        // Lo que ya pasó se le pone a la original para comprobar que la copia no lo arrastra.
        var referencia = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/referencia-efactura",
            new { referencia = $"NCF-{Guid.NewGuid():N}"[..16] },
            ct);
        referencia.StatusCode.Should().Be(HttpStatusCode.OK, await referencia.Cuerpo());

        var duplicada = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/duplicar", new { }, ct);
        duplicada.StatusCode.Should().Be(HttpStatusCode.OK, await duplicada.Cuerpo());

        var cuerpo = await duplicada.Content.ReadFromJsonAsync<JsonElement>(ct);
        var copiaId = cuerpo.GetProperty("factura").GetProperty("id").GetGuid();

        var copia = await AyudaCiclo.FilaAsync(bd, copiaId, ct);
        copia.Estado.Should().Be(EstadoFactura.Borrador, "una copia nunca nace emitida");
        copia.Numero.Should().BeNull("y por tanto no consume número");
        copia.ReferenciaEfactura.Should().BeNull();
        copia.Total.Should().Be(original.GetProperty("total").GetDecimal());
        copia.Retencion.Should().Be(original.GetProperty("retencion").GetDecimal());

        var copiaJson = await AyudaCobros.FacturaAsync(sesion, copiaId, ct);
        var lineasOriginal = original.GetProperty("lineas").EnumerateArray().ToList();
        var lineasCopia = copiaJson.GetProperty("lineas").EnumerateArray().ToList();

        lineasCopia.Should().HaveCount(lineasOriginal.Count);
        lineasCopia.Select(l => l.GetProperty("tipo").GetString())
            .Should().BeEquivalentTo(lineasOriginal.Select(l => l.GetProperty("tipo").GetString()));
        lineasCopia.Select(l => l.GetProperty("importe").GetDecimal())
            .Should().BeEquivalentTo(lineasOriginal.Select(l => l.GetProperty("importe").GetDecimal()));

        lineasCopia.Select(l => GuidDe(l, "tipoItbisId"))
            .Should().BeEquivalentTo(lineasOriginal.Select(l => GuidDe(l, "tipoItbisId")));
        lineasCopia.Select(l => GuidDe(l, "tipoRetencionId"))
            .Should().BeEquivalentTo(lineasOriginal.Select(l => GuidDe(l, "tipoRetencionId")));

        lineasCopia.Should().Contain(l => GuidDe(l, "tipoItbisId") == tipoItbis, "el ITBIS viene con su tipo configurado");
        lineasCopia.Should().Contain(l => GuidDe(l, "tipoRetencionId") == tipoRetencion, "y la retención con el suyo");

        (await AyudaComprobante.ComprobanteAsync(bd, copiaId, ct)).Should().BeNull();
        (await AyudaRetenciones.ComprobanteAsync(bd, copiaId, ct)).Should().BeNull();

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CobroAsignacion.AsNoTracking().CountAsync(a => a.FacturaId == copiaId, ct))
            .Should().Be(0, "los cobros de la original no se copian");

        emitida.GetProperty("retencionSinComprobante").GetBoolean()
            .Should().BeTrue("el comprobante de retención se creó al emitir la original, y solo ahí");
    }

    /// <summary>
    /// Si ya hay otra factura del mismo contrato, periodo y concepto, la duplicación se hace igual y
    /// avisa. El aviso informa; no bloquea, porque la otra puede ser una factura anterior correcta.
    /// </summary>
    [Fact]
    public async Task DuplicarAvisaDeLoQueYaExisteYNoLoImpide()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var concepto = $"Renta {Guid.NewGuid():N}"[..24];

        // Dos facturas del mismo contrato y concepto: la segunda es la «otra» que va a avisar.
        var primeraId = await CrearConConceptoAsync(sesion, clienteId, contratoId, serieId, hoy, concepto, ct);
        var segundaId = await CrearConConceptoAsync(sesion, clienteId, contratoId, serieId, hoy, concepto, ct);

        await sesion.PostAsync($"/api/v1/facturas/{primeraId:D}/emitir", new { }, ct);
        await sesion.PostAsync($"/api/v1/facturas/{segundaId:D}/emitir", new { }, ct);

        var duplicada = await sesion.PostAsync($"/api/v1/facturas/{segundaId:D}/duplicar", new { }, ct);
        duplicada.StatusCode.Should().Be(HttpStatusCode.OK, "el aviso no impide la duplicación");

        var cuerpo = await duplicada.Content.ReadFromJsonAsync<JsonElement>(ct);
        var similares = cuerpo.GetProperty("similares").EnumerateArray().ToList();

        similares.Should().Contain(s => s.GetProperty("facturaId").GetGuid() == primeraId);
        similares.Should().NotContain(s => s.GetProperty("facturaId").GetGuid() == cuerpo.GetProperty("factura").GetProperty("id").GetGuid(),
            "la copia no se avisa a sí misma");
    }

    /// <summary>
    /// Convertir una proforma saca la factura con la misma moneda y los mismos importes, y deja la
    /// proforma en <c>Convertida</c>. Ninguna de las dos puede contar en el mismo sitio: la serie de la
    /// proforma no suma a facturado ni a saldos (§3.7), que es lo que evita que el mismo importe
    /// aparezca dos veces en la vista de cuentas.
    /// </summary>
    [Fact]
    public async Task ConvertirProformaMantieneMonedaEImportesYLaDejaDeContar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieProforma = await AyudaFacturas.CrearSerieAsync(
            sesion, TipoSerie.Proforma, null, ct, sumaAFacturado: false, sumaASaldos: false);
        var serieFiscal = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        await AyudaComprobante.NumeradorAsync(sesion, serieFiscal, hoy, ct);

        var proformaId = await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieProforma, Moneda.USD, ct);
        var emitida = await sesion.PostAsync($"/api/v1/facturas/{proformaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        var proforma = await emitida.Content.ReadFromJsonAsync<JsonElement>(ct);
        var total = proforma.GetProperty("total").GetDecimal();

        var convertida = await sesion.PostAsync(
            $"/api/v1/facturas/{proformaId:D}/convertir-proforma",
            new { serieId = serieFiscal },
            ct);

        convertida.StatusCode.Should().Be(HttpStatusCode.OK, await convertida.Cuerpo());

        var cuerpo = await convertida.Content.ReadFromJsonAsync<JsonElement>(ct);
        var factura = cuerpo.GetProperty("factura");

        factura.GetProperty("estado").GetString().Should().Be(nameof(EstadoFactura.Emitida));
        factura.GetProperty("serieId").GetGuid().Should().Be(serieFiscal, "la serie fiscal es la que emite");
        factura.GetProperty("numero").GetInt32().Should().BePositive();
        factura.GetProperty("moneda").GetString().Should().Be(nameof(Moneda.USD), "D7: la moneda no se toca");
        factura.GetProperty("total").GetDecimal().Should().Be(total, "D7: los importes no se tocan");

        var filaProforma = await AyudaCiclo.FilaAsync(bd, proformaId, ct);
        filaProforma.Estado.Should().Be(EstadoFactura.Convertida);
        filaProforma.Numero.Should().Be(proforma.GetProperty("numero").GetInt32(), "su número no se borra");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var serie = await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieProforma, ct);
        serie.SumaAFacturado.Should().BeFalse("si sumara, el importe contaría dos veces");
        serie.SumaASaldos.Should().BeFalse();

        // Y el importe que cuenta en la vista de cuentas es el de la factura, una sola vez.
        var cuentaDeFacturado = await contexto.Factura.AsNoTracking()
            .Where(f => f.ClienteId == clienteId
                && f.Estado == EstadoFactura.Emitida
                && f.SerieId != serieProforma)
            .SumAsync(f => (decimal?)f.Total, ct);

        cuentaDeFacturado.Should().Be(total, "solo cuenta la factura nueva, y una sola vez");

        (await contexto.ComprobanteFiscal.AsNoTracking().CountAsync(c => c.FacturaId == proformaId, ct))
            .Should().Be(0, "una proforma no lleva comprobante fiscal");

        // La factura nueva sí lo lleva: es una factura.
        (await contexto.ComprobanteFiscal.AsNoTracking().CountAsync(
                c => c.FacturaId == cuerpo.GetProperty("factura").GetProperty("id").GetGuid(), ct))
            .Should().Be(1);
    }

    /// <summary>
    /// La serie destino se valida: no puede ser la de la proforma ni una serie inactiva, porque
    /// convertir en una serie de proformas dejaría la factura en el sitio del que ha salido.
    /// </summary>
    [Fact]
    public async Task LaSerieDestinoSeValida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieProforma = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Proforma, null, ct);
        var serieInactiva = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Fiscal, null, ct);

        var proformaId = await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieProforma, Moneda.USD, ct);
        await sesion.PostAsync($"/api/v1/facturas/{proformaId:D}/emitir", new { }, ct);

        await DesactivarSerieAsync(sesion, serieInactiva, ct);

        var mismaSerie = await sesion.PostAsync(
            $"/api/v1/facturas/{proformaId:D}/convertir-proforma", new { serieId = serieProforma }, ct);
        mismaSerie.StatusCode.Should().Be(HttpStatusCode.Conflict, await mismaSerie.Cuerpo());

        var inactiva = await sesion.PostAsync(
            $"/api/v1/facturas/{proformaId:D}/convertir-proforma", new { serieId = serieInactiva }, ct);
        inactiva.StatusCode.Should().Be(HttpStatusCode.Conflict, await inactiva.Cuerpo());

        (await AyudaCiclo.FilaAsync(bd, proformaId, ct)).Estado
            .Should().Be(EstadoFactura.Emitida, "y la proforma sigue como estaba");
    }

    /// <summary>
    /// Rehacer en otra moneda anula la original y deja un borrador en la moneda pedida con el importe
    /// tecleado. La original no cambia de moneda: se anula con su número.
    /// </summary>
    [Fact]
    public async Task RehacerEnMonedaAnulaLaOriginalYCreaElBorradorNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieUsd = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var serieDop = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, null, serieUsd, Moneda.USD, hoy.AddDays(30), 250m, ct);

        var original = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);

        var rehecha = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/rehacer-moneda",
            new
            {
                moneda = nameof(Moneda.DOP),
                serieId = serieDop,
                total = 14_750m,
                motivo = "Se emitió en la moneda equivocada",
            },
            ct);

        rehecha.StatusCode.Should().Be(HttpStatusCode.OK, await rehecha.Cuerpo());

        var cuerpo = await rehecha.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("facturaAnulada").GetGuid().Should().Be(facturaId);
        cuerpo.GetProperty("numeroAnulado").GetInt32()
            .Should().Be(original.GetProperty("numero").GetInt32(), "el número no se libera (D28)");

        var anulada = await AyudaCiclo.FilaAsync(bd, facturaId, ct);
        anulada.Estado.Should().Be(EstadoFactura.Anulada);
        anulada.Moneda.Should().Be(Moneda.USD, "la original no cambia de moneda");
        anulada.MotivoAnulacion.Should().Be("Se emitió en la moneda equivocada");

        var nuevaId = cuerpo.GetProperty("nueva").GetProperty("id").GetGuid();
        var nueva = await AyudaCiclo.FilaAsync(bd, nuevaId, ct);
        nueva.Estado.Should().Be(EstadoFactura.Borrador, "la nueva nace en borrador, para revisarla antes de emitir");
        nueva.Moneda.Should().Be(Moneda.DOP);
        nueva.SerieId.Should().Be(serieDop);
        nueva.Total.Should().Be(14_750m, "el importe es el tecleado, no un cambio calculado");
        nueva.Numero.Should().BeNull();

        // El motivo es obligatorio aquí igual que en la anulación: sin él no se explica por qué se
        // quema un número.
        var sinMotivo = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/rehacer-moneda",
            new { moneda = nameof(Moneda.DOP), serieId = serieDop, total = 100m },
            ct);
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest, await sinMotivo.Cuerpo());
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>
    /// Identificador de un campo que puede venir nulo —una línea sin ITBIS lo trae así— sin que eso
    /// sea una excepción: la pregunta es si viene o no viene.
    /// </summary>
    private static Guid? GuidDe(JsonElement linea, string propiedad)
    {
        var valor = linea.GetProperty(propiedad);
        return valor.ValueKind == JsonValueKind.Null ? null : valor.GetGuid();
    }

    /// <summary>Factura en borrador con el concepto que le pida la prueba, que es parte de la clave.</summary>
    private static async Task<Guid> CrearConConceptoAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid contratoId,
        Guid serieId,
        DateOnly hoy,
        string concepto,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                contratoId,
                serieId,
                moneda = nameof(Moneda.USD),
                fechaVencimiento = hoy.AddDays(30).ToString("yyyy-MM-dd"),
                concepto,
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Cuerpo());
        var facturaId = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        await AyudaFacturas.AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);

        return facturaId;
    }

    /// <summary>
    /// Da de baja la serie por su propio endpoint, que la marca como inactiva sin borrarla. Se usa
    /// porque una serie con facturas emitidas no se puede editar (F3-02) y esta no tiene ninguna.
    /// </summary>
    private static async Task DesactivarSerieAsync(SesionCliente sesion, Guid serieId, CancellationToken ct)
    {
        var dadaDeBaja = await sesion.DeleteAsync($"/api/v1/config/series/{serieId:D}", ct);
        dadaDeBaja.StatusCode.Should().Be(HttpStatusCode.NoContent, await dadaDeBaja.Cuerpo());
    }
}