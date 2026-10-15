using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Tarifa de una unidad por modalidad y por moneda (docs/ESPECIFICACION.md §3.1). El importe es de
/// la modalidad indicada <strong>y</strong> de la moneda indicada, y nunca se convierte entre
/// monedas: USD 1.000 en una modalidad es USD 1.000 (decisión D7).
/// <para>
/// El único es <c>(unidad, modalidad, moneda)</c>: una unidad no tiene dos tarifas para lo mismo.
/// </para>
/// </summary>
public sealed class TarifaUnidad
{
    public Guid Id { get; set; }

    public Guid UnidadId { get; set; }

    public ModalidadAlquiler Modalidad { get; set; }

    public Moneda Moneda { get; set; }

    /// <summary>Importe de una sola modalidad en una sola moneda. <c>numeric(18,2)</c>.</summary>
    public decimal Importe { get; set; }

    public Unidad Unidad { get; set; } = null!;
}