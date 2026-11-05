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

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// BUG-048: una reserva con factura vigente no se edita (días, importe, moneda) ni se cancela sin
/// anular antes la factura, y dos peticiones de facturar a la vez no crean dos facturas.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaReservaBloqueoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task ConFacturaVigenteNoSeCambianDiasImporteNiMonedaPeroSiLasObservaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, clienteId, unidadId, fecha) = await ReservaAsync(admin, 440, ct);
        await FacturarAsync(admin, reservaId, serieId, ct);

        (await admin.PutAsync($"/api/v1/reservas/{reservaId:D}", Cuerpo(clienteId, unidadId, fecha, importe: 900m), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "el importe ya está facturado");
        (await admin.PutAsync(
            $"/api/v1/reservas/{reservaId:D}", Cuerpo(clienteId, unidadId, fecha.AddDays(1), importe: 500m), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "los días ya están facturados");
        (await admin.PutAsync(
            $"/api/v1/reservas/{reservaId:D}", Cuerpo(clienteId, unidadId, fecha, importe: 500m, moneda: "DOP"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "la moneda de la factura no cambia");

        var observaciones = await admin.PutAsync(
            $"/api/v1/reservas/{reservaId:D}", Cuerpo(clienteId, unidadId, fecha, importe: 500m, observaciones: "nota"), ct);
        observaciones.StatusCode.Should().Be(HttpStatusCode.OK, await observaciones.Cuerpo());
    }

    [Fact]
    public async Task CancelarExigeQueLaFacturaEsteAnulada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _, _, _) = await ReservaAsync(admin, 441, ct);
        var facturaId = await FacturarAsync(admin, reservaId, serieId, ct);

        (await admin.DeleteAsync($"/api/v1/reservas/{reservaId:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "tiene una factura vigente");

        var anulada = await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/anular", new { motivo = "error" }, ct);
        anulada.StatusCode.Should().Be(HttpStatusCode.OK, await anulada.Cuerpo());

        var cancelada = await admin.DeleteAsync($"/api/v1/reservas/{reservaId:D}", ct);
        cancelada.StatusCode.Should().Be(HttpStatusCode.OK, await cancelada.Cuerpo());
    }

    [Fact]
    public async Task DosPeticionesDeFacturarALaVezCreanUnaSolaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var una = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var otra = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(una, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _, _, _) = await ReservaAsync(una, 442, ct);

        var barrera = new Barrier(2);
        async Task<HttpStatusCode> Enviar(SesionCliente sesion)
        {
            await Task.Yield();
            barrera.SignalAndWait(ct);
            return (await sesion.PostAsync($"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct)).StatusCode;
        }

        var estados = await Task.WhenAll(Task.Run(() => Enviar(una), ct), Task.Run(() => Enviar(otra), ct));

        estados.Count(e => e == HttpStatusCode.OK).Should().Be(1);
        estados.Count(e => e == HttpStatusCode.Conflict).Should().Be(1, "la perdedora recibe un 409, no un 500");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().CountAsync(
            f => f.Concepto == $"Reserva corta {reservaId:D}" && f.Estado != EstadoFactura.Anulada, ct))
            .Should().Be(1);
    }

    private static object Cuerpo(
        Guid clienteId,
        Guid unidadId,
        DateOnly fecha,
        decimal importe,
        string moneda = "USD",
        string? observaciones = null) =>
        new
        {
            unidadId,
            clienteId,
            tipo = "Corta",
            modalidad = "MedioDia",
            entradaPrevista = fecha.ToString("yyyy-MM-dd"),
            moneda,
            importe,
            observaciones,
            dias = new[] { AyudaReserva.Franja(fecha) },
        };

    private static async Task<(Guid ReservaId, Guid ClienteId, Guid UnidadId, DateOnly Fecha)> ReservaAsync(
        SesionCliente sesion,
        int desplazamiento,
        CancellationToken ct)
    {
        var fecha = TiempoNegocio.Hoy().AddDays(desplazamiento);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var id = await AyudaReserva.CrearAsync(sesion, Cuerpo(clienteId, unidadId, fecha, 500m), ct);

        return (id, clienteId, unidadId, fecha);
    }

    private static async Task<Guid> FacturarAsync(SesionCliente sesion, Guid reservaId, Guid serieId, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync($"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("facturaId").GetGuid();
    }
}
