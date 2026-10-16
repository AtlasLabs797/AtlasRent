using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// F3-08: modificar una factura emitida (docs/ESPECIFICACION.md §3.8).
/// <para>
/// Lo que separa esta prueba de una edición de borrador es lo que <b>no</b> puede cambiar: ni el
/// número, ni la fecha de emisión, ni el tipo de cambio congelado. Y con comprobante fiscal no cambia
/// ni un importe, porque el documento ya salió con un número de comprobante detrás.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaModificacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Cambia el importe de una línea y los totales se recalculan; el número, la fecha de emisión y el
    /// cambio congelado se quedan donde estaban. La marca de modificación queda puesta con su autor.
    /// </summary>
    [Fact]
    public async Task ModificarSinComprobanteRecalculaYNoTocaLoEmitido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        var conLinea = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 2m, 50m, null, ct);

        var lineaId = conLinea.GetProperty("lineas")[0].GetProperty("id").GetGuid();

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        var antes = await emitida.Content.ReadFromJsonAsync<JsonElement>(ct);
        var numero = antes.GetProperty("numero").GetInt32();
        var fechaEmision = antes.GetProperty("fechaEmision").GetString();

        var modificada = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar",
            new
            {
                lineas = new[]
                {
                    new
                    {
                        lineaId,
                        tipo = nameof(TipoLineaFactura.Renta),
                        concepto = "Renta del mes corregida",
                        cantidad = 3m,
                        precioUnitario = 40m,
                    },
                },
            },
            ct);

        modificada.StatusCode.Should().Be(HttpStatusCode.OK, await modificada.Cuerpo());

        var cuerpo = await modificada.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("total").GetDecimal().Should().Be(120m, "los totales se recalculan con la línea nueva");
        cuerpo.GetProperty("netoACobrar").GetDecimal().Should().Be(120m);
        cuerpo.GetProperty("lineas")[0].GetProperty("concepto").GetString().Should().Be("Renta del mes corregida");
        cuerpo.GetProperty("lineas")[0].GetProperty("importe").GetDecimal().Should().Be(120m);

        var fila = await AyudaCiclo.FilaAsync(bd, facturaId, ct);
        fila.Numero.Should().Be(numero, "el número emitido no se toca jamás");
        fila.Estado.Should().Be(EstadoFactura.Emitida);
        fila.Total.Should().Be(120m);

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("fechaEmision").GetString()
            .Should().Be(fechaEmision, "la fecha de emisión es de la emisión");
        leida.GetProperty("tipoCambioValor").GetDecimal()
            .Should().Be(antes.GetProperty("tipoCambioValor").GetDecimal(), "el cambio congelado es de la emisión");

        // La marca de «modificada tras envío» que van a filtrar F3-10 y F4-02.
        fila.ModificadaAt.Should().NotBeNull();
        fila.ModificadaPor.Should().NotBeNull();

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var auditoria = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == "factura.modificada" && a.EntidadId == facturaId.ToString("D"))
            .SingleAsync(ct);

        auditoria.Ip.Should().NotBeNullOrWhiteSpace("toda mutación lleva IP (AGENTS.md §6)");
        auditoria.Resultado.Should().Be("permitido");
        auditoria.Origen.Should().Be("api");
    }

    /// <summary>
    /// Con comprobante fiscal los importes no se tocan. No es un capricho del servicio: el
    /// comprobante es lo que declara la DGII y cambiar el importe detrás dejaría la declaración y el
    /// libro contable diciendo cosas distintas.
    /// </summary>
    [Fact]
    public async Task ConComprobanteFiscalLosImportesNoSeModifican()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        await AyudaComprobante.NumeradorAsync(sesion, serieId, hoy, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        var conLinea = await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        var lineaId = conLinea.GetProperty("lineas")[0].GetProperty("id").GetGuid();

        var intento = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar",
            new
            {
                lineas = new[]
                {
                    new
                    {
                        lineaId,
                        tipo = nameof(TipoLineaFactura.Renta),
                        concepto = "Otro importe",
                        cantidad = 1m,
                        precioUnitario = 999m,
                    },
                },
            },
            ct);

        intento.StatusCode.Should().Be(HttpStatusCode.Conflict, await intento.Cuerpo());

        var fila = await AyudaCiclo.FilaAsync(bd, facturaId, ct);
        fila.Total.Should().Be(100m, "un 409 no cambia nada");
        fila.ModificadaAt.Should().BeNull();
        fila.Estado.Should().Be(EstadoFactura.Emitida);
    }
}