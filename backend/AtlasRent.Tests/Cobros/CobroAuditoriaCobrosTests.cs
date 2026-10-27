using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// AUD-04: BUG-019 (dos cobros simultáneos sobre la misma factura), BUG-031 (selección de facturas en
/// otra moneda), BUG-032 (anular un cobro cuyo crédito ya se gastó) y BUG-035 (ajuste por diferencia
/// de cambio duplicado o sin cobro en otra moneda).
/// <para>
/// Todo va por la API real y las comprobaciones leen las tablas, no la respuesta.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroAuditoriaCobrosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const int Rondas = 8;

    /// <summary>
    /// BUG-019: factura USD 1.000 y dos cobros de 1.000 a la vez, con claves de idempotencia distintas.
    /// Sin cerrojo los dos leen «1.000 pendientes» y asignan 1.000 cada uno. Con él, se turnan: uno
    /// asigna 1.000 y el otro, que ya no ve deuda, deja su dinero como excedente (saldo a favor,
    /// §3.10.1). Se repite varias rondas porque la carrera no es determinista.
    /// </summary>
    [Fact]
    public async Task DosCobrosSimultaneosNuncaAsignanMasQueElNetoDeLaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        for (var ronda = 1; ronda <= Rondas; ronda++)
        {
            var facturaId = await AyudaCobros.EmitidaAsync(
                sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

            var barrera = new Barrier(2);
            var peticiones = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                barrera.SignalAndWait(ct);
                return await sesion.PostAsync(
                    "/api/v1/cobros",
                    AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 1_000m, formaPagoId, cuentaId),
                    Guid.NewGuid().ToString("N"),
                    ct);
            }, ct)).ToArray();

            var respuestas = await Task.WhenAll(peticiones);

            foreach (var respuesta in respuestas)
            {
                respuesta.StatusCode.Should().Be(HttpStatusCode.OK, $"ronda {ronda}: " + await respuesta.Cuerpo());
            }

            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            var asignado = await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == facturaId && a.Vigente)
                .SumAsync(a => a.Importe, ct);

            asignado.Should().Be(1_000m, $"ronda {ronda}: la factura no puede cobrarse por encima de su neto");

            var cuerpos = new List<JsonElement>();
            foreach (var respuesta in respuestas)
            {
                cuerpos.Add(await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct));
            }

            cuerpos.Sum(c => c.GetProperty("totalAsignado").GetDecimal()).Should().Be(1_000m);
            cuerpos.Sum(c => c.GetProperty("excedente").GetDecimal())
                .Should().Be(1_000m, $"ronda {ronda}: el segundo cobro no tenía deuda y su dinero es saldo a favor");
        }
    }

    /// <summary>
    /// BUG-031: la selección de filas de la vista de cuentas con una factura en dólares y un cobro en
    /// pesos mezclaba las dos monedas en el recorte y daba un excedente negativo. 30.000 DOP a 60
    /// son 500 USD: se asignan 500 y se consumen los 30.000.
    /// </summary>
    [Fact]
    public async Task UnaFacturaSeleccionadaEnDolaresSeCobraParcialmenteConPesos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 30_000m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: 60m,
                facturasSeleccionadas: new[] { facturaId }),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var asignacion = await contexto.CobroAsignacion.AsNoTracking().SingleAsync(a => a.FacturaId == facturaId, ct);
        asignacion.Importe.Should().Be(500m, "30.000 pesos a 60 son 500 dólares de la factura");
        asignacion.ImporteRecibido.Should().Be(30_000m);

        var cobro = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == asignacion.CobroId, ct);
        cobro.Excedente.Should().Be(0m, "no sobra nada: todo el cobro se gasta en la factura");
    }

    /// <summary>
    /// BUG-031, el otro lado: si el cobro en pesos cubre de más, la factura se salda por su pendiente
    /// entero y lo que sobra, en pesos, es excedente.
    /// </summary>
    [Fact]
    public async Task UnaFacturaSeleccionadaEnDolaresCobradaDeMasConPesosDejaExcedenteEnPesos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 70_000m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: 60m,
                facturasSeleccionadas: new[] { facturaId }),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var asignacion = await contexto.CobroAsignacion.AsNoTracking().SingleAsync(a => a.FacturaId == facturaId, ct);
        asignacion.Importe.Should().Be(1_000m);
        asignacion.ImporteRecibido.Should().Be(60_000m);

        var cobro = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == asignacion.CobroId, ct);
        cobro.Excedente.Should().Be(10_000m, "los 10.000 pesos que sobran son saldo a favor en pesos");
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.DOP, ct)).Should().Be(10_000m);
    }

    /// <summary>
    /// BUG-032: el cobro A deja 300 de crédito, el cobro B se gasta 300 de ese crédito. Anular A
    /// dejaría el saldo en negativo y el clamp a 0 lo escondía. Ahora es un 409 que dice qué cobro hay
    /// que anular antes; anulado B, A se anula sin problema.
    /// </summary>
    [Fact]
    public async Task NoSeAnulaUnCobroCuyoCreditoYaFinancioOtroCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var cobroA = await RegistrarAsync(sesion, AyudaCobros.Peticion(
            clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId), ct);
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(300m);

        await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 250m, ct);
        var cobroB = await RegistrarAsync(sesion, AyudaCobros.Peticion(
            clienteId, contratoId, hoy, Moneda.USD, 50m, formaPagoId, cuentaId, usarSaldoAFavor: 300m), ct);
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(100m);

        var rechazo = await sesion.PostAsync($"/api/v1/cobros/{cobroA:D}/anular", new { motivo = "Error de captura" }, ct);

        rechazo.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazo.Cuerpo());
        (await rechazo.Content.ReadAsStringAsync(ct)).Should().Contain(
            cobroB.ToString("D"), "el mensaje dice qué cobro hay que anular antes");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            (await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == cobroA, ct)).Estado
                .Should().Be(EstadoCobro.Registrado, "un 409 no anula nada");
        }

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(100m, "ni toca el saldo");

        var anuladoB = await sesion.PostAsync($"/api/v1/cobros/{cobroB:D}/anular", new { motivo = "Se anula primero B" }, ct);
        anuladoB.StatusCode.Should().Be(HttpStatusCode.OK, await anuladoB.Cuerpo());
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(300m);

        var anuladoA = await sesion.PostAsync($"/api/v1/cobros/{cobroA:D}/anular", new { motivo = "Ahora sí" }, ct);
        anuladoA.StatusCode.Should().Be(HttpStatusCode.OK, await anuladoA.Cuerpo());
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(0m);
    }

    /// <summary>
    /// BUG-035 (a): dos ajustes por diferencia de cambio simultáneos sobre la misma factura. Solo uno
    /// puede entrar: el otro, tras el cerrojo, ya no ve pendiente.
    /// </summary>
    [Fact]
    public async Task DosAjustesSimultaneosNoSeDuplican()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, 1.00m, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        for (var ronda = 1; ronda <= Rondas; ronda++)
        {
            var facturaId = await AyudaCobros.EmitidaAsync(
                sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

            // 59.455 DOP a 59,50 dejan 0,76 USD pendientes.
            var cobro = await sesion.PostAsync(
                "/api/v1/cobros",
                AyudaCobros.Peticion(
                    clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                    tipoCambioAplicado: AyudaCobros.CambioExacto,
                    facturasSeleccionadas: new[] { facturaId }),
                ct);
            cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

            var barrera = new Barrier(2);
            var peticiones = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                barrera.SignalAndWait(ct);
                return await sesion.PostAsync(
                    "/api/v1/cobros/ajustes-diferencia-cambio",
                    new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Diferencia de cambio" },
                    Guid.NewGuid().ToString("N"),
                    ct);
            }, ct)).ToArray();

            var respuestas = await Task.WhenAll(peticiones);

            respuestas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, $"ronda {ronda}");
            respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1, $"ronda {ronda}");
            (await AyudaCobros.AjustesAsync(bd, facturaId, ct)).Where(a => a.AnuladoAt is null)
                .Should().ContainSingle($"ronda {ronda}: el ajuste no se duplica");
        }
    }

    /// <summary>
    /// BUG-035 (b): el ajuste solo existe para el céntimo que deja un pago en otra moneda. Una factura
    /// sin cobros, o con cobros solo en su moneda, no se cierra por ajuste aunque su pendiente quepa en
    /// la tolerancia.
    /// </summary>
    [Fact]
    public async Task ElAjusteExigeUnCobroVigenteEnOtraMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, 1.00m, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaDolares = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Sin ningún cobro: pendiente 0,50 dentro de la tolerancia.
        var sinCobros = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 0.50m, ct);
        var rechazoSinCobros = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId = sinCobros, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Cerrar sin pagar" },
            ct);
        rechazoSinCobros.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazoSinCobros.Cuerpo());
        (await AyudaCobros.AjustesAsync(bd, sinCobros, ct)).Should().BeEmpty();

        // Con un cobro en su misma moneda (USD): tampoco hay diferencia de cambio que ajustar.
        var enMismaMoneda = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(20), 100m, ct);
        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 99.50m, formaPagoId, cuentaDolares,
                facturasSeleccionadas: new[] { enMismaMoneda }),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var rechazoMismaMoneda = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId = enMismaMoneda, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Cerrar sin cambio" },
            ct);
        rechazoMismaMoneda.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazoMismaMoneda.Cuerpo());
        (await AyudaCobros.AjustesAsync(bd, enMismaMoneda, ct)).Should().BeEmpty();
    }

    private static async Task<Guid> RegistrarAsync(SesionCliente sesion, object peticion, CancellationToken ct)
    {
        var registrado = await sesion.PostAsync("/api/v1/cobros", peticion, ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }
}
