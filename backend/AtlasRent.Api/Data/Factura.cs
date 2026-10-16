using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Factura y proforma (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// <b>Una sola tabla para las dos</b>, y lo que las distingue es el tipo de la serie, no la tabla:
/// <c>Emitida</c> es lo normal en una serie normal o fiscal, y <c>Convertida</c> solo aparece en
/// una proforma que ya se pasó a factura (F3-08). Dos tablas obligarían a duplicar líneas, totales y
/// cobros sin ganar nada.
/// </para>
/// <para>
/// <b>Nada de esto se borra nunca</b> (AGENTS.md §6). En borrador la factura todavía no tiene
/// número, ni comprobante, ni deuda, así que se puede reescribir entera, incluidas sus líneas.
/// Emitida, cualquier cambio pasa por anular y emitir otra (F3-08).
/// </para>
/// <para>
/// <b>Una sola moneda</b> y no se convierte nunca (D7). El tipo de cambio que hay aquí no convierte
/// esta factura: se congela al emitir solo para consolidar informes (D9, §3.12).
/// </para>
/// </summary>
public sealed class Factura : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <summary>Cliente al que se factura. Obligatorio: una factura siempre es para alguien.</summary>
    public Guid ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>Unidad facturada, si la factura viene de una unidad concreta (§3.8).</summary>
    public Guid? UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    /// <summary>
    /// Contrato de origen. Nulo en una factura manual que no viene de un contrato (§3.8 admite
    /// facturar desde la unidad o desde el cliente sin contrato).
    /// </summary>
    public Guid? ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>
    /// Serie de numeración. Es la que fija la moneda cuando la tiene fija (D7) y la que decide si
    /// la factura es normal, fiscal o proforma (§3.7).
    /// </summary>
    public Guid SerieId { get; set; }

    public Serie? Serie { get; set; }

    /// <summary>
    /// Número dentro de la serie. Nulo hasta que se emite: un borrador no consume número, y el
    /// número lo saca el contador transaccional en el momento de la emisión (§3.7, D28).
    /// </summary>
    public int? Numero { get; set; }

    public EstadoFactura Estado { get; set; }

    /// <summary>Moneda única de todo el documento (D7). No se convierte nunca.</summary>
    public Moneda Moneda { get; set; }

    /// <summary>Día de emisión. Lo pone la emisión, no quien la escribe: es el día en que salió.</summary>
    public DateOnly? FechaEmision { get; set; }

    /// <summary>Día de vencimiento, en día de negocio. Lo escribe quien la prepara (§3.13).</summary>
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>Concepto del documento: lo que se factura de un vistazo en la vista de cuentas (§3.13).</summary>
    public string Concepto { get; set; } = string.Empty;

    /// <summary>Suma algebraica de los importes de las líneas, redondeada medio hacia arriba.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Suma de los ITBIS de línea ya redondeados (D27), no el redondeo del subtotal: el impuesto se
    /// calcula y redondea en cada línea, que es donde lo pide §3.8 ("ITBIS opcional por línea").
    /// </summary>
    public decimal Itbis { get; set; }

    /// <summary>Subtotal más ITBIS.</summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Suma de las líneas de retención, en positivo (§3.9, D14, F3-04). <b>No baja el total</b>: es
    /// el total menos esta columna lo que el cliente debe. Se guarda en positivo porque «lo retenido»
    /// es una cantidad que se le resta a lo facturado; el signo negativo vive en la línea.
    /// </summary>
    public decimal Retencion { get; set; }

    /// <summary>
    /// Lo que hay que cobrar de verdad: total menos retención (F3-04). Es la cifra contra la que se
    /// comparan los cobros, y por eso la retención entra en el estado de cobro aunque no sea dinero
    /// cobrado.
    /// </summary>
    public decimal NetoACobrar { get; set; }

    /// <summary>
    /// Tipo de cambio congelado al emitir, <c>numeric(18,6)</c>. Solo para informes consolidados
    /// (D9): no convierte ni un solo importe de esta factura.
    /// <para>
    /// Nulo en borrador y también en una factura emitida sin cambio, que es lo que pasa con el
    /// dólar: el valor del dólar es 1 por definición y no se guarda (F3-01).
    /// </para>
    /// </summary>
    public decimal? TipoCambioValor { get; set; }

    /// <summary>Día de negocio al que corresponde el cambio congelado.</summary>
    public DateOnly? TipoCambioFecha { get; set; }

    /// <summary>
    /// Fila de <c>tipo_cambio</c> (F3-01) de la que salió el valor congelado, para poder saber de
    /// dónde vino. Nulo cuando no hay fila, que es el caso del dólar.
    /// </summary>
    public Guid? TipoCambioId { get; set; }

    public TipoCambio? TipoCambio { get; set; }

    /// <summary>
    /// Referencia de la e-factura externa (D5, §3.7). <b>Texto tecleado a mano por gerente o
    /// administrador</b>: la aplicación usa comprobante fiscal normal y si una factura necesita
    /// e-factura se emite en otra aplicación y aquí se apunta su número. Sin validación, sin
    /// normalizar y sin lógica: lo que se escribe es lo que después imprime el PDF.
    /// </summary>
    public string? ReferenciaEfactura { get; set; }

    /// <summary>
    /// Líneas del documento. Se reemplazan enteras mientras la factura está en borrador; en cuanto
    /// se emite quedan congeladas.
    /// </summary>
    public List<FacturaLinea> Lineas { get; set; } = [];

    // F3-04: las líneas de retención son líneas de más, con Tipo = TipoLineaFactura.Retencion y en
    // negativo, y su suma es la columna Retencion de arriba. No hay tabla de retenciones por factura:
    // la línea ya guarda el concepto, el importe, el porcentaje y el tipo configurado del que salió
    // (§3.9, D14). El neto a cobrar es Total - Retencion y vive en columna porque una factura
    // emitida no vuelve a calcularse salvo que se duplique (F3-08) o se anule (F3-08).

    // F3-06: cobrado, pendiente y estado de cobro derivado (Pendiente / Parcial / Pagada, §3.8) ya
    // salen en el DTO y los calcula Dominio/CalculoCobro a partir de las asignaciones vivas de
    // cobro_asignacion. No son columnas: dependen de los cobros, que cambian aunque la factura no
    // se toque. La retención entra en ese mismo cálculo desde F3-04, aunque no sea dinero cobrado:
    // cierra el pendiente, pero no cuenta como cobrada.

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}
