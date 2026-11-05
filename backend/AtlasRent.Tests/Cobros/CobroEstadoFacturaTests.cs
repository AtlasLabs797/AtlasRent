using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// F3-06: el estado de cobro derivado que el DTO de factura expone (§3.8), que era el
/// <c>TODO(F3-06)</c> que dejó F3-02.
/// <para>
/// El cálculo en sí está probado sin base de datos en <see cref="CobroCalculoTests"/>; aquí se
/// comprueba que sale por la API, con el dato real leído de las tablas.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroEstadoFacturaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Los tres estados y la vencimiento, en una sola cartera: pagada, parcial y vencida. Se lee cada
    /// factura por su endpoint, que es el DTO que consume la vista de cuentas (§3.13).
    /// </summary>
    [Fact]
    public async Task LaFacturaExponeCobradoPendienteEstadoYVencida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var pagada = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var parcial = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(20), 400m, ct);
        var vencida = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(-9), 500m, ct);

        var sinCobrar = await AyudaCobros.FacturaAsync(sesion, pagada, ct);
        sinCobrar.GetProperty("cobrado").LeerDecimal().Should().Be(0m);
        sinCobrar.GetProperty("pendiente").LeerDecimal().Should().Be(300m);
        sinCobrar.GetProperty("estadoCobro").GetString().Should().Be("Pendiente");
        sinCobrar.GetProperty("vencida").GetBoolean().Should().BeFalse();

        // La más vencida va primera en la FIFO, así que este cobro la deja a medias. Es justo el caso
        // que hay que ver: parcial y vencida a la vez.
        var primero = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 200m, formaPagoId, cuentaId),
            ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Cuerpo());

        var tercera = await AyudaCobros.FacturaAsync(sesion, vencida, ct);
        tercera.GetProperty("cobrado").LeerDecimal().Should().Be(200m);
        tercera.GetProperty("pendiente").LeerDecimal().Should().Be(300m);
        tercera.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
        tercera.GetProperty("vencida").GetBoolean().Should().BeTrue("su vencimiento fue hace nueve días");

        // El segundo cobro dice a dónde va: una entera y parte de otra.
        var segundo = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaId,
                asignaciones: new[] { new { facturaId = pagada, importe = 300m }, new { facturaId = parcial, importe = 200m } }),
            ct);

        segundo.StatusCode.Should().Be(HttpStatusCode.OK, await segundo.Cuerpo());

        var primera = await AyudaCobros.FacturaAsync(sesion, pagada, ct);
        primera.GetProperty("cobrado").LeerDecimal().Should().Be(300m);
        primera.GetProperty("pendiente").LeerDecimal().Should().Be(0m);
        primera.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        primera.GetProperty("vencida").GetBoolean().Should().BeFalse();

        var segunda = await AyudaCobros.FacturaAsync(sesion, parcial, ct);
        segunda.GetProperty("cobrado").LeerDecimal().Should().Be(200m);
        segunda.GetProperty("pendiente").LeerDecimal().Should().Be(200m);
        segunda.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
        segunda.GetProperty("vencida").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Una factura a la que se le ha pagado parte y además venció está <b>Parcial</b> y <b>vencida</b>:
    /// son dos cosas distintas y la vista de cuentas necesita las dos.
    /// </summary>
    [Fact]
    public async Task UnaFacturaParcialYNVencidaSaleParcialYVencida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(-5), 300m, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 120m, formaPagoId, cuentaId),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("cobrado").LeerDecimal().Should().Be(120m);
        leida.GetProperty("pendiente").LeerDecimal().Should().Be(180m);
        leida.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
        leida.GetProperty("vencida").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// La lista de facturas trae también el estado derivado, resuelto en una consulta agregada sobre la
    /// página y no con una consulta por fila (§3.13).
    /// </summary>
    [Fact]
    public async Task LaListaDeFacturasTraeElEstadoDerivado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var listado = await sesion.GetAsync($"/api/v1/facturas?clienteId={clienteId:D}&pageSize=200", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.OK, await listado.Cuerpo());

        var cuerpo = await listado.Content.ReadFromJsonAsync<JsonElement>(ct);
        var fila = cuerpo.GetProperty("data").EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == facturaId);

        fila.GetProperty("cobrado").LeerDecimal().Should().Be(300m);
        fila.GetProperty("pendiente").LeerDecimal().Should().Be(0m);
        fila.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        fila.GetProperty("vencida").GetBoolean().Should().BeFalse();
    }
}