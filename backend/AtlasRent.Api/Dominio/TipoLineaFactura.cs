namespace AtlasRent.Api.Dominio;

/// <summary>
/// Tipo de línea de factura (docs/ESPECIFICACION.md §3.8): renta, mora, ajuste, servicio, suministro,
/// cargo y descuento.
/// <para>
/// Los valores son enteros persistidos y <b>no se renumeran nunca</b>: una factura emitida de hace
/// cinco años sigue guardando su 1 = renta, y cambiar el orden de esta lista cambiaría el
/// significado de documentos ya emitidos.
/// </para>
/// </summary>
public enum TipoLineaFactura
{
    Renta = 1,

    /// <summary>Mora por retraso de pago (D13): la introduce una persona, no la calcula el sistema.</summary>
    Mora = 2,

    Ajuste = 3,

    Servicio = 4,

    Suministro = 5,

    Cargo = 6,

    /// <summary>
    /// Línea de importe <b>negativo</b>: el subtotal de la factura es la suma algebraica de sus
    /// líneas, así que un descuento resta sin ser un signo aparte.
    /// </summary>
    Descuento = 7,

    /// <summary>
    /// Línea de retención de §3.9 (D14), también en negativo y <b>fuera</b> del subtotal: el total
    /// de la factura no baja por retener, lo que baja es el neto a cobrar. Por eso esta línea no es
    /// un descuento más, aunque se le parezca en el signo.
    /// </summary>
    Retencion = 8,
}
