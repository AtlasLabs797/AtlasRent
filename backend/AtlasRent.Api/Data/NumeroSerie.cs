namespace AtlasRent.Api.Data;

/// <summary>
/// Número emitido de una serie (docs/ESPECIFICACION.md §3.7 y docs/DOC_TECNICA.md §4.3).
/// <para>
/// La unicidad <c>(serie_id, numero)</c> es la red de seguridad del contador: si dos emisiones
/// simultáneas salieran con el mismo número, la segunda se rechazaría en la base de datos.
/// </para>
/// <para>
/// Un número anulado sigue ocupado. No se libera ni se vuelve a sacar (D28): la factura anulada
/// conserva su número y su comprobante, marcados como anulados.
/// </para>
/// </summary>
public sealed class NumeroSerie
{
    public Guid Id { get; set; }

    public Guid SerieId { get; set; }

    /// <summary>Número emitido dentro de la serie. Único junto con la serie.</summary>
    public int Numero { get; set; }

    /// <summary><c>emitido</c> o <c>anulado</c>.</summary>
    public EstadoNumeroSerie Estado { get; set; }

    public DateTimeOffset EmitidoAt { get; set; }

    public Guid EmitidoPor { get; set; }

    public DateTimeOffset? AnuladoAt { get; set; }

    public Guid? AnuladoPor { get; set; }

    /// <summary>Motivo de la anulación. Obligatorio, como en la anulación de facturas.</summary>
    public string? MotivoAnulacion { get; set; }
}