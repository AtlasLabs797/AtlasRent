namespace AtlasRent.Api.Data;

/// <summary>
/// Línea de una cotización (docs/ESPECIFICACION.md §3.3): lo que se ofrece, cuánto y a qué precio.
/// <para>
/// El importe no se calcula en la base de datos sino en la aplicación, con el redondeo medio hacia
/// arriba del sistema (AGENTS.md §6). Un <c>decimal</c> se multiplica exacto, así que la suma de las
/// líneas y el total guardado siempre cuadran.
/// </para>
/// <para>
/// <c>Posicion</c> es el orden en el que se escriben las líneas. PostgreSQL no garantiza el orden de
/// lectura de una tabla sin una columna que lo fije, y en un documento que se envía al cliente el
/// orden importa.
/// </para>
/// </summary>
public sealed class CotizacionLinea : IEntidadTrazada
{
    // TODO(A#): la especificación no pide número de cotización ni serie para ella (eso es §3.7 y
    // los planes F3/F4). Si lo necesita, hay que decidir aquí la serie y una migración que lo añada;
    // hasta entonces el PDF no lleva número, solo la fecha.

    public Guid Id { get; set; }

    public Guid CotizacionId { get; set; }

    public Cotizacion Cotizacion { get; set; } = null!;

    /// <summary>Orden de la línea dentro del documento, empezando en 1.</summary>
    public int Posicion { get; set; }

    /// <summary>Qué se ofrece.</summary>
    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    /// <summary>Cantidad × precio unitario, ya redondeado a dos decimales.</summary>
    public decimal Importe { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}