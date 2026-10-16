namespace AtlasRent.Api.Data;

/// <summary>
/// Numerador del comprobante fiscal (F3-03, docs/ESPECIFICACION.md §3.7). Uno por serie fiscal.
/// <para>
/// <b>No es el contador de la serie.</b> <see cref="Serie.Siguiente"/> numera facturas y
/// <see cref="SiguienteNumero"/> numera comprobantes: son dos cosas distintas y confundirlas es el
/// error clásico —un rango autorizado que se renumera con las facturas, o una factura que consume un
/// número de comprobante que no le tocaba—. Aquí vive el segundo, con su propio
/// <c>UPDATE ... RETURNING</c> (docs/DOC_TECNICA.md §4.3).
/// </para>
/// <para>
/// Los valores del rango, del tipo de comprobante y del umbral los escribe el administrador: no hay
/// ninguno en el código ni en las migraciones (D14 y el criterio de aceptación de <c>docs/PLAN.md</c>).
/// </para>
/// </summary>
public sealed class NumeradorFiscal : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    /// <summary>Serie a la que pertenece. Único: el numerador es 1:1 con la serie fiscal.</summary>
    public Guid SerieId { get; set; }

    public Serie? Serie { get; set; }

    /// <summary>
    /// Tipo o prefijo del comprobante que autoriza este numerador. Se copia a cada comprobante
    /// emitido: si mañana el administrador cambia el tipo, los comprobantes viejos tienen que seguir
    /// diciendo el que tenían cuando salieron.
    /// </summary>
    public string TipoPrefijo { get; set; } = string.Empty;

    /// <summary>Primer número autorizado del rango.</summary>
    public int RangoDesde { get; set; }

    /// <summary>Último número autorizado del rango. Agotado es <c>SiguienteNumero &gt; RangoHasta</c>.</summary>
    public int RangoHasta { get; set; }

    /// <summary>
    /// Día en que este rango deja de valer. Vencido es <c>hoy &gt; FechaVencimiento</c>; el día del
    /// vencimiento todavía vale.
    /// </summary>
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>
    /// Número de comprobante que se emite a continuación. Lo mueve
    /// <see cref="Services.ServicioComprobantes"/> con <c>UPDATE ... RETURNING</c> dentro de la
    /// transacción de emisión, nunca <c>MAX() + 1</c>.
    /// </summary>
    public int SiguienteNumero { get; set; }

    /// <summary>
    /// A partir de cuántos números restantes salta el aviso. Lo configura el administrador porque el
    /// ritmo de emisión lo conoce la empresa, no el sistema.
    /// </summary>
    public int UmbralAlerta { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}