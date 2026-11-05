using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Dominio;

/// <summary>
/// F1-02: redondeo half-up por línea (D27, AGENTS.md §6).
/// El redondeo por defecto de <c>decimal</c> en .NET es half-even, así que hay que
/// hacerlo explícito o los importes difieren del céntimo que espera la empresa.
/// </summary>
public sealed class RedondeoTests
{
    [Theory]
    [InlineData("0.005", "0.01")]     // half-up: sube
    [InlineData("0.015", "0.02")]
    [InlineData("0.025", "0.03")]
    [InlineData("1.005", "1.01")]
    [InlineData("2.675", "2.68")]     // trampa clásica del double; aquí decimal es exacto
    [InlineData("10.344999", "10.34")]
    [InlineData("10.345001", "10.35")]
    [InlineData("1234.5678", "1234.57")]
    public void RedondeaALaCentimaHaciaArriba(string entrada, string esperado)
    {
        decimal importe = decimal.Parse(entrada, System.Globalization.CultureInfo.InvariantCulture);
        decimal esperadoDecimal = decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture);

        Redondeo.Importe(importe).Should().Be(esperadoDecimal);
    }

    [Theory]
    [InlineData("-0.005", "-0.01")]   // away from zero también en negativo
    [InlineData("-1.005", "-1.01")]
    [InlineData("-1234.5678", "-1234.57")]
    public void RedondeaNegativosAlejandoDelCero(string entrada, string esperado)
    {
        decimal importe = decimal.Parse(entrada, System.Globalization.CultureInfo.InvariantCulture);
        decimal esperadoDecimal = decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture);

        Redondeo.Importe(importe).Should().Be(esperadoDecimal);
    }

    [Fact]
    public void RedondeaTipoDeCambioASeisDecimales()
    {
        // numeric(18,6) en base de datos: el tipo de cambio no se corta a dos decimales.
        Redondeo.TipoCambio(60.3456785m).Should().Be(60.345679m);
        Redondeo.TipoCambio(59.5m).Should().Be(59.500000m);
    }

    [Fact]
    public void ElRedondeoPorDefectoDeDecimalNoSirve()
    {
        // documenting why Redondeo.Importe exists: half-even would give 0.00 and 2.68->2.68 but 1.005->1.00
        Math.Round(0.005m, 2).Should().NotBe(0.01m);
        Math.Round(1.005m, 2).Should().NotBe(1.01m);
    }
}