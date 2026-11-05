using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
    /// F3-06: la moneda del cobro. El tipo de cambio del día es obligatorio para toda moneda que no sea el
    /// dólar (D9).
    /// <para>
    /// La conversión entre monedas es de F3-07 (§3.10.2, D8) y se prueba en
    /// <see cref="CobroCambioMonedaTests"/>; aquí solo queda el otro sentido, que sigue sin poder
    /// hacerse: pagar una factura en pesos con un cobro en dólares necesita el cambio del peso y el
    /// cobro solo trae el de la moneda en que entra el dinero.
    /// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroMonedaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Sin cambio del día no se registra el cobro: sin cambio congelado el movimiento no se puede
    /// consolidar en dólares, y la salida es la carga manual de §3.12. El 409 la señala.
    /// </summary>
    [Fact]
    public async Task UnCobroEnPesosSinTipoDeCambioDelDiaFalla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Este cobro congela el cambio del día. Al terminar se deshace la anotación para devolver la
        // fila a la base compartida, que si no deja rota la prueba de F3-02 que la borra (BUG-006).
        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);

        await AyudaCobros.QuitarCambioAsync(bd, hoy, Moneda.DOP, ct);

        var cuerpo = AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.DOP, 1_000m, formaPagoId, cuentaId);

        var sinCambio = await sesion.PostAsync("/api/v1/cobros", cuerpo, ct);
        sinCambio.StatusCode.Should().Be(HttpStatusCode.Conflict, await sinCambio.Cuerpo());
        (await sinCambio.Content.ReadAsStringAsync(ct)).Should().Contain("tipo de cambio");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(0, "el 409 no deja el cobro a medias");

        // La salida que señala el 409: la carga manual de §3.12, y entonces sí se registra con el
        // cambio congelado.
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var conCambio = await sesion.PostAsync("/api/v1/cobros", cuerpo, ct);
        conCambio.StatusCode.Should().Be(HttpStatusCode.OK, await conCambio.Cuerpo());

        var cobro = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.ClienteId == clienteId, ct);
        cobro.TipoCambioValor.Should().Be(AyudaCobros.CambioDelDia, "el cambio queda congelado en el cobro, como en la factura");
        cobro.TipoCambioFecha.Should().Be(hoy);
        cobro.TipoCambioId.Should().NotBeNull();
    }

    /// <summary>
    /// El dólar vale 1 por definición y no tiene fila de tipo de cambio (F3-01), así que en dólares el
    /// cobro se registra sin cargar nada.
    /// </summary>
    [Fact]
    public async Task UnCobroEnDolarVaConCambioUnoYSinFila()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 250m, formaPagoId, cuentaId),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var cobro = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.ClienteId == clienteId, ct);
        cobro.TipoCambioValor.Should().Be(1m);
        cobro.TipoCambioFecha.Should().Be(hoy);
        cobro.TipoCambioId.Should().BeNull("el dólar no se guarda en tipo_cambio");
    }

    /// <summary>
    /// Un cobro en dólares no puede saldar deuda en pesos, porque para eso haría falta el cambio del
    /// peso y el cobro solo trae el cambio de la moneda en que entra el dinero. El 409 lo dice con esa
    /// razón, y no con un «no se puede» sin explicación.
    /// <para>
    /// Este caso cambió con F3-07: antes el rechazo era para cualquier moneda distinta, y el mensaje
    /// señalaba §3.10.2 como algo pendiente. Ahora §3.10.2 está hecho y lo que no se puede es
    /// <b>convertir al revés</b>; el otro sentido (pagar una factura en dólares con pesos) sí funciona y
    /// lo comprueba <see cref="CobroCambioMonedaTests"/>.
    /// </para>
    /// <para>
    /// Y la simulación, que valida igual que el registro, deja la deuda en pesos sin tocar y la dice:
    /// el cobro no la cubre y quien lo ve tiene que poder decirlo.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PagarConDolaresUnaFacturaEnPesosFallaYLaSimulacionLoExplica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Emitir una factura en DOP congela el cambio del día; al terminar se deshace (BUG-006).
        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);

        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var seriePesos = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.DOP, ct);
        var facturaPesos = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, seriePesos, Moneda.DOP, hoy.AddDays(10), 2_000m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaDolar = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);

        var cuerpo = AyudaCobros.Peticion(
            clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaDolar,
            asignaciones: new[] { new { facturaId = facturaPesos, importe = 500m } });

        var rechazado = await sesion.PostAsync("/api/v1/cobros", cuerpo, ct);
        rechazado.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazado.Cuerpo());

        var detalle = await rechazado.Content.ReadAsStringAsync(ct);
        detalle.Should().Contain("cambio de DOP", "el conflicto dice qué falta, no solo que hay un conflicto");

        // La simulación valida igual que el registro: no puede proponer algo que luego se rechaza.
        var simulada = await sesion.PostAsync("/api/v1/cobros/simular", cuerpo, ct);
        simulada.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Sin asignaciones explícitas sí sale, no propone esa factura y dice qué deuda del contrato no
        // va a cubrir: saltarse una factura no conversible no es motivo para dejar de cobrar la otra.
        var sinAsignar = AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaDolar);
        var propuesta = await sesion.PostAsync("/api/v1/cobros/simular", sinAsignar, ct);
        propuesta.StatusCode.Should().Be(HttpStatusCode.OK, await propuesta.Cuerpo());

        var cuerpoPropuesta = await propuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoPropuesta.GetProperty("asignaciones").GetArrayLength()
            .Should().Be(0, "un cobro en dólares no salda deuda en pesos");

        var deudaAjena = cuerpoPropuesta.GetProperty("deudaEnOtraMoneda").EnumerateArray().ToList();
        deudaAjena.Should().HaveCount(1);
        deudaAjena[0].GetProperty("moneda").GetString().Should().Be("DOP");
        deudaAjena[0].GetProperty("pendiente").LeerDecimal().Should().Be(2_000m);
        deudaAjena[0].GetProperty("facturas").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// La cuenta receptora tiene una moneda y esa es la del importe recibido (D8). Un cobro que dice
    /// recibir en una moneda que la cuenta no puede contener es un error de captura.
    /// </summary>
    [Fact]
    public async Task LaCuentaReceptoraTieneQueSerDeLaMonedaDelCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var rechazado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaPesos),
            ct);

        rechazado.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazado.Cuerpo());
        (await rechazado.Content.ReadAsStringAsync(ct)).Should().Contain("moneda de la cuenta");
    }
}