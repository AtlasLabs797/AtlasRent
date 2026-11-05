using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Dominio;

/// <summary>
/// F1-02, aceptación: meses de 28, 29, 30 y 31 días.
/// Fórmula de DOC_TECNICA §5: <c>renta / díasDelMes × díasFacturables</c>, días inclusivos,
/// redondeo half-up a 2 decimales.
/// </summary>
public sealed class ProrrateoTests
{
    [Theory]
    // Mes de 28 días (febrero no bisiesto), mes completo.
    [InlineData(28)]
    // Mes de 29 días (febrero bisiesto), mes completo.
    [InlineData(29)]
    // Mes de 30 días.
    [InlineData(30)]
    // Mes de 31 días.
    [InlineData(31)]
    public void MesCompletoDevuelveLaRentaEntera(int diasDelMes)
    {
        Prorrateo.Mensual(31000m, diasDelMes, diasDelMes).Should().Be(31000.00m);
    }

    [Theory]
    // 31000 / 29 * 15 = 16034.4827... -> 16034.48
    [InlineData(29, 15, 16034.48)]
    // 31000 / 30 * 16 = 16533.3333... -> 16533.33
    [InlineData(30, 16, 16533.33)]
    // 31000 / 31 * 16 = 16000.0000 -> 16000.00
    [InlineData(31, 16, 16000.00)]
    // 31000 / 28 * 14 = 15500.0000 -> 15500.00
    [InlineData(28, 14, 15500.00)]
    // Caso que exige el redondeo: 31000 / 28 * 20 = 22142.8571... -> 22142.86
    [InlineData(28, 20, 22142.86)]
    public void ProrrateaProporcionalmenteAlMes(int diasDelMes, int diasFacturables, decimal esperado)
    {
        Prorrateo.Mensual(31000m, diasDelMes, diasFacturables).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 31)]
    [InlineData(32, 31)]
    [InlineData(-1, 31)]
    public void RechazaDiasFacturablesFueraDeRango(int diasFacturables, int diasDelMes)
    {
        FluentActions.Invoking(() => Prorrateo.Mensual(31000m, diasDelMes, diasFacturables))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-31)]
    public void RechazaMesesSinDias(int diasDelMes)
    {
        FluentActions.Invoking(() => Prorrateo.Mensual(31000m, diasDelMes, 1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 1, 31)]
    public void DiasDelMesCoincidenConElCalendario(int anio, int mes, int esperado)
    {
        Prorrateo.DiasDelMes(anio, mes).Should().Be(esperado);
    }
}