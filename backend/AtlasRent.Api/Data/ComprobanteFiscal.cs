namespace AtlasRent.Api.Data;

/// <summary>
/// Comprobante fiscal emitido (F3-03, docs/ESPECIFICACION.md §3.7 y §3.8).
/// <para>
/// Uno por factura emitida en una serie que genera comprobante. Nace en la misma transacción que la
/// factura y que su número de serie: si el comprobante falla, la factura no sale.
/// </para>
/// <para>
/// <b>Nunca se borra</b> (AGENTS.md §6, D28): anular un comprobante lo marca como anulado y el
/// número no vuelve a salir. Las columnas de la anulación las rellena F3-08, que es quien la ejecuta.
/// </para>
/// <para>
/// La unicidad <c>(serie_id, numero)</c> es la red de seguridad del contador, igual que en
/// <see cref="NumeroSerie"/>: si dos emisiones simultáneas salieran con el mismo número de
/// comprobante, la segunda no llegaría a commitear.
/// </para>
/// </summary>
public sealed class ComprobanteFiscal : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Factura a la que pertenece este comprobante. Una por factura.</summary>
    public Guid FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Serie que autoriza el comprobante. Se copia porque es lo que, junto con el número, identifica
    /// el comprobante ante quien lo recibe, y esa identidad no puede depender de cómo esté la serie
    /// después.
    /// </summary>
    public Guid SerieId { get; set; }

    public Serie? Serie { get; set; }

    /// <summary>Número de comprobante dentro del rango autorizado.</summary>
    public int Numero { get; set; }

    /// <summary>
    /// Tipo de comprobante, copiado del numerador en el momento de asignarlo: si mañana cambian los
    /// rangos o el tipo, este comprobante tiene que seguir diciendo lo que era cuando salió.
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    public EstadoComprobanteFiscal Estado { get; set; }

    public DateTimeOffset RegistradoAt { get; set; }

    public Guid RegistradoPor { get; set; }

    // La anulación es de F3-08. Las columnas están desde ahora porque forman parte de la identidad
    // del comprobante y porque un borrado posterior de la tabla dejaría un anulado sin rastro de por
    // qué; todavía no hay nada que las escriba (nada las rellena antes de que exista la anulación).

    /// <summary>Momento de la anulación. Lo escribe F3-08.</summary>
    public DateTimeOffset? AnuladoAt { get; set; }

    /// <summary>Quién anuló el comprobante. Lo escribe F3-08.</summary>
    public Guid? AnuladoPor { get; set; }

    /// <summary>Motivo de la anulación. Obligatorio en F3-08, igual que en la factura.</summary>
    public string? MotivoAnulacion { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}