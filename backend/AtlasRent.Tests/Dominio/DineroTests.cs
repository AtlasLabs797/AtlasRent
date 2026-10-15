using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Dominio;

/// <summary>
/// F1-02: <see cref="Dinero"/> con decimal y moneda.
/// Regla dura (D7, AGENTS.md §6): un documento tiene una sola moneda y nunca se convierte.
/// Por eso sumar importes de monedas distintas es un error de código, no un resultado.
/// </summary>
public sealed class DineroTests
{
    [Fact]
    public void ConservaImporteYMoneda()
    {
        var importe = new Dinero(1250.50m, Moneda.USD);

        importe.Importe.Should().Be(1250.50m);
        importe.Moneda.Should().Be(Moneda.USD);
    }

    [Fact]
    public void SumaImportesDeLaMismaMoneda()
    {
        var total = new Dinero(1000m, Moneda.USD).Sumar(new Dinero(250.55m, Moneda.USD));

        total.Importe.Should().Be(1250.55m);
        total.Moneda.Should().Be(Moneda.USD);
    }

    [Fact]
    public void NoSumaMonedasDistintas()
    {
        // Prohibido por D7: no existe conversión implícita.
        FluentActions.Invoking(() => new Dinero(1000m, Moneda.USD).Sumar(new Dinero(250m, Moneda.DOP)))
            .Should().Throw<MonedaDistintaException>();
    }

    [Fact]
    public void NoPermiteCompararMonedasDistintas()
    {
        var usd = new Dinero(1000m, Moneda.USD);
        var dop = new Dinero(1000m, Moneda.DOP);

        FluentActions.Invoking(() => usd.MenorQue(dop)).Should().Throw<MonedaDistintaException>();
        FluentActions.Invoking(() => usd.MayorQue(dop)).Should().Throw<MonedaDistintaException>();
    }

    [Fact]
    public void ComparaImportesDeLaMismaMoneda()
    {
        new Dinero(1000m, Moneda.USD).MayorQue(new Dinero(999.99m, Moneda.USD)).Should().BeTrue();
        new Dinero(1000m, Moneda.USD).MenorQue(new Dinero(1000.01m, Moneda.USD)).Should().BeTrue();
        new Dinero(1000m, Moneda.USD).IgualA(new Dinero(1000.00m, Moneda.USD)).Should().BeTrue();
    }

    [Fact]
    public void LaRestaNuncaSeQuedaNegativa()
    {
        new Dinero(1000m, Moneda.USD).Restar(new Dinero(2500m, Moneda.USD)).Importe.Should().Be(0m);
    }

    [Fact]
    public void ElCeroHeredaLaMoneda()
    {
        Dinero.Cero(Moneda.DOP).Importe.Should().Be(0m);
        Dinero.Cero(Moneda.DOP).Moneda.Should().Be(Moneda.DOP);
    }

    [Fact]
    public void RedondeaAlConstruirseParaImpedirCentimasImplicitas()
    {
        var importe = new Dinero(10.005m, Moneda.USD);

        importe.Importe.Should().Be(10.01m);
    }

    [Fact]
    public void LosImportesRedondeadosSonIguales()
    {
        // Sin esto, 10.00 + 0.005 daría 10.005 y no 10.01: el céntimo se pierde al acumular.
        var a = new Dinero(10.00m, Moneda.USD);
        var b = new Dinero(0.005m, Moneda.USD);

        a.Sumar(b).Importe.Should().Be(10.01m);
    }
}