namespace AtlasRent.Api.Dominio;

/// <summary>
/// Cómo se sube la renta de un contrato al llegar su fecha (docs/ESPECIFICACION.md §3.5: "los
/// incrementos de renta, en porcentaje o importe fijo, con fecha, se aplican solos con aviso
/// interno").
/// <para>
/// La especificación dice "% o importe fijo" y son dos cosas distintas, no dos formas de escribir la
/// misma: el porcentaje se multiplica por la renta vigente en ese momento y el importe fijo se suma
/// tal cual, en la moneda del contrato y sin convertir. Por eso el tipo es explícito en la fila y
/// el valor va en la columna que le corresponde.
/// </para>
/// <para>
/// Los valores numéricos son los que guarda la columna, como el resto de enums del dominio, y no se
/// reordenan.
/// </para>
/// </summary>
public enum TipoIncremento
{
    /// <summary>Porcentaje sobre la renta vigente: <c>renta × (1 + p/100)</c>.</summary>
    Porcentaje = 1,

    /// <summary>
    /// Importe absoluto que se suma a la renta, en la moneda del contrato. Si el contrato está en
    /// dólares, el incremento está en dólares: no se convierte a la moneda de la factura.
    /// </summary>
    ImporteFijo = 2,
}