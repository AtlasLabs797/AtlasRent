namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado del documento de facturación (docs/ESPECIFICACION.md §3.8).
/// <para>
/// <c>Convertida</c> es solo de proformas: una proforma que se convierte en factura deja de
/// contar por sí misma (F3-08). Una factura normal nunca pasa por ahí.
/// </para>
/// <para>
/// <c>Anulada</c> no borra nada: la factura sigue existiendo con su número, que además no vuelve a
/// liberarse (D28, AGENTS.md §6).
/// </para>
/// </summary>
public enum EstadoFactura
{
    /// <summary>Se puede reescribir entera, incluidas sus líneas. No tiene número ni efecto económico.</summary>
    Borrador = 1,

    /// <summary>Tiene número y el cambio del día congelados. A partir de aquí ya no se toca (F3-08).</summary>
    Emitida = 2,

    /// <summary>Cancelada con motivo, conservando su número y su historial (F3-08).</summary>
    Anulada = 3,

    /// <summary>Proforma convertida en factura: la fuente deja de contar por sí misma (F3-08).</summary>
    Convertida = 4,
}
