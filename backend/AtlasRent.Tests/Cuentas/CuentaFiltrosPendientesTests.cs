using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: los filtros de §3.13 que <b>no</b> se aceptan, y por qué.
/// <para>
/// §3.13 pide «no enviadas», «envío fallido» y «modificadas tras envío». Los tres dependen de saber si
/// un documento se envió y con qué resultado, y eso es F4-01, que todavía no existe.
/// </para>
/// <para>
/// La decisión es no aceptarlos. Un filtro que se acepta y no se filtra devuelve filas que parecen
/// filtradas y alguien se fía de ellas —«estas son las que no se han enviado»— para perseguir facturas
/// que ya están enviadas. Un filtro que no existe, en cambio, se nota: la pantalla no lo tiene.
/// <c>TODO(A#)</c>: los tres se aceptan en F4-01, junto con el <c>estadoEnvio</c> de la fila, que por
/// eso ahora vuelve siempre en <c>null</c>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaFiltrosPendientesTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Los tres filtros de envío no existen en la API. No es un 400 con una lista de campos válidos —
    /// que sería otra forma de documentarlos— sino que no están, y esta prueba lo fija para que
    /// F4-01 sepa qué añadir.
    /// </summary>
    [Theory]
    [InlineData("noEnviadas=true")]
    [InlineData("envioFallido=true")]
    [InlineData("modificadasTrasEnvio=true")]
    public async Task LosFiltrosDeEnvioNoSeAceptanTodavia(string consulta)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var conFiltro = await admin.GetAsync(
            $"/api/v1/cuentas?clienteId={escenario.ClienteId:D}&{consulta}", ct);

        // La respuesta es la misma que sin filtro: el parámetro no existe y ASP.NET lo ignora. Lo que
        // se fija aquí es que el conjunto NO cambia, porque aceptarlo y devolverlo sin filtrar es
        // justo lo que no se quiere.
        conFiltro.StatusCode.Should().Be(HttpStatusCode.OK);
        AyudaCuentas.Contiene(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}&{consulta}", ct),
            facturaId)
            .Should().BeTrue(
                "F4-01 es quien va a filtrar esto: hoy el parámetro no existe y la vista no lo aplica");
    }

    /// <summary>
    /// El <c>estadoEnvio</c> de la fila es siempre nulo, por el mismo motivo. F4-01 lo rellena.
    /// </summary>
    [Fact]
    public async Task ElEstadoDeEnvioDeCadaFilaVieneVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        var filas = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct));

        filas.Should().NotBeEmpty();
        filas.Should().OnlyContain(
            f => f.GetProperty("estadoEnvio").ValueKind == System.Text.Json.JsonValueKind.Null);
    }
}