using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Comprobante de retención de una factura (F3-04, docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// <b>Uno por factura, no uno por línea de retención.</b> Es lo que dice §3.9 ("seguimiento del
/// comprobante de retención") y es lo que se puede pedirle al cliente: un formulario, no uno por
/// renglón de la factura. Por eso <see cref="FacturaId"/> es único.
/// </para>
/// <para>
/// Nace <see cref="EstadoComprobanteRetencion.Pendiente"/> <b>al emitir la factura, en la misma
/// transacción</b>: si la emisión se deshace, la fila no llega a existir. El motivo es que una
/// retención sin factura emitida es un dato a medias, y el trabajo diario (§8) solo tiene que buscar
/// lo que está pendiente, no lo que se olvidó de crear.
/// </para>
/// <para>
/// <b>Tipo, base, porcentaje e importe van copiados</b> y no se releen de <c>tipo_retencion</c>: si
/// mañana el administrador sube un porcentaje o cambia la base de un tipo, el comprobante que ya se
/// emitía sigue diciendo lo que se retuvo de verdad. Es el mismo criterio que aplicó F3-03 con el
/// tipo del comprobante fiscal.
/// </para>
/// </summary>
public sealed class ComprobanteRetencion : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Factura que retuvo. Es único: una factura retenida, un comprobante.</summary>
    public Guid FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Nombre del tipo de retención aplicado, congelado en el momento de emitir.
    /// <para>
    /// Con más de una línea de retención van los nombres de todos, separados por coma, porque el
    /// comprobante es único para la factura.
    /// </para>
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// Sobre qué importe de la factura se calculó (<c>tipo_retencion.base</c>).
    /// <para>
    /// Con varias líneas de bases distintas se copia la de la primera. §3.9 no dice qué debe
    /// declarar un comprobante con más de una retención de base distinta, así que el caso queda
    /// fuera de lo que se implementa aquí.
    /// </para>
    /// </summary>
    public BaseRetencion Base { get; set; }

    /// <summary>
    /// Porcentaje aplicado, congelado (<c>numeric(18,4)</c>). Con varias líneas es la suma de sus
    /// porcentajes, que es un resumen del comprobante y no un porcentaje sobre una base única.
    /// </summary>
    public decimal Porcentaje { get; set; }

    /// <summary>Importe retenido, en positivo: lo retenido es dinero que se va de la factura.</summary>
    public decimal Importe { get; set; }

    public EstadoComprobanteRetencion Estado { get; set; }

    /// <summary>
    /// Número del comprobante de retención (<c>607</c>). Es lo que dice el cliente al entregarlo y no
    /// hay a quién validárselo: no hay conexión con la DGII en el alcance (D6, D14), así que se
    /// guarda tal cual. Nulo mientras está pendiente.
    /// </summary>
    public string? Numero { get; set; }

    /// <summary>Fecha que dice el comprobante. Nula mientras está pendiente.</summary>
    public DateOnly? FechaRecepcion { get; set; }

    public DateTimeOffset? RecibidoAt { get; set; }

    public Guid? RecibidoPor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}