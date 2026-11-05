namespace AtlasRent.Api.Data;

/// <summary>
/// Estado del comprobante de retención de una factura (F3-04, docs/ESPECIFICACION.md §3.9).
/// <para>
/// A diferencia de <see cref="EstadoComprobanteFiscal"/> no tiene estado anulado: §3.9 solo habla de
/// pendiente y recibido, y lo que se anula es la factura (D6), no su comprobante. Cuando la factura
/// se anule, F3-08 decide qué pasa con esta fila.
/// </para>
/// </summary>
public enum EstadoComprobanteRetencion
{
    /// <summary>Se emitió la factura y el comprobante todavía no ha llegado. Es el estado que avisa.</summary>
    Pendiente = 1,

    /// <summary>Llegó el comprobante: hay número y fecha de recepción.</summary>
    Recibido = 2,
}