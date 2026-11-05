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

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: la factura de la reserva corta (docs/ESPECIFICACION.md §3.4, «se cobra antes»).
/// <para>
/// Lo que se comprueba aquí es el anticipo, porque es donde está la trampa. D10 deja el anticipo
/// como <b>nota de la reserva y no como cobro</b>: la factura de la reserva no lo convierte en
/// dinero, no le asigna nada y no lo cuenta dos veces —no lo cuenta ni una vez, que es lo único
/// que se puede afirmar sin inventar un segundo camino de cobros—. Lo que §3.4 pide al crear la
/// factura es el aviso, y es lo que la respuesta devuelve.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaReservaCortaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Reserva corta con anticipo anotado: la factura se emite, el aviso del anticipo viene en la
    /// respuesta, la nota sigue intacta y no se ha creado ningún cobro.
    /// </summary>
    [Fact]
    public async Task LaFacturaDeLaReservaSeEmiteConElAvisoDelAnticipoYSinConvertirloEnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, clienteId) = await ReservaCortaAsync(admin, anticipo: 250m, nota: "Recibido en efectivo.");

        var respuesta = await admin.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

        var facturaId = cuerpo.GetProperty("facturaId").GetGuid();
        cuerpo.GetProperty("numero").GetInt32().Should().Be(1);
        cuerpo.GetProperty("anticipoAviso").GetString().Should().Contain("Anticipo anotado");
        cuerpo.GetProperty("anticipoAviso").GetString().Should().Contain("250");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var factura = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);

        factura.Estado.Should().Be(EstadoFactura.Emitida);
        factura.ClienteId.Should().Be(clienteId);
        factura.UnidadId.Should().Be((await contexto.Reserva.AsNoTracking()
            .Where(r => r.Id == reservaId).Select(r => r.UnidadId).SingleAsync(ct)));
        factura.ContratoId.Should().BeNull("una reserva corta no viene de un contrato");
        factura.Moneda.Should().Be(Moneda.USD);
        factura.Total.Should().Be(500m);
        factura.PeriodoInicio.Should().NotBeNull("también entra en la clave de no duplicado");

        // El anticipo sigue siendo una nota de la reserva, intacta.
        var reserva = await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == reservaId, ct);
        reserva.AnticipoImporte.Should().Be(250m);
        reserva.AnticipoNota.Should().Be("Recibido en efectivo.");

        // Y no se ha inventado un cobro: D10 dice que el anticipo no es cobro.
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(0, "el anticipo es una nota; convertirlo en cobro sería un segundo camino de dinero");
        (await contexto.CobroAsignacion.AsNoTracking().CountAsync(a => a.FacturaId == facturaId, ct))
            .Should().Be(0, "y no hay nada que pueda contarlo dos veces");
    }

    /// <summary>
    /// El estado de cobro de la factura de la reserva sale de los cobros, no del anticipo: con el
    /// anticipo anotado la factura está tan pendiente como sin él.
    /// </summary>
    [Fact]
    public async Task ElAnticipoAnotadoNoCambiaElEstadoDeCobroDeLaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _) = await ReservaCortaAsync(admin, anticipo: 250m);

        var facturaId = await FacturarReservaAsync(admin, reservaId, serieId, ct);
        var lectura = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);

        lectura.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await lectura.Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("cobrado").LeerDecimal().Should().Be(0m, "el anticipo no se ha convertido en cobro");
        cuerpo.GetProperty("pendiente").LeerDecimal().Should().Be(500m);
        cuerpo.GetProperty("estadoCobro").GetString().Should().Be(nameof(EstadoCobroFactura.Pendiente));
    }

    /// <summary>Sin anticipo, la factura de la reserva se emite igual y con todo el importe pendiente.</summary>
    [Fact]
    public async Task SinAnticipoLaFacturaDeLaReservaSeEmiteConTodoElImportePendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _) = await ReservaCortaAsync(admin, anticipo: null);

        var respuesta = await admin.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("anticipoAviso").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var factura = await contexto.Factura.AsNoTracking()
            .SingleAsync(f => f.Id == cuerpo.GetProperty("facturaId").GetGuid(), ct);

        factura.Total.Should().Be(500m);
        factura.NetoACobrar.Should().Be(500m);
        factura.Retencion.Should().Be(0m);
    }

    /// <summary>
    /// Facturar dos veces la misma reserva corta es un 409 con el número de la factura que ya
    /// existe, no una segunda factura. El índice no lo impide solo aquí —una factura de reserva no
    /// tiene contrato—: lo impide el servicio, y la clave del concepto lleva el identificador de la
    /// reserva para que dos reservas del mismo día no se confundan.
    /// </summary>
    [Fact]
    public async Task FacturarDosVecesLaMismaReservaEsConflictoYNoUnaSegundaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _) = await ReservaCortaAsync(admin, anticipo: null);

        var primera = await admin.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Content.ReadAsStringAsync(ct));

        var segunda = await admin.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await segunda.Content.ReadAsStringAsync(ct)).Should().Contain("ya tiene su factura");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().CountAsync(
            f => f.ContratoId == null && f.Concepto == $"Reserva corta {reservaId:D}", ct))
            .Should().Be(1);
    }

    /// <summary>
    /// Facturar una reserva necesita el permiso de emitir, no el de tocar la cartera: crear el
    /// documento es emitirlo, con número y comprobante si la serie es fiscal.
    /// </summary>
    [Fact]
    public async Task FacturarLaReservaCortaTambienPideElPermisoDeEmitir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var (reservaId, _) = await ReservaCortaAsync(admin, anticipo: null);

        var respuesta = await empleado.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>
    /// Reserva corta de un medio día, creada por la API con su cliente y su unidad. Va en dólares
    /// para no depender del tipo de cambio del día (BUG-006).
    /// </summary>
    private async Task<(Guid ReservaId, Guid ClienteId)> ReservaCortaAsync(
        SesionCliente sesion,
        decimal? anticipo,
        string? nota = null,
        CancellationToken? ct = null)
    {
        var cancelacion = ct ?? TestContext.Current.CancellationToken;
        var fecha = TiempoNegocio.Hoy().AddDays(10);

        var clienteId = await AyudaReserva.ClienteAsync(sesion, cancelacion);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, cancelacion);

        var reservaId = await AyudaReserva.CrearAsync(
            sesion,
            new
            {
                unidadId,
                clienteId,
                tipo = "Corta",
                modalidad = "MedioDia",
                entradaPrevista = fecha.ToString("yyyy-MM-dd"),
                moneda = "USD",
                importe = 500m,
                anticipoImporte = anticipo,
                anticipoNota = nota,
                dias = new[] { AyudaReserva.Franja(fecha) },
            },
            cancelacion);

        return (reservaId, clienteId);
    }

    private static async Task<Guid> FacturarReservaAsync(
        SesionCliente sesion,
        Guid reservaId,
        Guid serieId,
        CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("facturaId").GetGuid();
    }
}