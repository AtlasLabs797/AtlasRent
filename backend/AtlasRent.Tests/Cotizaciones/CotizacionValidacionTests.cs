using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cotizaciones;

/// <summary>
/// F2-02: las reglas del documento (docs/ESPECIFICACION.md §3.3). Un 409 para lo que no se puede
/// guardar y ningún importe con decimales escondidos: todo se redondea medio hacia arriba.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CotizacionValidacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El importe de una línea se redondea medio hacia arriba (AGENTS.md §6): 1,5 × 0,07 = 0,105 y
    /// half-even dejaría 0,10. Aquí se comprueba contra el 0,11 que quiere la empresa.
    /// </summary>
    [Fact]
    public async Task ElImporteDeLaLineaSeRedondeaMedioHaciaArriba()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var id = await AyudaCotizacion.CrearAsync(
            gerente,
            await AyudaCotizacion.ClienteAsync(gerente, ct),
            lineas: new[] { AyudaCotizacion.Linea("Renta fraccionada", 1.5m, 0.07m) },
            ct: ct);

        var cotizacion = await AyudaCotizacion.Json(
            await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct);

        var linea = cotizacion.GetProperty("lineas")[0];
        linea.GetProperty("importe").GetDecimal().Should().Be(0.11m, "0,105 sube a 0,11, no baja a 0,10");
        cotizacion.GetProperty("total").GetDecimal().Should().Be(0.11m, "el total es la suma de las líneas");
        cotizacion.GetProperty("subtotal").GetDecimal().Should().Be(0.11m, "sin impuestos, el subtotal es el total");
    }

    /// <summary>El total de varias líneas es la suma de sus importes, en el orden del documento.</summary>
    [Fact]
    public async Task ElTotalEsLaSumaDeLasLineas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var id = await AyudaCotizacion.CrearAsync(
            gerente,
            await AyudaCotizacion.ClienteAsync(gerente, ct),
            lineas: new[]
            {
                AyudaCotizacion.Linea("Mensualidad", 1m, 25000m),
                AyudaCotizacion.Linea("Luz", 3m, 450.55m),
                AyudaCotizacion.Linea("Mantenimiento", 1m, 500m),
            },
            ct: ct);

        var cotizacion = await AyudaCotizacion.Json(
            await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct);

        cotizacion.GetProperty("lineas")[1].GetProperty("importe").GetDecimal()
            .Should().Be(1351.65m, "3 × 450,55");
        cotizacion.GetProperty("total").GetDecimal()
            .Should().Be(25000m + 1351.65m + 500m);
        cotizacion.GetProperty("lineas").EnumerateArray()
            .Select(l => l.GetProperty("posicion").GetInt32())
            .Should().Equal([1, 2, 3], "el orden del documento no depende de como los devuelva la base");
    }

    /// <summary>
    /// La cotización es para un cliente o para un prospecto: los dos y ninguno se rechazan. La
    /// aplicación lo comprueba y la tabla lo vuelve a comprobar con un CHECK.
    /// </summary>
    [Fact]
    public async Task NoSePuedeCotizarAUnClienteYUnProspectoALMismoTiempo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var clienteId = await AyudaCotizacion.ClienteAsync(gerente, ct);
        var prospectoId = await AyudaCotizacion.ProspectoAsync(gerente, ct);

        (await AyudaCotizacion.CrearRawAsync(gerente, clienteId, prospectoId, ct: ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Y tampoco se puede cotizar en el vacío.</summary>
    [Fact]
    public async Task LaCotizacionNecesitaAlguienAQuienCotizar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        (await AyudaCotizacion.CrearRawAsync(gerente, clienteId: null, ct: ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Una línea sin decir qué se ofrece no es una línea. La documentación vacía la rechaza la
    /// validación del modelo antes de llegar al servicio (que también la rechaza, por si alguien la
    /// llama por dentro).
    /// </summary>
    [Fact]
    public async Task UnaLineaSinDescripcionNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        (await AyudaCotizacion.CrearRawAsync(
            gerente,
            await AyudaCotizacion.ClienteAsync(gerente, ct),
            lineas: new[] { AyudaCotizacion.Linea("   ", 1m, 100m) },
            ct: ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "una línea en blanco no es una descripción");
    }

    /// <summary>Una cantidad negativa no es una cantidad.</summary>
    [Fact]
    public async Task UnaCantidadNegativaNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        (await AyudaCotizacion.CrearRawAsync(
            gerente,
            await AyudaCotizacion.ClienteAsync(gerente, ct),
            lineas: new[] { AyudaCotizacion.Linea("Alquiler", -1m, 100m) },
            ct: ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Tampoco un precio negativo, ni una moneda que no exista.</summary>
    [Fact]
    public async Task UnPrecioNegativoOUnaMonedaInexistenteNoSeGuardan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var clienteId = await AyudaCotizacion.ClienteAsync(gerente, ct);

        (await AyudaCotizacion.CrearRawAsync(
            gerente, clienteId, lineas: new[] { AyudaCotizacion.Linea("Alquiler", 1m, -100m) }, ct: ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await gerente.PostAsync(
            "/api/v1/cotizaciones",
            new
            {
                clienteId,
                moneda = "Euro",
                lineas = new[] { AyudaCotizacion.Linea("Alquiler") },
            },
            ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "una moneda que no existe no deserializa");
    }

    /// <summary>
    /// Editar un borrador cambia sus líneas: se reemplazan enteras y el total se recalcula.
    /// </summary>
    [Fact]
    public async Task EditarUnBorradorReemplazaSusLineas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var clienteId = await AyudaCotizacion.ClienteAsync(gerente, ct);
        var id = await AyudaCotizacion.CrearAsync(
            gerente, clienteId, lineas: new[] { AyudaCotizacion.Linea("Mensualidad", 1m, 25000m) }, ct: ct);

        (await gerente.PutAsync(
            $"/api/v1/cotizaciones/{id:D}",
            AyudaCotizacion.Cuerpo(
                clienteId,
                lineas: new[]
                {
                    AyudaCotizacion.Linea("Mensualidad", 1m, 30000m),
                    AyudaCotizacion.Linea("Luz", 1m, 700m),
                }),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var cotizacion = await AyudaCotizacion.Json(
            await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct);

        cotizacion.GetProperty("lineas").GetArrayLength().Should().Be(2);
        cotizacion.GetProperty("total").GetDecimal().Should().Be(30700m);
    }

    /// <summary>La baja es lógica: la fila se queda, con su activo a false.</summary>
    [Fact]
    public async Task LaBajaEsLogicaYSoloEnBorrador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);

        (await gerente.DeleteAsync($"/api/v1/cotizaciones/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var lista = await AyudaCotizacion.Json(await gerente.GetAsync("/api/v1/cotizaciones", ct), ct);
        lista.GetProperty("data").EnumerateArray()
            .Should().NotContain(c => c.GetProperty("id").GetGuid() == id, "la baja es lógica");

        (await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "la fila sigue ahí, se puede auditar");
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}