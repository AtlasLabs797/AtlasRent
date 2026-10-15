namespace AtlasRent.Api.Data;

/// <summary>
/// Aplicación de un cobro a una factura (F3-06, docs/ESPECIFICACION.md §3.10.1).
/// <para>
/// <b>El importe va en la moneda de la factura</b>, que es la deuda que salda. El importe recibido
/// en otra moneda y el cambio aplicado los añade F3-07 con su migración (§3.10.2, D8); hasta entonces
/// esta tabla solo admite cobros de la misma moneda que la factura.
/// </para>
/// <para>
/// <b>La fila no se borra nunca.</b> Anular un cobro —o anular la factura que pagaba, que es D6 y es
/// F3-08— no borra la asignación: la deja sin vigor, y lo que no cuenta para nada es una asignación
/// de un cobro anulado. Borrarla perdería la trazabilidad de qué cobro pagó qué, que es exactamente
/// lo que §3.10.1 llama historial completo.
/// </para>
/// </summary>
public sealed class CobroAsignacion : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    public Guid CobroId { get; set; }

    public Cobro? Cobro { get; set; }

    /// <summary>Factura a la que se aplica este importe. Obligatoria en esta fase.</summary>
    public Guid FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Importe aplicado a la factura, en la moneda de la factura. Nunca supera lo pendiente de esa
    /// factura, y el servicio lo comprueba contra el pendiente real, no contra el total.
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>
    /// Si la asignación cuenta. Se pone en falso al anular el cobro (F3-06) y al anular la factura
    /// que pagaba (D6, F3-08). Por defecto verdadera: una asignación nueva siempre cuenta.
    /// </summary>
    public bool Vigente { get; set; } = true;

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}