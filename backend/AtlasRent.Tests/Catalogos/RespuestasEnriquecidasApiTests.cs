using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.CicloFactura;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Retenciones;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Catalogos;

/// <summary>
/// Campos que la interfaz necesita y no estaban: lo devuelto de un cobro, el comprobante fiscal y de
/// retención de la factura, el indicador de modificada tras envío y la lista de comprobantes de retención.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RespuestasEnriquecidasApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task ElCobroDevuelveLoDevueltoAlAnularUnaDeSusFacturas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var a = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);
        var b = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);
        var cobroId = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 1_000m, formaPagoId, cuentaId, ct, (a, 500m), (b, 500m));

        var antes = await LeerAsync(sesion, $"/api/v1/cobros/{cobroId:D}", ct);
        Numero(antes.GetProperty("importeDevuelto")).Should().Be(0m);

        await AyudaCiclo.AnularAsync(sesion, a, "Se emitió por error", ct);

        var despues = await LeerAsync(sesion, $"/api/v1/cobros/{cobroId:D}", ct);
        Numero(despues.GetProperty("importeDevuelto")).Should().Be(500m);
    }

    [Fact]
    public async Task LaFacturaFiscalDevuelveSuComprobanteFiscalYUnaSinSerieFiscalNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);

        var serieFiscal = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieFiscal, hoy, ct);
        var fiscal = await AyudaComprobante.BorradorAsync(admin, clienteId, serieFiscal, ct);
        await AyudaRetenciones.EmitirAsync(admin, fiscal, ct);

        var serieNormal = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var normal = await AyudaFacturas.BorradorConLineaAsync(admin, clienteId, serieNormal, Moneda.USD, ct);
        await AyudaRetenciones.EmitirAsync(admin, normal, ct);

        var conComprobante = await LeerAsync(sesion, $"/api/v1/facturas/{fiscal:D}", ct);
        conComprobante.GetProperty("comprobanteFiscal").GetString().Should().NotBeNullOrWhiteSpace();
        conComprobante.GetProperty("comprobanteFiscalId").GetGuid().Should().NotBeEmpty();
        conComprobante.GetProperty("modificadaTrasEnvio").GetBoolean().Should().BeFalse();
        conComprobante.GetProperty("comprobanteRetencionId").ValueKind.Should().Be(JsonValueKind.Null);

        var sinComprobante = await LeerAsync(sesion, $"/api/v1/facturas/{normal:D}", ct);
        sinComprobante.GetProperty("comprobanteFiscal").ValueKind.Should().Be(JsonValueKind.Null);
        sinComprobante.GetProperty("comprobanteFiscalId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task LasRespuestasDeEscrituraTraenLosMismosDatosFiscalesQueElDetalle()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieFiscal = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieFiscal, hoy, ct);
        var borrador = await AyudaComprobante.BorradorAsync(admin, clienteId, serieFiscal, ct);

        var emitida = await AyudaRetenciones.EmitirAsync(admin, borrador, ct);
        emitida.GetProperty("comprobanteFiscal").GetString().Should().NotBeNullOrWhiteSpace(
            "la respuesta de emitir ya trae el comprobante que acaba de asignar");
        emitida.GetProperty("comprobanteFiscalId").GetGuid().Should().NotBeEmpty();

        var detalle = await LeerAsync(admin, $"/api/v1/facturas/{borrador:D}", ct);
        emitida.GetProperty("comprobanteFiscal").GetString()
            .Should().Be(detalle.GetProperty("comprobanteFiscal").GetString());

        // Otra escritura sobre la emitida (referencia de e-factura) tampoco pierde el dato.
        var referencia = await admin.PutAsync(
            $"/api/v1/facturas/{borrador:D}/referencia-efactura", new { referencia = "E31-PRUEBA" }, ct);
        referencia.StatusCode.Should().Be(HttpStatusCode.OK);
        (await referencia.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("comprobanteFiscal")
            .GetString().Should().Be(detalle.GetProperty("comprobanteFiscal").GetString());
    }

    [Fact]
    public async Task LaFacturaQueRetieneDevuelveElIdDeSuComprobanteDeRetencion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var tipo = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(admin, tipo, ct);
        await AyudaRetenciones.EmitirAsync(admin, facturaId, ct);
        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);

        var factura = await LeerAsync(admin, $"/api/v1/facturas/{facturaId:D}", ct);

        factura.GetProperty("comprobanteRetencionId").GetGuid().Should().Be(comprobante!.Id);
    }

    [Fact]
    public async Task ComprobantesDeRetencionSeListanYSeLeenPorFacturaYPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var tipo = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Total, 10m, ct);
        var primera = await AyudaRetenciones.BorradorConRetencionAsync(admin, tipo, ct);
        var segunda = await AyudaRetenciones.BorradorConRetencionAsync(admin, tipo, ct);
        await AyudaRetenciones.EmitirAsync(admin, primera, ct);
        await AyudaRetenciones.EmitirAsync(admin, segunda, ct);
        var deLaPrimera = await AyudaRetenciones.ComprobanteAsync(bd, primera, ct);
        (await admin.PutAsync(
            $"/api/v1/comprobantes-retencion/{deLaPrimera!.Id:D}/recibir",
            new { numero = "607-LISTA-1", fechaRecepcion = "2026-10-16" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);

            var porFactura = await LeerAsync(sesion, $"/api/v1/comprobantes-retencion?facturaId={primera:D}", ct);
            porFactura.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())
                .Should().ContainSingle().Which.Should().Be(deLaPrimera.Id);
            porFactura.GetProperty("total").GetInt32().Should().Be(1);

            var pendientes = await LeerAsync(sesion, $"/api/v1/comprobantes-retencion?pendiente=true&pageSize=200", ct);
            var ids = pendientes.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ToList();
            ids.Should().NotContain(deLaPrimera.Id, "ya se recibió");
            ids.Should().Contain((await AyudaRetenciones.ComprobanteAsync(bd, segunda, ct))!.Id);

            var uno = await LeerAsync(sesion, $"/api/v1/comprobantes-retencion/{deLaPrimera.Id:D}", ct);
            uno.GetProperty("facturaId").GetGuid().Should().Be(primera);
            uno.GetProperty("numero").GetString().Should().Be("607-LISTA-1");
        }

        (await admin.GetAsync($"/api/v1/comprobantes-retencion/{Guid.NewGuid():D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ComprobantesDeRetencionConElPermisoRecortadoDan403()
    {
        var ct = TestContext.Current.CancellationToken;
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, AyudaFacturas.Contrasena, rol: Roles.Gerente, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, AyudaFacturas.Contrasena, rol: Roles.Admin, ct: ct);
        using (var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, AyudaFacturas.Contrasena, ct))
        {
            (await sesionAdmin.PutAsync(
                $"/api/v1/usuarios/{gerente.Id:D}/permisos",
                new { accion = Acciones.FacturaVer, permitido = false },
                ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, gerente, AyudaFacturas.Contrasena, ct);

        (await sesion.GetAsync("/api/v1/comprobantes-retencion", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync($"/api/v1/comprobantes-retencion/{Guid.NewGuid():D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> LeerAsync(SesionCliente sesion, string ruta, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(ruta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static decimal Numero(JsonElement v) =>
        v.ValueKind == JsonValueKind.String
            ? decimal.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : v.GetDecimal();
}
