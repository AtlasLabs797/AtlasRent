namespace AtlasRent.Api.Data;

/// <summary>
/// Forma de pago de un cobro (docs/ESPECIFICACION.md §3.10.1). La lista la lleva el
/// administrador.
/// </summary>
public sealed class FormaPago : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}