namespace AtlasRent.Api.Data;

/// <summary>
/// Tipo de ITBIS: nombre y porcentaje (docs/ESPECIFICACION.md §3.9, D14). El porcentaje es
/// <em>dato</em>: lo configura el administrador en pantalla y de aquí lo lee el cálculo. En el
/// código no hay ninguna cifra fiscal.
/// </summary>
public sealed class TipoItbis : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Porcentaje de ITBIS, con cuatro decimales (<c>numeric(18,4)</c>).</summary>
    public decimal Porcentaje { get; set; }

    /// <summary>Tipo desactivado: no se puede elegir en una factura, pero se conserva.</summary>
    public bool Activo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}