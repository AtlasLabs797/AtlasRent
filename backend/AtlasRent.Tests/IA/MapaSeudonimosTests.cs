using System.Text.Json;
using System.Text.Json.Nodes;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.IA;

/// <summary>
/// SEC-026: la seudonimización no depende solo de una lista de nombres de propiedad y no lanza si
/// un dato personal llega con otro tipo. Se prueba sobre una forma de JSON por alcance.
/// </summary>
public sealed class MapaSeudonimosTests
{
    private const string Nombre = "José Peña";
    private const string Correo = "jose.pena@example.test";
    private const string Telefono = "809-555-0142";
    private const string Documento = "001-1234567-8";

    public static TheoryData<string, string> FormasPorAlcance => new()
    {
        {
            "clientes",
            $$"""{"data":[{"id":"1","nombre":"{{Nombre}}","email":"{{Correo}}","telefono":"{{Telefono}}","activo":true}]}"""
        },
        {
            "cuentas",
            $$"""{"filas":[{"cliente":"{{Nombre}}","concepto":"Renta de {{Nombre}}","total":"100.00"}],"totales":[]}"""
        },
        {
            "facturas",
            $$"""{"data":[{"numero":"A-1","clienteNombre":"{{Nombre}}","concepto":"Renta","total":"100.00"}]}"""
        },
        {
            "contratos",
            $$"""{"data":[{"id":"1","cliente":"{{Nombre}}","documento":"{{Documento}}","notas":"Llamar al {{Telefono}}"}]}"""
        },
        {
            "informes",
            $$"""{"clientes":[{"razonSocial":"{{Nombre}}","emailFacturacion":"{{Correo}}","telefonoMovil":"{{Telefono}}"}]}"""
        },
    };

    [Theory]
    [MemberData(nameof(FormasPorAlcance))]
    public void NingunDatoPersonalSaleEnElJsonDesescapado(string alcance, string json)
    {
        _ = alcance;

        var salida = new MapaSeudonimos().Anonimizar(JsonNode.Parse(json));

        // Se busca sobre los textos des-escapados, no solo sobre el JSON impreso.
        var textos = string.Join("\n", Textos(JsonDocument.Parse(salida).RootElement));
        foreach (var valor in new[] { Nombre, Correo, Telefono, Documento })
        {
            textos.Should().NotContain(valor);
            salida.Should().NotContain(valor);
        }

        textos.Should().Contain("[[");
    }

    [Fact]
    public void UnTelefonoQueLlegaComoNumeroSeSeudonimizaSinLanzar()
    {
        var mapa = new MapaSeudonimos();

        var salida = mapa.Anonimizar(JsonNode.Parse("""{"telefono":8095550142,"nombre":"Ana Gómez"}"""));

        salida.Should().NotContain("8095550142");
        mapa.Reidentificar(salida).Should().Contain("8095550142");
    }

    [Fact]
    public void UnaPropiedadPersonalNoListadaSeSeudonimizaPorTipo()
    {
        var salida = new MapaSeudonimos().Anonimizar(JsonNode.Parse(
            $$"""{"emailFacturacion":"{{Correo}}","telefonoMovil":"{{Telefono}}","direccionFiscal":"Calle 1 #2"}"""));

        salida.Should().NotContain(Correo).And.NotContain(Telefono).And.NotContain("Calle 1");
    }

    [Fact]
    public void UnValorConocidoEnOtroCampoNoLlegaAlProveedor()
    {
        var mapa = new MapaSeudonimos();

        var accion = () => mapa.Anonimizar(JsonNode.Parse(
            $$"""{"nombre":"{{Nombre}}","resumen":"Cobrar a {{Nombre}} esta semana"}"""));

        accion.Should().Throw<ProveedorIaException>().Which.Message.Should().NotContain(Nombre);
    }

    [Fact]
    public void UnCorreoEnUnCampoNoListadoNoLlegaAlProveedor()
    {
        var accion = () => new MapaSeudonimos().Anonimizar(JsonNode.Parse(
            $$"""{"observaciones":"escribir a {{Correo}}"}"""));

        accion.Should().Throw<ProveedorIaException>();
    }

    private static IEnumerable<string> Textos(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Textos(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Textos),
        _ => [],
    };
}
