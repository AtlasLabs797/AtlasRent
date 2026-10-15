using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Dominio;

/// <summary>
/// F1-02: fechas de negocio (<see cref="DateOnly"/>) y zona horaria America/Santo_Domingo.
/// En Santo Domingo no hay horario de verano: UTC−4 todo el año. Una factura vencida
/// se decide con la fecha local, nunca con la del servidor.
/// </summary>
public sealed class TiempoNegocioTests
{
    [Fact]
    public void LaZonaPorDefectoEsSantoDomingo()
    {
        TiempoNegocio.ZonaPorDefecto.Should().Be("America/Santo_Domingo");
    }

    [Fact]
    public void LaZonaSeResuelveEnEstaMaquina()
    {
        var zona = TiempoNegocio.BuscarZona(TiempoNegocio.ZonaPorDefecto);

        zona.Should().NotBeNull();
    }

    [Theory]
    // 03:00 UTC del 15/01 son las 23:00 del 14/01 en Santo Domingo: la fecha todavía es la anterior.
    [InlineData("2026-01-15T03:00:00+00:00", 2026, 1, 14)]
    // 05:00 UTC del 15/01 son las 01:00 del 15/01: ya ha cambiado de día.
    [InlineData("2026-01-15T05:00:00+00:00", 2026, 1, 15)]
    // Medianoche localexacta.
    [InlineData("2026-01-15T04:00:00+00:00", 2026, 1, 15)]
    // Fin de año cruzando el cambio de día.
    [InlineData("2026-12-31T23:30:00+00:00", 2026, 12, 31)]
    [InlineData("2027-01-01T03:00:00+00:00", 2026, 12, 31)]
    public void ConvierteUnInstanteUtcAFechaDeNegocio(string instanteUtc, int anio, int mes, int dia)
    {
        var instante = DateTimeOffset.Parse(instanteUtc, System.Globalization.CultureInfo.InvariantCulture);

        TiempoNegocio.FechaEnZonaNegocio(instante).Should().Be(new DateOnly(anio, mes, dia));
    }

    [Theory]
    // Vencida: la fecha de vencimiento ya pasó.
    [InlineData("2026-01-14", "2026-01-15", true)]
    // Vence hoy: no está vencida.
    [InlineData("2026-01-15", "2026-01-15", false)]
    // Vence mañana: no está vencida.
    [InlineData("2026-01-16", "2026-01-15", false)]
    public void UnaFacturaEstaVencidaSoloSiHoySuperaElVencimiento(string vencimiento, string hoy, bool esperado)
    {
        var fechaVencimiento = DateOnly.Parse(vencimiento, System.Globalization.CultureInfo.InvariantCulture);
        var fechaHoy = DateOnly.Parse(hoy, System.Globalization.CultureInfo.InvariantCulture);

        TiempoNegocio.EstaVencida(fechaVencimiento, fechaHoy).Should().Be(esperado);
    }

    [Fact]
    public void DiasDeVencidaNoPuedeSerNegativo()
    {
        var hoy = new DateOnly(2026, 1, 15);

        TiempoNegocio.DiasDeVencida(new DateOnly(2026, 1, 10), hoy).Should().Be(5);
        TiempoNegocio.DiasDeVencida(new DateOnly(2026, 1, 20), hoy).Should().Be(0);
    }
}