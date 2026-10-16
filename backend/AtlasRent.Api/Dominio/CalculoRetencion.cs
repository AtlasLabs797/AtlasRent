namespace AtlasRent.Api.Dominio;

/// <summary>
/// Importe de una línea de retención de una factura (F3-04, docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// <b>La base la decide el tipo configurado, no este cálculo</b> (<c>tipo_retencion.base</c>): los
/// mismos datos de factura dan tres importes distintos según sea sobre el subtotal, el ITBIS o el
/// total, y esa elección es de la empresa. Aquí solo se aplica el porcentaje que llega sobre la base
/// que llega.
/// </para>
/// <para>
/// Vive en <c>Dominio</c> y no en el servicio por lo mismo que <see cref="CalculoCobro"/>: lo leen
/// la recalculación de totales de la factura, el alta de la línea y la emisión, y si cada uno
/// multiplicara por su cuenta, un día los tres dirían una retención diferente.
/// </para>
/// <para>
/// El importe sale <b>en negativo</b> porque es el importe de una línea de factura y las líneas de
/// retención van en negativo igual que el descuento; el servicio es quien decide el signo al
/// escribirla.
/// </para>
/// </summary>
public static class CalculoRetencion
{
    /// <summary>
    /// Divisor de un porcentaje, no una cifra fiscal (D14): el porcentaje entra como dato, lo lee
    /// <c>tipo_retencion</c> y de ahí no sale ninguna constante.
    /// </summary>
    private const decimal PorCiento = 100m;

    /// <summary>
    /// Importe retenido por una línea, redondeado una sola vez medio hacia arriba (D27) que es el
    /// redondeo del sistema.
    /// </summary>
    /// <param name="baseCalculo">Sobre qué importe de la factura se aplica, según el tipo configurado.</param>
    /// <param name="subtotal">Subtotal de la factura, sin ITBIS y <b>sin</b> retenciones.</param>
    /// <param name="itbis">ITBIS de la factura, ya redondeado línea a línea.</param>
    /// <param name="total">Subtotal más ITBIS.</param>
    /// <param name="porcentaje">Porcentaje del tipo de retención, congelado en la línea.</param>
    public static decimal Importe(
        BaseRetencion baseCalculo,
        decimal subtotal,
        decimal itbis,
        decimal total,
        decimal porcentaje)
    {
        var baseImporte = baseCalculo switch
        {
            BaseRetencion.Subtotal => subtotal,
            BaseRetencion.Itbis => itbis,
            BaseRetencion.Total => total,
            _ => throw new ArgumentOutOfRangeException(
                nameof(baseCalculo), baseCalculo, "Base de retención desconocida."),
        };

        return Redondeo.Importe(baseImporte * porcentaje / PorCiento);
    }
}