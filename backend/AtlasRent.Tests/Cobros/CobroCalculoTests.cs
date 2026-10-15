using AtlasRent.Api.Dominio;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// El cálculo de saldo y estado de cobro de <c>Dominio/CalculoCobro.cs</c> (F3-06,
/// docs/ESPECIFICACION.md §3.8 y §3.9).
/// <para>
/// Es una prueba pura, sin base de datos, porque el cálculo es de dominio y porque es lo que van a
/// heredar F3-04 (retenciones) y F3-07 (cobros en otra moneda) sin volver a escribirlo.
/// </para>
/// <para>
/// <b>Lo que hereda F3-04 es el caso de la retención</b>: como la columna <c>Factura.Retencion</c>
/// todavía no existe, esta es la única forma de comprobar que «cobros más retenciones iguales al
/// total» deja la factura pagada, y se comprueba pasando la retención como parámetro.
/// </para>
/// </summary>
public sealed class CobroCalculoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 16);

    [Fact]
    public void UnaFacturaSinCobrarEstaPendienteYNoVencida()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 0m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pendiente);
        situacion.Pendiente.Should().Be(1_000m);
        situacion.Cobrado.Should().Be(0m);
        situacion.Vencida.Should().BeFalse();
        situacion.DiasVencida.Should().Be(0);
    }

    [Fact]
    public void UnaFacturaACobrarEstaParcial()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 400m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Parcial);
        situacion.Pendiente.Should().Be(600m);
        situacion.Cobrado.Should().Be(400m);
        situacion.Vencida.Should().BeFalse();
    }

    [Fact]
    public void UnaFacturaTotalmenteCobradaEstaPagada()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 1_000m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pagada);
        situacion.Pendiente.Should().Be(0m);
        situacion.Vencida.Should().BeFalse("una factura pagada no está vencida aunque su día ya haya pasado");
        situacion.DiasVencida.Should().Be(0);
    }

    /// <summary>
    /// Vencida es una bandera, no un estado: la factura sigue siendo pendiente, que es lo que la
    /// vista de cuentas de §3.13 necesita para poder ordenarlas. Y los días se cuentan desde el
    /// vencimiento en días de negocio, no en horas ni en UTC.
    /// </summary>
    [Fact]
    public void UnaFacturaSinCobrarYPasadaDeVencimientoEstaVencidaConSusDias()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 0m, Hoy.AddDays(-12), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pendiente, "vencida no sustituye al estado");
        situacion.Vencida.Should().BeTrue();
        situacion.DiasVencida.Should().Be(12);
    }

    [Fact]
    public void UnaFacturaParcialYNocobradaPorCompletoTambienPuedeEstarVencida()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 400m, Hoy.AddDays(-3), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Parcial);
        situacion.Vencida.Should().BeTrue("lo que está pendiente y pasó su día está vencido");
        situacion.DiasVencida.Should().Be(3);
    }

    /// <summary>
    /// El día del vencimiento no está vencido: vence al final de ese día, que es lo que fija
    /// <c>TiempoNegocio.EstaVencida</c>.
    /// </summary>
    [Fact]
    public void ElDiaDelVencimientoNoEstaVencida()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 0m, Hoy, Hoy);

        situacion.Vencida.Should().BeFalse();
        situacion.DiasVencida.Should().Be(0);
    }

    /// <summary>
    /// Cobro más retención igual al total deja la factura pagada (§3.9, D14). Es el caso que hereda
    /// F3-04 en cuanto exista la columna de retención, y por eso se prueba ahora con el parámetro.
    /// </summary>
    [Fact]
    public void CobroMasRetencionIgualesAlTotalDejaLaFacturaPagada()
    {
        var situacion = CalculoCobro.Calcular(total: 1_180m, retencion: 180m, cobrado: 1_000m, Hoy.AddDays(30), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pagada, "la retención descuenta de lo que hay que ingresar");
        situacion.Pendiente.Should().Be(0m);
    }

    [Fact]
    public void LaRetencionTambienHaceQueUnaFacturaVencidaDejaDeEstarlo()
    {
        var situacion = CalculoCobro.Calcular(1_180m, 180m, 0m, Hoy.AddDays(-5), Hoy);

        situacion.Estado.Should().Be(EstadoCobroFactura.Pendiente);
        situacion.Pendiente.Should().Be(1_000m);
        situacion.Vencida.Should().BeTrue("la retención no vence la factura: solo reduce lo pendiente");
    }

    /// <summary>
    /// Cobrar de más no da un pendiente negativo: lo que sobra es saldo a favor del contrato
    /// (§3.10.1), que vive en otra tabla con otras reglas. Un pendiente negativo haría que cualquier
    /// suma de pendientes de §3.13 descuadrara contra el dinero real.
    /// </summary>
    [Fact]
    public void CobrarDeMasDejaPendienteCeroYNoNegativo()
    {
        var situacion = CalculoCobro.Calcular(1_000m, 0m, 1_250m, Hoy.AddDays(30), Hoy);

        situacion.Pendiente.Should().Be(0m);
        situacion.Estado.Should().Be(EstadoCobroFactura.Pagada);
        situacion.Cobrado.Should().Be(1_250m, "lo cobrado sigue siendo lo que entró, no lo que se debía");
    }

    /// <summary>
    /// El centimo que aparece al restar la retención se redondea una sola vez y medio hacia arriba
    /// (D27), que es el redondeo del sistema.
    /// </summary>
    [Fact]
    public void ElRedondeoDelNetoEsElDelSistemaYSeAplicaUnaSolaVez()
    {
        var situacion = CalculoCobro.Calcular(total: 100.005m, retencion: 0m, cobrado: 0m, Hoy, Hoy);

        situacion.Pendiente.Should().Be(100.01m);
    }
}