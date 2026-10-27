using System.Net;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// BUG-036: modificar una factura con retención deja el comprobante de retención con los importes
/// viejos. Pendiente: se actualiza. Recibido: el cliente ya entregó un papel con ese importe, así que
/// la modificación se bloquea (409).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RetencionModificacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private static object Cuerpo(Guid lineaId, decimal precio) => new
    {
        lineas = new[]
        {
            new
            {
                lineaId,
                tipo = nameof(TipoLineaFactura.Renta),
                concepto = "Renta corregida",
                cantidad = 1m,
                precioUnitario = precio,
            },
        },
    };

    private static Guid LineaRenta(JsonElement factura) =>
        factura.GetProperty("lineas").EnumerateArray()
            .First(l => l.GetProperty("tipo").GetString() == nameof(TipoLineaFactura.Renta))
            .GetProperty("id").GetGuid();

    [Fact]
    public async Task ModificarConComprobantePendienteActualizaSuImporte()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipo = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipo, ct);
        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        (await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct))!.Importe.Should().Be(100m);

        var modificada = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar", Cuerpo(LineaRenta(emitida), 2_000m), ct);
        modificada.StatusCode.Should().Be(HttpStatusCode.OK, await modificada.Cuerpo());

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);
        comprobante!.Importe.Should().Be(200m, "el comprobante sigue a la retención de la factura");
        comprobante.Porcentaje.Should().Be(10m);
        comprobante.Estado.Should().Be(EstadoComprobanteRetencion.Pendiente);
        (await AyudaRetenciones.TotalesAsync(bd, facturaId, ct)).Retencion.Should().Be(200m);
    }

    [Fact]
    public async Task ModificarConComprobanteRecibidoSeBloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipo = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipo, ct);
        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);
        var recibida = await sesion.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante!.Id:D}/recibir",
            new { numero = "607-MOD-1", fechaRecepcion = "2026-10-15" },
            ct);
        recibida.StatusCode.Should().Be(HttpStatusCode.OK, await recibida.Cuerpo());

        var intento = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar", Cuerpo(LineaRenta(emitida), 2_000m), ct);
        intento.StatusCode.Should().Be(HttpStatusCode.Conflict, await intento.Cuerpo());

        (await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct))!.Importe.Should().Be(100m);
        (await AyudaRetenciones.TotalesAsync(bd, facturaId, ct)).Retencion.Should().Be(100m);
    }
}
