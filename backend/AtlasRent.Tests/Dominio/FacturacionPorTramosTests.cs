using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Dominio;

/// <summary>
/// BUG-029 y BUG-024: prorrateo sin perder el céntimo del empate, fin vigente exclusivo y renta
/// por tramos cuando un incremento cae a mitad de mes. Los esperados están calculados a mano.
/// </summary>
public sealed class FacturacionPorTramosTests
{
    [Fact]
    public void ElProrrateoMultiplicaAntesDeDividir()
    {
        // 12345.67 x 15 / 30 = 6172.835 exacto -> half-up 6172.84. Dividiendo antes daba 6172.83.
        Prorrateo.Mensual(12345.67m, 30, 15).Should().Be(6172.84m);
    }

    [Fact]
    public void ElFinVigenteEsExclusivoYNoSeFacturaUnDiaDeMas()
    {
        var marzo = PeriodoFacturacion.DelMes(2027, 3);

        // Contrato del 1 de enero al 10 de marzo (exclusivo): marzo factura los días 1 al 9.
        marzo.DiasFacturables(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 10)).Should().Be(9);
        // Fin vigente justo el día 1: no toca el mes.
        marzo.DiasFacturables(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 1)).Should().Be(0);
        // Fin vigente el primer día del mes siguiente: mes entero.
        marzo.DiasFacturables(new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 1)).Should().Be(31);
        // Empieza el 20 y acaba el 1 de abril: días 20..31 = 12.
        marzo.DiasFacturables(new DateOnly(2027, 3, 20), new DateOnly(2027, 4, 1)).Should().Be(12);
    }

    [Fact]
    public void UnIncrementoALaMitadDelMesPartePeriodoEnDosTramos()
    {
        var incrementos = new[]
        {
            new IncrementoPactado(new DateOnly(2027, 3, 10), TipoIncremento.Porcentaje, 10m, null, false, null),
        };

        var tramos = RentaPorTramos.Tramos(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 4, 1), 1000m, incrementos);

        tramos.Should().Equal(
            new TramoDeRenta(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 10), 1000m),
            new TramoDeRenta(new DateOnly(2027, 3, 10), new DateOnly(2027, 4, 1), 1100m));
    }

    [Fact]
    public void UnIncrementoYaAplicadoNoAdelantaLaRentaNuevaAFechasAnterioresASuVigencia()
    {
        // La renta guardada ya es 1100 porque se aplicó en la renovación; antes del día 10 era 1000.
        var incrementos = new[]
        {
            new IncrementoPactado(new DateOnly(2027, 3, 10), TipoIncremento.Porcentaje, 10m, null, true, 1000m),
        };

        var tramos = RentaPorTramos.Tramos(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 4, 1), 1100m, incrementos);

        tramos.Select(t => t.Renta).Should().Equal(1000m, 1100m);
    }

    [Fact]
    public void UnIncrementoElPrimerDiaDelPeriodoNoPartePeriodo()
    {
        var incrementos = new[]
        {
            new IncrementoPactado(new DateOnly(2027, 3, 1), TipoIncremento.ImporteFijo, null, 50m, false, null),
        };

        var tramos = RentaPorTramos.Tramos(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 4, 1), 1000m, incrementos);

        tramos.Should().ContainSingle().Which.Renta.Should().Be(1050m);
    }
}
