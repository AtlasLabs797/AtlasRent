using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using Microsoft.EntityFrameworkCore;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: las columnas de §3.13 en la vista única de cuentas.
/// <para>
/// Una factura con comprobante, una proforma y una fianza tienen que salir con la insignia de tipo
/// correcta y con todas las columnas, y lo que no aplica en una de ellas tiene que venir <b>vacío
/// y no en cero</b>. Es la diferencia entre una vista y un formulario: un cero en la columna de
/// cobrado de una proforma se lee como «esta factura no está cobrada», que es una deuda que no
/// existe.
/// </para>
/// <para>
/// Las proformas van con filtro y por defecto no salen: §3.13 lo dice así y es lo razonable, porque
/// un presupuesto no es una cuenta y no debe ensuciar el saldo de quien mira.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaColumnasTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Factura con comprobante, proforma y fianza salen las tres con sus columnas y con lo que no
    /// aplica en nulo.
    /// </summary>
    [Fact]
    public async Task FacturaProformaYFianzaSalenConSusColumnasYLoQueNoAplicaVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct, generaComprobante: true);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var proformaId = await AyudaCuentas.ProformaAsync(admin, escenario, 900m, ct);
        var fianzaId = await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        var vista = await AyudaCuentas.VistaAsync(
            admin,
            $"clienteId={escenario.ClienteId:D}&incluirProformas=true",
            ct);

        var filas = AyudaCuentas.Filas(vista);
        filas.Should().HaveCount(3);

        // ---------------------------------------------------------------- factura

        var factura = filas.Single(f => f.GetProperty("id").GetGuid() == facturaId);

        factura.GetProperty("tipo").GetString().Should().Be("Factura");
        factura.GetProperty("serieId").GetGuid().Should().Be(escenario.SerieId);
        factura.GetProperty("serieNumero").GetInt32().Should().BeGreaterThan(0);
        factura.GetProperty("numero").GetInt32().Should().BeGreaterThan(0);
        factura.GetProperty("comprobanteTipo").GetString().Should().NotBeNullOrWhiteSpace();
        factura.GetProperty("comprobanteNumero").GetInt32().Should().BeGreaterThan(0);
        factura.GetProperty("fecha").GetString().Should().Be(escenario.Hoy.ToString("yyyy-MM-dd"));
        factura.GetProperty("clienteId").GetGuid().Should().Be(escenario.ClienteId);
        factura.GetProperty("cliente").GetString().Should().NotBeNullOrWhiteSpace();
        factura.GetProperty("unidadId").GetGuid().Should().Be(escenario.UnidadId);
        factura.GetProperty("unidad").GetString().Should().NotBeNullOrWhiteSpace();
        factura.GetProperty("concepto").GetString().Should().NotBeNullOrWhiteSpace();
        factura.GetProperty("moneda").GetString().Should().Be(nameof(Moneda.USD));
        factura.GetProperty("total").LeerDecimal().Should().Be(300m);
        factura.GetProperty("retencion").LeerDecimal().Should().Be(0m);
        factura.GetProperty("cobrado").LeerDecimal().Should().Be(0m);
        factura.GetProperty("pendiente").LeerDecimal().Should().Be(300m);
        factura.GetProperty("saldoADevolver").ValueKind.Should().Be(JsonValueKind.Null);
        factura.GetProperty("vencimiento").GetString().Should().Be(escenario.Hoy.AddDays(10).ToString("yyyy-MM-dd"));
        factura.GetProperty("diasVencida").GetInt32().Should().Be(0);
        factura.GetProperty("vencida").GetBoolean().Should().BeFalse();
        factura.GetProperty("estado").GetString().Should().Be(nameof(EstadoCobroFactura.Pendiente));
        factura.GetProperty("estadoEnvio").ValueKind
            .Should().Be(JsonValueKind.Null, "el estado de envío es de F4-01 y no se inventa");

        // ---------------------------------------------------------------- proforma

        var proforma = filas.Single(f => f.GetProperty("id").GetGuid() == proformaId);

        proforma.GetProperty("tipo").GetString().Should().Be("Proforma");
        proforma.GetProperty("total").LeerDecimal().Should().Be(900m, "su importe sí se ve");
        proforma.GetProperty("comprobanteTipo").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("comprobanteNumero").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("retencion").ValueKind.Should().Be(JsonValueKind.Null, "no hay retención que pedir");
        proforma.GetProperty("cobrado").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("pendiente").ValueKind.Should().Be(JsonValueKind.Null, "una proforma no genera deuda");
        proforma.GetProperty("saldoADevolver").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("vencimiento").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("diasVencida").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("vencida").ValueKind.Should().Be(JsonValueKind.Null);
        proforma.GetProperty("cobrable").GetBoolean()
            .Should().BeFalse("no se cobra un presupuesto: se convierte en factura");

        // ---------------------------------------------------------------- fianza

        var fianza = filas.Single(f => f.GetProperty("id").GetGuid() == fianzaId);

        fianza.GetProperty("tipo").GetString().Should().Be("Fianza");
        fianza.GetProperty("serieId").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("serieNumero").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("numero").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("comprobanteTipo").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("total").LeerDecimal().Should().Be(500m, "el total de la fianza es lo exigido");
        fianza.GetProperty("retencion").ValueKind.Should().Be(JsonValueKind.Null, "una fianza no retiene");
        fianza.GetProperty("saldoADevolver").LeerDecimal().Should().Be(0m);
        fianza.GetProperty("vencimiento").ValueKind.Should().Be(JsonValueKind.Null, "una fianza no vence");
        fianza.GetProperty("diasVencida").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("vencida").ValueKind.Should().Be(JsonValueKind.Null);
        fianza.GetProperty("contratoId").GetGuid().Should().Be(escenario.ContratoId);
        fianza.GetProperty("clienteId").GetGuid().Should().Be(escenario.ClienteId);
        fianza.GetProperty("unidadId").GetGuid().Should().Be(escenario.UnidadId);
        fianza.GetProperty("concepto").GetString().Should().Contain("Fianza");
    }

    /// <summary>
    /// Sin el filtro, las proformas no salen. §3.13 las pone «con filtro» y una vista de cuentas que
    /// mezclara presupuestos con deudas no respondería a la pregunta para la que existe.
    /// </summary>
    [Fact]
    public async Task LasProformasNoSalenSiNoSePiden()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var proformaId = await AyudaCuentas.ProformaAsync(admin, escenario, 900m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var sinFiltro = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        AyudaCuentas.Contiene(sinFiltro, proformaId).Should().BeFalse();

        var conFiltro = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&incluirProformas=true", ct);
        AyudaCuentas.Contiene(conFiltro, proformaId).Should().BeTrue();
    }

    /// <summary>
    /// El icono informativo de la reserva con anticipo (§3.13) y el <c>estadoEnvio</c> vacío, que es
    /// lo que puede afirmarse hoy: que el sistema todavía no sabe si un documento se ha enviado.
    /// </summary>
    [Fact]
    public async Task LaFacturaDeUnaReservaConAnticipoLlevaElIconoYElEstadoDeEnvioVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var (reservaId, clienteId) = await ReservaCortaConAnticipoAsync(admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var facturada = await admin.PostAsync(
            $"/api/v1/reservas/{reservaId:D}/factura", new { serieId }, ct);
        facturada.StatusCode.Should().Be(HttpStatusCode.OK, await facturada.Content.ReadAsStringAsync(ct));

        var facturaId = (await facturada.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("facturaId").GetGuid();

        var vista = await AyudaCuentas.VistaAsync(admin, $"clienteId={clienteId:D}", ct);
        var fila = AyudaCuentas.Filas(vista).Single(f => f.GetProperty("id").GetGuid() == facturaId);

        // F3-05 no dejó una columna que una la factura con su reserva: la marca en el concepto. Eso es
        // lo que la vista comprueba, y esta línea deja dicho de dónde sale el icono.
        var leida = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        (await leida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("concepto").GetString()
            .Should().Be($"Reserva corta {reservaId:D}");

        fila.GetProperty("tieneReservaConAnticipo").GetBoolean()
            .Should().BeTrue("la reserva con anticipo anotado se marca con un icono");

        // La fila es de dinero: la factura existe y se ve con su total y su pendiente.
        fila.GetProperty("total").LeerDecimal().Should().Be(500m);
        fila.GetProperty("pendiente").LeerDecimal().Should().Be(500m, "el anticipo es una nota, no un cobro (D10)");
        fila.GetProperty("estadoEnvio").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Reserva corta con anticipo anotado, en dólares para no depender del tipo de cambio del día
    /// (BUG-006), que es el mismo camino que usa la prueba de F3-05.
    /// </summary>
    private static async Task<(Guid ReservaId, Guid ClienteId)> ReservaCortaConAnticipoAsync(
        SesionCliente sesion,
        CancellationToken ct)
    {
        var fecha = TiempoNegocio.Hoy().AddDays(10);

        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

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
                anticipoImporte = 250m,
                anticipoNota = "Recibido en efectivo.",
                dias = new[] { AyudaReserva.Franja(fecha) },
            },
            ct);

        return (reservaId, clienteId);
    }
}
