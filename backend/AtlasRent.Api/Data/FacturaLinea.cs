using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Línea de factura o proforma (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// El importe y el ITBIS los calcula la aplicación, no quien escribe la línea, y se redondean aquí
/// una vez (D27). Una línea de tipo <see cref="TipoLineaFactura.Descuento"/> guarda el importe en
/// negativo: el subtotal de la factura es la suma algebraica de sus líneas.
/// </para>
/// <para>
/// <b>El porcentaje de ITBIS queda congelado en la línea.</b> La factura dice "este importe lleva
/// este impuesto": si mañana el tipo de ITBIS cambia, una factura ya emitida tiene que seguir
/// diciendo lo que decía, que es justamente lo que no haría si guardara solo la clave foránea.
/// </para>
/// <para>
/// F3-04 (§3.9) añade una línea más, de tipo <see cref="TipoLineaFactura.Retencion"/>, también en
/// negativo y con el mismo criterio de congelar el porcentaje. Se diferencia en que su importe se
/// recalcula cada vez que cambian los totales de la factura, porque la base de la retención es un
/// importe de la factura y no de la línea.
/// </para>
/// </summary>
public sealed class FacturaLinea : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    public Guid FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Orden de la línea en el documento. Lo guarda la línea y no el orden en que PostgreSQL
    /// devuelva las filas: el PDF y la vista de cuentas leen este número.
    /// </summary>
    public int Posicion { get; set; }

    public TipoLineaFactura Tipo { get; set; }

    /// <summary>Qué se factura en esta línea. Es texto de la persona, no una plantilla.</summary>
    public string Concepto { get; set; } = string.Empty;

    /// <summary>Cantidad facturable, con cuatro decimales: hay líneas que no son enteras (medios días).</summary>
    public decimal Cantidad { get; set; }

    /// <summary>Precio unitario en la moneda de la factura. Negativo solo en un descuento o en una retención.</summary>
    public decimal PrecioUnitario { get; set; }

    /// <summary>
    /// Cantidad por precio, redondeado medio hacia arriba (D27). Negativo en un descuento y en una
    /// retención (F3-04).
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>Tipo de ITBIS aplicado. Nulo cuando la línea no lleva impuesto (§3.8: opcional).</summary>
    public Guid? TipoItbisId { get; set; }

    public TipoItbis? TipoItbis { get; set; }

    /// <summary>
    /// Porcentaje de ITBIS congelado en el momento de escribir la línea. Se guarda aunque
    /// <see cref="TipoItbisId"/> venga nulo, porque el porcentaje es el dato que define el importe
    /// del impuesto una vez emitido el documento.
    /// </summary>
    public decimal? PorcentajeItbis { get; set; }

    /// <summary>ITBIS de esta línea ya redondeado. El de la factura es la suma de estos.</summary>
    public decimal ItbisImporte { get; set; }

    // ---------------------------------------------------------------- retención (F3-04, §3.9)

    /// <summary>
    /// Tipo de retención del que sale esta línea. Solo en líneas <see cref="TipoLineaFactura.Retencion"/>
    /// y solo a clientes agente de retención (§3.9, D14).
    /// </summary>
    public Guid? TipoRetencionId { get; set; }

    public TipoRetencion? TipoRetencion { get; set; }

    /// <summary>
    /// Porcentaje de retención congelado, como el de ITBIS: si mañana el administrador cambia el
    /// tipo, la factura sigue diciendo el porcentaje con el que se calculó.
    /// </summary>
    public decimal? PorcentajeRetencion { get; set; }

    /// <summary>
    /// Sobre qué importe de la factura se calculó esta línea, copiado del tipo (<c>base</c>). Vive en
    /// la línea porque el importe retenido cambia cuando cambian los totales de la factura: es la
    /// única forma de que la línea siga cuadrando sin releer la configuración en cada lectura.
    /// </summary>
    public BaseRetencion? BaseRetencion { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}
