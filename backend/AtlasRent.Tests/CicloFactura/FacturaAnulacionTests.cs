using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// F3-08: anular una factura emitida conserva todo lo que salió y deshace su efecto económico
/// (docs/ESPECIFICACION.md §3.8, D6, D28).
/// <para>
/// El criterio de aceptación del plan es «cobro de 2 facturas, anular 1: la otra sigue pagada», y es
/// la primera prueba de aquí. Se lee de las tablas y no de los DTO porque lo que hay que demostrar es
/// que las filas siguen ahí y han dejado de contar, que es justo lo que un JSON no puede enseñar.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaAnulacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// El criterio del plan: un cobro paga dos facturas, se anula una y la otra sigue pagada. El
    /// cobro no se toca —sigue registrado, porque el dinero llegó— y lo único que deja de contar es
    /// la asignación de la factura anulada.
    /// </summary>
    [Fact]
    public async Task CobroDeDosFacturasAlAnularUnaLaOtraSiguePagada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var anuladaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var quietaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 200m, ct);

        var cobroId = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 500m, formaPagoId, cuentaId, ct,
            (anuladaId, 300m), (quietaId, 200m));

        await AyudaCiclo.AnularAsync(sesion, anuladaId, "Se emitió por error", ct);

        // El cobro sigue vigente: es dinero que llegó y que sigue cubriendo una deuda.
        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Registrado, "anular la factura no es anular el cobro");
        cobro.AnuladoAt.Should().BeNull();

        var asignaciones = await AyudaCiclo.AsignacionesDeCobroAsync(bd, cobroId, ct);
        asignaciones.Should().HaveCount(2, "las filas no se borran (AGENTS.md §6)");
        asignaciones.Single(a => a.FacturaId == quietaId).Vigente.Should().BeTrue();
        asignaciones.Single(a => a.FacturaId == quietaId).Importe.Should().Be(200m);

        var muerta = asignaciones.Single(a => a.FacturaId == anuladaId);
        muerta.Vigente.Should().BeFalse("la asignación de la factura anulada deja de contar");
        muerta.AnuladaAt.Should().NotBeNull();
        muerta.AnuladaPor.Should().NotBeNull();
        muerta.MotivoAnulacion.Should().Be("Se emitió por error");

        // Y la que sigue viva sigue pagada de verdad, no por un dato guardado sino por la suma de lo
        // que sigue contando.
        var quieta = await AyudaCobros.FacturaAsync(sesion, quietaId, ct);
        quieta.GetProperty("estadoCobro").GetString().Should().Be(nameof(EstadoCobroFactura.Pagada));
        quieta.GetProperty("cobrado").GetDecimal().Should().Be(200m);
        quieta.GetProperty("pendiente").GetDecimal().Should().Be(0m);
    }

    /// <summary>
    /// Un cobro que solo pagaba esa factura se anula con ella, porque se queda sin nada que pagar. Sus
    /// asignaciones quedan en la tabla y la factura anulada deja de ser deuda.
    /// </summary>
    [Fact]
    public async Task UnCobroQueSoloPagaraEsaFacturaSeAnulaConSuMotivo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        // Sobra dinero a propósito: ese excedente es crédito del contrato y tiene que volver cuando el
        // cobro se anula (§3.10.1).
        var cobroId = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 400m, formaPagoId, cuentaId, ct, (facturaId, 300m));

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(100m, "los 100 de más quedan de crédito del contrato");

        await AyudaCiclo.AnularAsync(sesion, facturaId, "Error de captura", ct);

        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Anulado, "sin asignaciones vivas, el cobro no cubre nada");
        cobro.MotivoAnulacion.Should().Contain("Error de captura", "el motivo sale del de la factura");
        cobro.AnuladoPor.Should().NotBeNull();

        (await AyudaCiclo.AsignacionesDeCobroAsync(bd, cobroId, ct))
            .Should().ContainSingle("la fila se queda; lo que se pierde es su vigor");

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(0m, "el excedente sale del saldo a favor del contrato");

        // Una factura anulada no es una deuda pendiente: si lo fuera, el aging de F3-10 sumaría dinero
        // que el cliente no debe por un documento que ya no vale.
        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("estado").GetString().Should().Be(nameof(EstadoFactura.Anulada));
        leida.GetProperty("estadoCobro").GetString().Should().Be(nameof(EstadoCobroFactura.Anulada));
        leida.GetProperty("pendiente").GetDecimal().Should().Be(0m);
        leida.GetProperty("cobrado").GetDecimal().Should().Be(0m);
    }

    /// <summary>
    /// D28: anular no libera el número. El siguiente número es el de después, no el anulado, y el
    /// numerador fiscal sigue contando igual.
    /// </summary>
    [Fact]
    public async Task ElNumeroYElComprobanteAnuladosNoSeReutilizan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        await AyudaComprobante.NumeradorAsync(sesion, serieId, hoy, ct);

        var facturaId = await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieId, Moneda.USD, ct);
        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        var numero = (await emitida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("numero").GetInt32();
        var comprobanteAntes = await AyudaComprobante.ComprobanteAsync(bd, facturaId, ct);
        comprobanteAntes.Should().NotBeNull();

        await AyudaCiclo.AnularAsync(sesion, facturaId, "Número duplicado en el mostrador", ct);

        // El número sigue ocupado y marcado como anulado; la serie no retrocede.
        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct)).Should().Contain(numero);

        var numeroFila = await ContextoAsync(c => c.NumeroSerie.AsNoTracking()
            .SingleAsync(n => n.SerieId == serieId && n.Numero == numero, ct));

        numeroFila.Estado.Should().Be(EstadoNumeroSerie.Anulado);
        numeroFila.MotivoAnulacion.Should().Be("Número duplicado en el mostrador");

        var comprobante = await AyudaComprobante.ComprobanteAsync(bd, facturaId, ct);
        comprobante!.Estado.Should().Be(EstadoComprobanteFiscal.Anulado);
        comprobante.Numero.Should().Be(comprobanteAntes!.Numero, "el número de comprobante no se libera");
        comprobante.MotivoAnulacion.Should().Be("Número duplicado en el mostrador");

        // Y la serie sigue por delante del número anulado: el numerador tampoco se queda atrás.
        (await AyudaComprobante.NumeradorDeAsync(bd, serieId, ct))!.SiguienteNumero
            .Should().Be(comprobanteAntes.Numero + 1, "anular no libera el número de comprobante");

        var siguiente = await AyudaCiclo.EmitirEnSerieAsync(
            sesion, clienteId, serieId, hoy.AddDays(30), 500m, ct);

        siguiente.Should().Be(numero + 1, "el contador no retrocede por anular");

        (await AyudaComprobante.NumeradorDeAsync(bd, serieId, ct))!.SiguienteNumero
            .Should().Be(comprobanteAntes.Numero + 2, "el numerador siguió contando, no saltó el anulado");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var repetido = await contexto.Factura.AsNoTracking()
            .CountAsync(f => f.SerieId == serieId && f.Numero == numero, ct);

        repetido.Should().Be(1, "el índice único de (serie, número) sigue protegiendo el número");
    }

    /// <summary>
    /// Sin motivo no hay anulación: es una petición incompleta, no un conflicto (400), y nada cambia.
    /// Anular dos veces, anular lo ya anulado y anular un borrador son conflictos distintos y todos 409.
    /// </summary>
    [Fact]
    public async Task SinMotivoNoSeAnulaYLoQueNoSePuedeAnularEs409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieId, Moneda.USD, ct);
        await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        var sinMotivo = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/anular", new { motivo = "   " }, ct);
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest, await sinMotivo.Cuerpo());

        var ausente = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/anular", new { }, ct);
        ausente.StatusCode.Should().Be(HttpStatusCode.BadRequest, await ausente.Cuerpo());

        (await AyudaCiclo.FilaAsync(bd, facturaId, ct)).Estado
            .Should().Be(EstadoFactura.Emitida, "un 400 no anula nada");

        await AyudaCiclo.AnularAsync(sesion, facturaId, "Primera", ct);

        var otraVez = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/anular", new { motivo = "Segunda" }, ct);
        otraVez.StatusCode.Should().Be(HttpStatusCode.Conflict, await otraVez.Cuerpo());

        // Un borrador no tiene número que conservar: se descarta, no se anula.
        var borradorId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        var borrador = await sesion.PostAsync(
            $"/api/v1/facturas/{borradorId:D}/anular", new { motivo = "No salió" }, ct);
        borrador.StatusCode.Should().Be(HttpStatusCode.Conflict, await borrador.Cuerpo());

        var fila = await AyudaCiclo.FilaAsync(bd, facturaId, ct);
        fila.AnuladoAt.Should().NotBeNull();
        fila.MotivoAnulacion.Should().Be("Primera", "el segundo intento no pisa el motivo del primero");
    }

    /// <summary>
    /// La pantalla de confirmación de §3.8: qué cobros se anularían y cuáles solo se tocarían, con sus
    /// importes, y sin escribir una sola fila.
    /// </summary>
    [Fact]
    public async Task LaSimulacionDiceLosCobrosYNoEscribeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var conCobroPropio = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var compartida = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 200m, ct);
        var tercera = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 150m, ct);

        var propio = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 300m, formaPagoId, cuentaId, ct, (conCobroPropio, 300m));
        var compartido = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 350m, formaPagoId, cuentaId, ct, (compartida, 200m), (tercera, 150m));

        var antes = await AyudaCiclo.RecuentoAsync(bd, ct);

        var simulacion = await sesion.PostAsync(
            $"/api/v1/facturas/{conCobroPropio:D}/simular-anulacion", new { }, ct);
        simulacion.StatusCode.Should().Be(HttpStatusCode.OK, await simulacion.Cuerpo());

        var cuerpo = await simulacion.Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("numeroSeConserva").GetBoolean().Should().BeTrue("D28");
        cuerpo.GetProperty("numero").GetInt32().Should().BePositive();
        cuerpo.GetProperty("netoACobrar").GetDecimal().Should().Be(300m, "lo que estaba pendiente");

        var seAnulan = cuerpo.GetProperty("cobrosQueSeAnulan").EnumerateArray().ToList();
        seAnulan.Should().ContainSingle("este cobro solo paga esa factura");
        seAnulan[0].GetProperty("cobroId").GetGuid().Should().Be(propio);
        seAnulan[0].GetProperty("importeDeEstaFactura").GetDecimal().Should().Be(300m);
        seAnulan[0].GetProperty("importeQueSigueVigente").GetDecimal().Should().Be(0m);
        seAnulan[0].GetProperty("cambioDelSaldoAFavor").GetDecimal()
            .Should().Be(0m, "aquí no quedó excedente: todo el dinero iba a esa factura");
        seAnulan[0].GetProperty("seAnulaEntero").GetBoolean().Should().BeTrue();

        cuerpo.GetProperty("cobrosQueSeToCan").EnumerateArray().Should().BeEmpty();

        // La factura que comparte cobro con otra enseña el otro caso: el cobro no se anula, solo
        // deja de pagar esta.
        var segunda = await sesion.PostAsync(
            $"/api/v1/facturas/{compartida:D}/simular-anulacion", new { }, ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Cuerpo());

        var cuerpo2 = await segunda.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo2.GetProperty("cobrosQueSeAnulan").EnumerateArray().Should().BeEmpty();

        var tocados = cuerpo2.GetProperty("cobrosQueSeToCan").EnumerateArray().ToList();
        tocados.Should().ContainSingle();
        tocados[0].GetProperty("cobroId").GetGuid().Should().Be(compartido);
        tocados[0].GetProperty("importeDeEstaFactura").GetDecimal().Should().Be(200m);
        tocados[0].GetProperty("importeQueSigueVigente").GetDecimal().Should().Be(150m, "la tercera sigue cubierta");
        tocados[0].GetProperty("seAnulaEntero").GetBoolean().Should().BeFalse();

        (await AyudaCiclo.RecuentoAsync(bd, ct))
            .Should().Be(antes, "una simulación no escribe nada");
        (await AyudaCiclo.CobroFilaAsync(bd, propio, ct)).Estado.Should().Be(EstadoCobro.Registrado);
        (await AyudaCiclo.CobroFilaAsync(bd, compartido, ct)).Estado.Should().Be(EstadoCobro.Registrado);
    }

    private async Task<T> ContextoAsync<T>(Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}