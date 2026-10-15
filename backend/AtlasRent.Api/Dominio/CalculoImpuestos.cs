namespace AtlasRent.Api.Dominio;

/// <summary>Resultado del cálculo de una factura con ITBIS y retenciones.</summary>
/// <param name="Subtotal">Base imponible antes de ITBIS.</param>
/// <param name="Itbis">ITBIS calculado con el porcentaje configurado.</param>
/// <param name="Retencion">Suma de las retenciones aplicadas.</param>
/// <param name="Total">Subtotal más ITBIS.</param>
/// <param name="NetoACobrar">
/// Total menos retenciones (docs/ESPECIFICACION.md §3.9). La retención no es dinero cobrado: la
/// factura está pagada cuando cobros más retenciones llegan al total.
/// </param>
public sealed record ResultadoImpuestos(
    decimal Subtotal,
    decimal Itbis,
    decimal Retencion,
    decimal Total,
    decimal NetoACobrar);

/// <summary>
/// Cálculo de ITBIS y retenciones de una factura (docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// El 100 es un divisor de porcentaje, no una cifra fiscal: los porcentajes entran como datos, los
/// lee <c>ServicioConfiguracion.CalcularAsync</c> de la tabla y nunca de aquí.
/// </para>
/// </summary>
public static class CalculoImpuestos
{
    private const decimal PorCiento = 100m;

    /// <summary>
    /// Calcula ITBIS y retenciones de un subtotal. Las retenciones se suman en el orden en que
    /// llegan, cada una redondeada a su línea (AGENTS.md §6).
    /// </summary>
    /// <param name="porcentajeItbis">Porcentaje de ITBIS del tipo configurado.</param>
    /// <param name="retenciones">Porcentaje y base de cada tipo de retención aplicado.</param>
    public static ResultadoImpuestos Calcular(
        decimal subtotal,
        decimal porcentajeItbis,
        IReadOnlyList<(decimal Porcentaje, BaseRetencion Base)> retenciones)
    {
        var itbis = Redondeo.Importe(subtotal * porcentajeItbis / PorCiento);
        var total = Redondeo.Importe(subtotal + itbis);

        decimal retencion = 0m;
        foreach (var (porcentaje, baseCalculo) in retenciones)
        {
            var baseImporte = baseCalculo switch
            {
                BaseRetencion.Subtotal => subtotal,
                BaseRetencion.Itbis => itbis,
                BaseRetencion.Total => total,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(retenciones), baseCalculo, "Base de retención desconocida."),
            };

            retencion += Redondeo.Importe(baseImporte * porcentaje / PorCiento);
        }

        return new ResultadoImpuestos(
            subtotal,
            itbis,
            Redondeo.Importe(retencion),
            total,
            Redondeo.Importe(total - retencion));
    }
}