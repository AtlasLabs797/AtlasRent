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

/// <summary>Importe e ITBIS de una línea de factura, ya redondeados.</summary>
/// <param name="Importe">Cantidad por precio, redondeada a dos decimales.</param>
/// <param name="ItbisImporte">
/// ITBIS de la línea, redondeado a dos decimales. Cero si la línea no lleva impuesto.
/// </param>
public sealed record ResultadoImpuestosLinea(decimal Importe, decimal ItbisImporte);

/// <summary>
/// Cálculo de una línea de factura (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Existe aparte de <see cref="CalculoImpuestos.Calcular"/> porque el ITBIS de §3.8 es
/// <b>por línea</b> y no sobre la factura completa: cada línea lleva su propio tipo de impuesto,
/// unas lo llevan y otras no. Calcular sobre el subtotal y repartir después daría un resultado
/// distinto, y en los céntimos se nota.
/// </para>
/// <para>
/// El porcentaje entra como dato (lo lee <c>tipo_itbis</c>, F1-08) y el 100 es un divisor de
/// porcentaje. Aquí no hay ninguna cifra fiscal.
/// </para>
/// <para>
/// Una línea de descuento lleva el importe en negativo, y su ITBIS sale también en negativo: es la
/// lectura coherente de "el descuento resta también la base del impuesto", y es lo que hace que
/// <c>Σ Itbis de línea</c> cuadre con la suma algebraica del subtotal.
/// </para>
/// </summary>
public static class CalculoImpuestosLinea
{
    private const decimal PorCiento = 100m;

    /// <summary>
    /// Importe e ITBIS de una línea, cada uno redondeado una sola vez (D27).
    /// </summary>
    /// <param name="cantidad">Cantidad facturable, con cuatro decimales.</param>
    /// <param name="precioUnitario">Precio unitario en la moneda de la factura.</param>
    /// <param name="porcentajeItbis">Porcentaje congelado de ITBIS; nulo si la línea no lleva.</param>
    public static ResultadoImpuestosLinea Calcular(
        decimal cantidad,
        decimal precioUnitario,
        decimal? porcentajeItbis)
    {
        var importe = Redondeo.Importe(cantidad * precioUnitario);
        var itbis = porcentajeItbis is null
            ? 0m
            : Redondeo.Importe(importe * porcentajeItbis.Value / PorCiento);

        return new ResultadoImpuestosLinea(importe, itbis);
    }
}