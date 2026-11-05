using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// BUG-050 (§3.5: "la liquidación cierra inventario de salida, fianza y saldos"): no se liquida un
/// contrato con la fianza cobrada sin resolver ni con facturas suyas pendientes de cobro. La
/// validación no mueve dinero: dice qué falta.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class LiquidarCierraFianzaYSaldosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task NoSeLiquidaConLaFianzaCobradaSinResolver()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        var (clienteId, contratoId) = await FinalizadoAsync(admin, 30, ct);

        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var rechazada = await AyudaContrato.Boton(admin, contratoId, "liquidacion", null, ct);
        rechazada.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazada.Cuerpo());
        (await rechazada.Cuerpo()).Should().Contain("fianza");
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);
        (await AyudaLiquidacion.LiquidacionAsync(bd, contratoId, ct)).Should().BeNull();

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver", AyudaFianzas.PeticionDevolucion(500m), ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        await AyudaContrato.AceptarAsync(admin, contratoId, "liquidacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Liquidado);
    }

    [Fact]
    public async Task NoSeLiquidaConUnaFacturaDelContratoPendienteDeCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        var (clienteId, contratoId) = await FinalizadoAsync(admin, 31, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        await AyudaCobros.EmitidaAsync(admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var rechazada = await AyudaContrato.Boton(admin, contratoId, "liquidacion", null, ct);
        rechazada.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazada.Cuerpo());
        (await rechazada.Cuerpo()).Should().Contain("factura");
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);

        var cobro = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 300m,
                await AyudaCobros.FormaPagoAsync(admin, ct),
                await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct)),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        await AyudaContrato.AceptarAsync(admin, contratoId, "liquidacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Liquidado);
    }

    /// <summary>Contrato activo con inicio en el pasado, dado de baja hoy y finalizado.</summary>
    private async Task<(Guid ClienteId, Guid ContratoId)> FinalizadoAsync(
        SesionCliente admin,
        int diasAtras,
        CancellationToken ct)
    {
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var unidadId = await AyudaReserva.UnidadAsync(admin, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            admin,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(-diasAtras), moneda: "USD"),
            ct);
        await AyudaContrato.AceptarAsync(admin, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(admin, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(admin, contratoId, "activacion", null, ct);
        await AyudaLiquidacion.DarBajaYFinalizarAsync(admin, contratoId, AyudaContrato.Dia(), ct);

        return (clienteId, contratoId);
    }
}
