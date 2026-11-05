using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Cuenta receptora de un cobro (docs/ESPECIFICACION.md §3.10.1). La cuenta tiene una moneda, y esa
/// es la del importe recibido; el documento conserva la suya (D8, AGENTS.md §6).
/// </summary>
public sealed class CuentaReceptora : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Banco { get; set; }

    public Moneda Moneda { get; set; }

    public bool Activa { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}