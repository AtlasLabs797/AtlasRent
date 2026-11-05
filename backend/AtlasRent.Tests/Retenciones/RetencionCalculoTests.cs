using System.Globalization;
using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// F3-04: el cálculo de la retención sobre <c>Dominio/CalculoRetencion.cs</c> (docs/ESPECIFICACION.md
/// §3.9, D14).
/// <para>
/// Es una prueba pura, sin base de datos, porque el cálculo es de dominio: el mismo argumento que
/// escribió <see cref="CalculoCobro"/> en F3-06. Aquí no hay ni una factura, y aun así se comprueba lo
/// que importa: que la base la decide el tipo configurado y que la retención cierra la factura sin
/// ser dinero cobrado.
/// </para>
/// <para>
/// <b>Los porcentajes son de la prueba, no del sistema</b>: el cálculo no tiene ninguno propio y no
/// puede tenerlos, porque D14 los deja en <c>tipo_retencion</c>. Los de aquí son números inventados
/// para poder comprobar el redondeo.
/// </para>
/// </summary>
public sealed class RetencionCalculoTests
{
    /// <summary>Datos de una factura con las tres bases distintas, para que se note cuál se usó.</summary>
    private const decimal Subtotal = 1_000m;

    private const decimal Itbis = 180m;

    private const decimal Total = Subtotal + Itbis;

    /// <summary>
    /// La base la dice el tipo, no el cálculo: los mismos totales dan tres importes distintos. Es el
    /// criterio de aceptación de F3-04 sobre el dominio.
    /// <para>
    /// El esperado va como texto porque un atributo no admite decimales y el importe tiene que ser
    /// <c>decimal</c>: sin <c>double</c> ni un redondeo de más (D27).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(BaseRetencion.Subtotal, "165")]
    [InlineData(BaseRetencion.Itbis, "29.7")]
    [InlineData(BaseRetencion.Total, "194.7")]
    public void ElImporteSaleDeLaBaseQueDiceElTipo(BaseRetencion baseCalculo, string esperado)
    {
        CalculoRetencion.Importe(baseCalculo, Subtotal, Itbis, Total, porcentaje: 16.5m)
            .Should().Be(
                decimal.Parse(esperado, CultureInfo.InvariantCulture),
                $"con base {baseCalculo} se aplica sobre ese importe y sobre ninguno más");
    }

    /// <summary>
    /// Redondeo medio hacia arriba, el del sistema (D27), y una sola vez: el importe retenido es un
    /// importe de dinero, no un porcentaje que se vuelve a redondear al printable.
    /// </summary>
    [Fact]
    public void ElImporteSeRedondeaMedioHaciaArribaYUnaSolaVez()
    {
        // 0,50 por el 5% son 0,025 exactos: el tercer decimal es un cinco y sube a 0,03. El
        // redondeo de .NET sobre decimal es half-even y dejaría 0,02, que es justo el error que D27
        // vino a evitar.
        CalculoRetencion.Importe(BaseRetencion.Subtotal, 0.50m, 0m, 0.50m, porcentaje: 5m)
            .Should().Be(0.03m, "medio hacia arriba, no al par más cercano");

        // Por debajo del medio no sube: el redondeo no tiene que levantar de más.
        CalculoRetencion.Importe(BaseRetencion.Subtotal, 0.49m, 0m, 0.49m, porcentaje: 5m)
            .Should().Be(0.02m, "0,0245 se queda en 0,02");

        // Y un importe normal no se toca: un solo redondeo, el del resultado.
        CalculoRetencion.Importe(BaseRetencion.Subtotal, 1_000m, 0m, 1_000m, porcentaje: 16.5m)
            .Should().Be(165m);
    }

    /// <summary>
    /// El criterio de aceptación literal del plan: <b>cobro + retención = total ⇒ Pagada</b>. Con la
    /// retención cubriendo sola y sin un solo cobro, que es el caso que no cabía antes de que existiera
    /// la columna.
    /// </summary>
    [Fact]
    public void UnaFacturaCubiertaSoloConRetencionQuedaPagadaSinCobrarNada()
    {
        var retencion = CalculoRetencion.Importe(BaseRetencion.Total, Subtotal, Itbis, Total, 100m);

        var situacion = CalculoCobro.Calcular(Total, retencion, cobrado: 0m, ajustado: 0m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(
            EstadoCobroFactura.Pagada,
            "lo retenido descuenta de lo que hay que ingresar, y no queda nada por cobrar");
        situacion.Pendiente.Should().Be(0m);
    }

    /// <summary>
    /// Y con un cobro parcial más la retención, que es el otro camino del mismo criterio: el cobro
    /// cubre lo que falta después de retener.
    /// </summary>
    [Fact]
    public void UnCobroParcialMasLaRetencionTambienDejaLaFacturaPagada()
    {
        var retencion = CalculoRetencion.Importe(BaseRetencion.Total, Subtotal, Itbis, Total, 10m);
        var neto = Redondeo.Importe(Total - retencion);

        var situacion = CalculoCobro.Calcular(
            Total, retencion, cobrado: neto, ajustado: 0m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pagada);
        situacion.Pendiente.Should().Be(0m);
    }

    /// <summary>
    /// <b>La retención no es dinero cobrado</b>, y esto es lo que impide que un informe de caja se
    /// infle: cerrar la factura con una retención no significa que el cliente haya pagado nada.
    /// </summary>
    [Fact]
    public void UnaFacturaPagadaSoloConRetencionNoTieneNadaCobrado()
    {
        var retencion = CalculoRetencion.Importe(BaseRetencion.Total, Subtotal, Itbis, Total, 100m);

        var situacion = CalculoCobro.Calcular(Total, retencion, cobrado: 0m, ajustado: 0m, Hoy, Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pagada);
        situacion.Cobrado.Should().Be(
            0m,
            "lo cobrado es dinero que entró en caja, y retener es dinero que no entra");
        situacion.Vencida.Should().BeFalse("sin pendiente no hay nada vencido, aunque venció el día");
    }

    /// <summary>
    /// Y lo contrario: con la misma factura, si lo cobrado no llega a cubrir lo que queda después de
    /// retener, la factura está <b>parcial</b>, no pagada. Es el otro lado del mismo dato y evita que
    /// «retención» se lea como «cobro» por error.
    /// </summary>
    [Fact]
    public void UnaRetencionQueNoCubreElTotalDejaLaFacturaParcialYNoPagada()
    {
        var retencion = CalculoRetencion.Importe(BaseRetencion.Subtotal, Subtotal, Itbis, Total, 5m);

        var situacion = CalculoCobro.Calcular(
            Total, retencion, cobrado: 500m, ajustado: 0m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Parcial);
        situacion.Pendiente.Should().BeGreaterThan(0m);
    }

    /// <summary>Un tipo sin base riconocida es un dato corrupto, no un cero silencioso.</summary>
    [Fact]
    public void UnaBaseDesconocidaSeRechazaYNoDevuelveCero()
    {
        var actua = () => CalculoRetencion.Importe((BaseRetencion)99, Subtotal, Itbis, Total, 10m);

        actua.Should().Throw<ArgumentOutOfRangeException>(
            "un importe de cero en una línea de retención dejaría una factura que dice retener y no retiene");
    }

    private static DateOnly Hoy => new(2026, 10, 16);
}