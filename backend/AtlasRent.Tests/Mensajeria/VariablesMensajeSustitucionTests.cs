using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// BUG-052: la sustitución de variables de una plantilla es de una pasada. Un valor que contiene el
/// nombre de otra variable (un cliente que se llama «{total}») sale tal cual, no se vuelve a sustituir.
/// </summary>
public sealed class VariablesMensajeSustitucionTests
{
    [Fact]
    public void UnValorQueParecePlantillaNoSeSustituyeDeNuevo()
    {
        var variables = new VariablesMensaje(
            Cliente: "{total} {unidad}",
            Numero: "F-1",
            Total: "100.00 USD",
            Pendiente: "40.00 USD",
            Vencimiento: "01/01/2026",
            Unidad: "Torre {cliente}");
        var plantilla = new PlantillaMensaje
        {
            Asunto = "Factura {numero}",
            Cuerpo = "Hola {cliente}, total {total}, unidad {unidad}. {desconocida}",
        };

        var (asunto, cuerpo) = variables.Sustituir(plantilla);

        asunto.Should().Be("Factura F-1");
        cuerpo.Should().Be("Hola {total} {unidad}, total 100.00 USD, unidad Torre {cliente}. {desconocida}");
    }
}
