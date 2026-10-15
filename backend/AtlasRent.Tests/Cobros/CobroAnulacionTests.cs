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
/// F3-06: la corrección de un cobro. Sin motivo no se anula; con motivo, el cobro queda anulado, sus
/// asignaciones dejan de contar <b>sin borrarse</b>, las facturas vuelven a su pendiente anterior y el
/// crédito vuelve al contrato (§3.10.1, AGENTS.md §6).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroAnulacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task SinMotivoNoSeAnula()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var cobroId = await RegistrarAsync(sesion, clienteId, contratoId, hoy, 300m, formaPagoId, cuentaId, ct);

        var sinMotivo = await sesion.PostAsync($"/api/v1/cobros/{cobroId:D}/anular", new { motivo = "   " }, ct);
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest, await sinMotivo.Cuerpo());

        var ausente = await sesion.PostAsync($"/api/v1/cobros/{cobroId:D}/anular", new { }, ct);
        ausente.StatusCode.Should().Be(HttpStatusCode.BadRequest, await ausente.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == cobroId, ct)).Estado
            .Should().Be(EstadoCobro.Registrado, "un 400 no anula nada");
        (await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == facturaId && a.Vigente)
                .SumAsync(a => a.Importe, ct))
            .Should().Be(300m);
    }

    /// <summary>
    /// El caso que de verdad importa: anular un cobro devuelve las facturas a su pendiente anterior,
    /// deja de contar sin borrar las filas y devuelve el crédito al contrato.
    /// </summary>
    [Fact]
    public async Task AnularDevuelveElPendienteLasAsignacionesYElCredito()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        // Cobro de 500 contra una factura de 300: 300 a la factura y 200 de crédito.
        var cobroId = await RegistrarAsync(sesion, clienteId, contratoId, hoy, 500m, formaPagoId, cuentaId, ct);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(200m);

        var anulado = await sesion.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Se recibió un cheque de otra empresa" },
            ct);

        anulado.StatusCode.Should().Be(HttpStatusCode.OK, await anulado.Cuerpo());
        var cuerpo = await anulado.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("estado").GetString().Should().Be("Anulado");
        cuerpo.GetProperty("motivoAnulacion").GetString().Should().Be("Se recibió un cheque de otra empresa");
        cuerpo.GetProperty("totalAsignado").GetDecimal().Should().Be(0m, "lo anulado ya no cuenta");
        cuerpo.GetProperty("asignaciones").GetArrayLength().Should().Be(1, "pero la fila sigue ahí");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var cobro = await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Anulado);
        cobro.AnuladoAt.Should().NotBeNull();
        cobro.AnuladoPor.Should().NotBeNull();
        cobro.MotivoAnulacion.Should().Be("Se recibió un cheque de otra empresa");

        var filas = await AyudaCobros.AsignacionesAsync(bd, facturaId, ct);
        filas.Should().HaveCount(1, "anular no borra: la asignación sigue escrita con su importe");
        filas[0].Importe.Should().Be(300m);
        filas[0].Vigente.Should().BeFalse("pero ya no cuenta para nada");
        filas[0].CobroId.Should().Be(cobroId);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(0m, "devolver el cobro devuelve el crédito que dejó");
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.Id == cobroId, ct))
            .Should().Be(1, "el cobro tampoco se borra");

        var factura = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        factura.GetProperty("cobrado").GetDecimal().Should().Be(0m);
        factura.GetProperty("pendiente").GetDecimal().Should().Be(300m, "la factura vuelve a su pendiente anterior");
        factura.GetProperty("estadoCobro").GetString().Should().Be("Pendiente");
    }

    /// <summary>
    /// Anular dos veces no vale: el motivo del segundo intento sería el del primero, y un cobro
    /// anulado dos veces habría devuelto el crédito dos veces.
    /// </summary>
    [Fact]
    public async Task UnCobroYaAnuladoNoSeAnulaOtraVez()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var cobroId = await RegistrarAsync(sesion, clienteId, contratoId, hoy, 500m, formaPagoId, cuentaId, ct);

        var primera = await sesion.PostAsync($"/api/v1/cobros/{cobroId:D}/anular", new { motivo = "Error de captura" }, ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());

        var segunda = await sesion.PostAsync($"/api/v1/cobros/{cobroId:D}/anular", new { motivo = "otra vez" }, ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, await segunda.Cuerpo());

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(0m, "el crédito no vuelve a salir de un cobro que ya se anuló");
    }

    /// <summary>
    /// El crédito consumido por un cobro vuelve al contrato cuando ese cobro se anula: es la simetría
    /// de la regla y la que impide que anular un cobro con crédito se lleve el crédito de otro.
    /// </summary>
    [Fact]
    public async Task AnularDevuelveElCreditoQueElCobroHabiaConsumido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Nada que cobrar todavía: este cobro deja 300 de crédito.
        await RegistrarAsync(sesion, clienteId, contratoId, hoy, 300m, formaPagoId, cuentaId, ct);
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(300m);

        await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 250m, ct);

        var conCredito = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 50m, formaPagoId, cuentaId, usarSaldoAFavor: 300m),
            ct);

        conCredito.StatusCode.Should().Be(HttpStatusCode.OK, await conCredito.Cuerpo());
        var cuerpo = await conCredito.Content.ReadFromJsonAsync<JsonElement>(ct);
        var cobroId = cuerpo.GetProperty("id").GetGuid();
        cuerpo.GetProperty("totalAsignado").GetDecimal().Should().Be(250m, "el crédito pagó la factura entera");
        cuerpo.GetProperty("excedente").GetDecimal().Should().Be(100m);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(100m, "salen 300 de crédito y entran 100 de los 350");

        var anulado = await sesion.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Se pagó con otro cobro" },
            ct);

        anulado.StatusCode.Should().Be(HttpStatusCode.OK, await anulado.Cuerpo());
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(300m, "vuelve el crédito que el cobro consumió y sale el excedente que dejó");
    }

    private static async Task<Guid> RegistrarAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid contratoId,
        DateOnly hoy,
        decimal importe,
        Guid formaPagoId,
        Guid cuentaId,
        CancellationToken ct)
    {
        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, importe, formaPagoId, cuentaId),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }
}