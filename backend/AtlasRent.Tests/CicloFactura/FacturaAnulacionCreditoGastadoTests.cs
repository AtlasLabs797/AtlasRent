using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// BUG-019 y BUG-032 aplicados a la anulación de factura: anular la única factura de un cobro lo anula
/// entero, y eso saca su excedente del saldo a favor. Si ese crédito ya se gastó, la anulación se
/// rechaza (409) y no se esconde con un clamp a cero; y la anulación toma el mismo cerrojo por contrato
/// que los cobros.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaAnulacionCreditoGastadoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task AnularLaFacturaDeUnCobroCuyoCreditoYaSeGastoDa409YNoCambiaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var f1 = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        // A: 500 contra 300 de deuda, deja 200 de crédito y es el único cobro de F1.
        var a = await RegistrarAsync(sesion, clienteId, contratoId, hoy, 500m, 0m, formaPagoId, cuentaId, ct);

        // B paga F2 (151) con 1 en efectivo y 150 del crédito de A.
        await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 151m, ct);
        var b = await RegistrarAsync(sesion, clienteId, contratoId, hoy, 1m, 150m, formaPagoId, cuentaId, ct);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(50m);

        var respuesta = await sesion.PostAsync(
            $"/api/v1/facturas/{f1:D}/anular", new { motivo = "Error de emisión" }, ct);

        var cuerpo = await respuesta.Cuerpo();
        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, cuerpo);
        cuerpo.Should().Contain(b.ToString("D"), "dice qué cobro hay que anular antes");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == f1, ct)).Estado
            .Should().Be(EstadoFactura.Emitida);
        var cobroA = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == a, ct);
        cobroA.Estado.Should().Be(EstadoCobro.Registrado);
        cobroA.ImporteDevuelto.Should().Be(0m);
        (await contexto.CobroAsignacion.AsNoTracking().CountAsync(x => x.CobroId == a && x.Vigente, ct))
            .Should().Be(1);
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(50m);
    }

    /// <summary>
    /// A deja 200 de crédito sobre F1. A la vez: anular F1 (anula A y saca 200 del saldo) y un cobro C que
    /// gasta esos 200 en F2. Sin cerrojo ganan los dos y el saldo queda en -200 (o escondido con un clamp);
    /// con él exactamente uno gana y las cuentas cuadran.
    /// </summary>
    [Fact]
    public async Task AnularFacturaYGastarSuCreditoALaVezNoDejanElSaldoDescuadrado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var f1 = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        await RegistrarAsync(sesion, clienteId, contratoId, hoy, 500m, 0m, formaPagoId, cuentaId, ct);
        await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 201m, ct);

        var barrera = new Barrier(2);
        var anular = Task.Run(async () =>
        {
            barrera.SignalAndWait(ct);
            return await sesion.PostAsync(
                $"/api/v1/facturas/{f1:D}/anular", new { motivo = "Concurrente" }, Guid.NewGuid().ToString("N"), ct);
        }, ct);
        var cobrar = Task.Run(async () =>
        {
            barrera.SignalAndWait(ct);
            return await sesion.PostAsync(
                "/api/v1/cobros",
                AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 1m, formaPagoId, cuentaId, usarSaldoAFavor: 200m),
                Guid.NewGuid().ToString("N"),
                ct);
        }, ct);

        var respuestas = await Task.WhenAll(anular, cobrar);
        var cuerpos = string.Join(" | ", await Task.WhenAll(respuestas.Select(r => r.Cuerpo())));

        respuestas.Count(r => r.IsSuccessStatusCode).Should().Be(
            1, "el crédito de 200 solo puede irse una vez: " + cuerpos);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var saldo = await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct) ?? 0m;
        saldo.Should().BeGreaterThanOrEqualTo(0m);

        var cobros = await contexto.Cobro.AsNoTracking().Where(c => c.ContratoId == contratoId).ToListAsync(ct);
        var asignaciones = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.Cobro!.ContratoId == contratoId).ToListAsync(ct);

        foreach (var cobro in cobros.Where(c => c.Estado == EstadoCobro.Registrado))
        {
            var vigente = asignaciones.Where(a => a.CobroId == cobro.Id && a.Vigente).Sum(a => a.ImporteRecibido ?? a.Importe);
            (vigente + cobro.Excedente + cobro.ImporteDevuelto).Should().Be(
                cobro.ImporteRecibido + cobro.SaldoFavorUsado, $"cobro {cobro.Id}");
        }

        saldo.Should().Be(
            cobros.Where(c => c.Estado == EstadoCobro.Registrado).Sum(c => c.Excedente - c.SaldoFavorUsado));
    }

    private static async Task<Guid> RegistrarAsync(
        SesionCliente sesion, Guid clienteId, Guid contratoId, DateOnly hoy, decimal importe, decimal credito,
        Guid formaPagoId, Guid cuentaId, CancellationToken ct)
    {
        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, importe, formaPagoId, cuentaId, usarSaldoAFavor: credito),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }
}
