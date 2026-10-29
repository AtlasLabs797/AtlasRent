using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasRent.Tests.Api;

/// <summary>
/// AGENTS.md §6: en TypeScript el dinero es un string decimal. La API escribe los decimales como
/// texto con su escala y los enums como texto, y sigue leyendo números y enteros.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class SerializacionJsonTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    private JsonSerializerOptions OpcionesApi() =>
        api.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    [Fact]
    public async Task UnTipoDeCambioDeLaApiTraeElValorComoStringConSuEscala()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        var fecha = AyudaTipoCambio_Dia(400);

        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { fecha = fecha.ToString("yyyy-MM-dd"), moneda = "DOP", valor = 58.5m },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var pagina = await sesion.GetAsync(
            $"/api/v1/tipos-cambio?moneda=DOP&desde={fecha:yyyy-MM-dd}&hasta={fecha:yyyy-MM-dd}", ct);
        pagina.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await pagina.Content.ReadFromJsonAsync<JsonElement>(ct);
        var fila = cuerpo.GetProperty("data").EnumerateArray().Single();
        fila.GetProperty("valor").ValueKind.Should().Be(JsonValueKind.String);
        fila.GetProperty("valor").GetString().Should().Be("58.500000", "numeric(18,6) conserva 6 decimales");
        fila.GetProperty("moneda").GetString().Should().Be("DOP");
    }

    private static DateOnly AyudaTipoCambio_Dia(int n) => TipoCambio.AyudaTipoCambio.Dia(n);

    [Fact]
    public void ElDecimalSeEscribeComoStringInvariantConLaEscalaQueTrae()
    {
        var opciones = OpcionesApi();

        JsonSerializer.Serialize(new { a = 1500.00m, b = 7m, c = (decimal?)null, d = -0.5m, e = 1.234567m }, opciones)
            .Should().Be("{\"a\":\"1500.00\",\"b\":\"7\",\"c\":null,\"d\":\"-0.5\",\"e\":\"1.234567\"}");
    }

    [Fact]
    public void ElDecimalSigueLeyendoseDesdeNumeroOString()
    {
        var opciones = OpcionesApi();

        JsonSerializer.Deserialize<decimal>("1500.25", opciones).Should().Be(1500.25m);
        JsonSerializer.Deserialize<decimal>("\"1500.25\"", opciones).Should().Be(1500.25m);
        JsonSerializer.Deserialize<decimal?>("null", opciones).Should().BeNull();
        var enorme = () => JsonSerializer.Deserialize<decimal>("\"99999999999999999.00\"", opciones);
        enorme.Should().Throw<JsonException>("SEC-030: el tope sigue vigente");
    }

    /// <summary>AUD-10 (H-02): "1,50" no puede leerse como 150 por el separador de miles.</summary>
    [Theory]
    [InlineData("\"1,50\"")]
    [InlineData("\"1,500.00\"")]
    [InlineData("\" 15\"")]
    [InlineData("\"1e3\"")]
    [InlineData("\"abc\"")]
    public void UnImporteEnTextoSoloAdmiteSignoYPuntoDecimal(string json)
    {
        var leer = () => JsonSerializer.Deserialize<decimal>(json, OpcionesApi());

        leer.Should().Throw<JsonException>();
        JsonSerializer.Deserialize<decimal>("\"-1.50\"", OpcionesApi()).Should().Be(-1.50m);
    }

    [Fact]
    public void LosEnumsSinAtributoSalenComoTextoYSeLeenDeTextoOEntero()
    {
        var opciones = OpcionesApi();
        var resumen = new ContratoResumen(
            Guid.Empty, Guid.Empty, null, Guid.Empty, null, Guid.Empty,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m, Moneda.USD,
            EstadoContrato.Activo, null, null, DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.SerializeToElement(resumen, opciones);
        json.GetProperty("estado").GetString().Should().Be("Activo");
        json.GetProperty("renta").GetString().Should().Be("100");

        var solicitud = new SolicitudAprobacionRespuesta(
            Guid.Empty, ResultadoSolicitud.Pendiente, "h", DateTimeOffset.UnixEpoch, null, null, null, null);
        JsonSerializer.SerializeToElement(solicitud, opciones)
            .GetProperty("resultado").GetString().Should().Be("Pendiente");

        JsonSerializer.Deserialize<EstadoContrato>("\"Activo\"", opciones).Should().Be(EstadoContrato.Activo);
        JsonSerializer.Deserialize<EstadoContrato>(((int)EstadoContrato.Activo).ToString(), opciones)
            .Should().Be(EstadoContrato.Activo, "los clientes antiguos mandan el entero");
    }
}
