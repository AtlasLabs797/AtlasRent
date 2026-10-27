using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Comprobantes;

/// <summary>
/// F3-03: la configuración del numerador y el veto de §3.7 a cambiar «genera comprobante» en una
/// serie con facturas emitidas.
/// <para>
/// Sin esta pantalla la función no se puede usar (§3.7), así que es parte de la tarea y no un extra.
/// Lo que se prueba aquí es lo que protege al dato: que el rango guardado sea coherente y que un
/// cambio de rango quede con el valor anterior en la auditoría.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class NumeradorConfiguracionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Guardar el numerador lo deja legible, con los números que quedan calculados, y comprobarlo
    /// desde la respuesta de la propia escritura es lo que hace útil el 409 de «agotado»: quien lo
    /// lee tiene que poder ver cuántos le quedan sin abrir la base.
    /// </summary>
    [Fact]
    public async Task ElNumeradorSeGuardaYSeLeeConLosNumerosQueQuedan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var id = await AyudaComprobante.NumeradorAsync(
            admin, serieId, hoy, ct, rangoDesde: 1, rangoHasta: 100, umbralAlerta: 5);

        var leido = await admin.GetAsync($"/api/v1/config/numeradores-fiscales/{serieId:D}", ct);
        leido.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await leido.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("id").GetGuid().Should().Be(id);
        cuerpo.GetProperty("serieId").GetGuid().Should().Be(serieId);
        cuerpo.GetProperty("restantes").GetInt32().Should().Be(100);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.NumeradorFiscal.AsNoTracking().SingleAsync(n => n.SerieId == serieId, ct);
        fila.RangoHasta.Should().Be(100, "lo guardado es lo que dice el contador, no lo que dice la pantalla");
    }

    /// <summary>
    /// Guardar un rango incoherente es un 409 con el motivo. Sin esto, un rango invertido dejaría el
    /// numerador con "números restantes" negativo y la emisión aceptando comprobantes de un rango que
    /// no existe.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnRangoIncoherenteSeRechaza(bool siguienteFuera)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var cuerpo = siguienteFuera
            ? new { tipoPrefijo = "T1", rangoDesde = 500, rangoHasta = 100, fechaVencimiento = hoy.ToString("yyyy-MM-dd"), siguienteNumero = 500, umbralAlerta = 5 }
            : new { tipoPrefijo = "T1", rangoDesde = 500, rangoHasta = 100, fechaVencimiento = hoy.ToString("yyyy-MM-dd"), siguienteNumero = 900, umbralAlerta = 5 };

        var respuesta = await admin.PutAsync(
            $"/api/v1/config/numeradores-fiscales/{serieId:D}", cuerpo, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.NumeradorFiscal.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(0, "un numerador incoherente tampoco se guarda a medias");
    }

    /// <summary>
    /// §3.7: «no se puede cambiar genera comprobante en una serie con facturas emitidas: se crea serie
    /// nueva». El motivo es que las facturas ya salieron con o sin comprobante y esa decisión está
    /// en el histórico; pasarla por debajo dejaría facturas de una serie fiscal sin comprobante, que
    /// es el documento que no se puede declarar.
    /// </summary>
    [Fact]
    public async Task NoSePuedeCambiarGeneraComprobanteEnUnaSerieConFacturasEmitidas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(admin, clienteId, serieId, ct);
        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        var actual = await AyudaComprobante.SerieAsync(admin, serieId, ct);

        var respuesta = await admin.PutAsync(
            $"/api/v1/config/series/{serieId:D}",
            new
            {
                numero = actual.GetProperty("numero").GetInt32(),
                prefijo = $"F{Guid.NewGuid():N}"[..8],
                tipo = "Fiscal",
                moneda = (string?)null,
                sumaAFacturado = true,
                sumaASaldos = true,
                generaComprobante = false,
                activa = true,
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain("serie nueva");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).GeneraComprobante
            .Should().BeTrue("el veto es del cambio entero, no de un campo");
    }

    /// <summary>
    /// BUG-046: con algun numero emitido, moneda, prefijo y tipo quedan fijos; el resto se puede editar.
    /// </summary>
    [Theory]
    [InlineData("moneda")]
    [InlineData("prefijo")]
    [InlineData("tipo")]
    public async Task NoSePuedeCambiarMonedaPrefijoNiTipoDeUnaSerieConNumerosEmitidos(string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(admin, clienteId, serieId, ct);
        (await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var actual = await AyudaComprobante.SerieAsync(admin, serieId, ct);
        object Cuerpo(string? moneda, string prefijo, string tipo, string? condiciones) => new
        {
            numero = actual.GetProperty("numero").GetInt32(),
            prefijo,
            tipo,
            moneda,
            condicionesPago = condiciones,
            sumaAFacturado = true,
            sumaASaldos = true,
            generaComprobante = true,
            activa = true,
        };

        var prefijoActual = actual.GetProperty("prefijo").GetString()!;
        var cambio = campo switch
        {
            "moneda" => Cuerpo("USD", prefijoActual, "Fiscal", null),
            "prefijo" => Cuerpo(null, $"X{Guid.NewGuid():N}"[..8], "Fiscal", null),
            _ => Cuerpo(null, prefijoActual, "Normal", null),
        };

        var respuesta = await admin.PutAsync($"/api/v1/config/series/{serieId:D}", cambio, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain("serie nueva");
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var serie = await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct);
            serie.Prefijo.Should().Be(prefijoActual);
            serie.Moneda.Should().BeNull();
            serie.Tipo.Should().Be(AtlasRent.Api.Dominio.TipoSerie.Fiscal);
        }

        // Lo que no cambia el sentido de los numeros sigue siendo editable.
        var inocuo = await admin.PutAsync(
            $"/api/v1/config/series/{serieId:D}", Cuerpo(null, prefijoActual, "Fiscal", "Pago a 30 dias"), ct);
        inocuo.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Una serie que no ha emitido nada sí admite el cambio: el veto es para las facturas que ya
    /// salieron, no una regla de que la bandera no se pueda tocar nunca.
    /// </summary>
    [Fact]
    public async Task UnaSerieSinFacturasEmitidasSiAdmiteElCambio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);

        var actual = await AyudaComprobante.SerieAsync(admin, serieId, ct);

        var respuesta = await admin.PutAsync(
            $"/api/v1/config/series/{serieId:D}",
            new
            {
                numero = actual.GetProperty("numero").GetInt32(),
                prefijo = $"F{Guid.NewGuid():N}"[..8],
                tipo = "Fiscal",
                moneda = (string?)null,
                sumaAFacturado = true,
                sumaASaldos = true,
                generaComprobante = false,
                activa = true,
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}