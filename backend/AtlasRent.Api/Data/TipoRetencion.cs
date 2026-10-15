using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Tipo de retención: nombre, porcentaje y sobre qué se calcula (docs/ESPECIFICACION.md §3.9).
/// Solo se aplica a clientes agente de retención.
/// </summary>
public sealed class TipoRetencion : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Porcentaje de retención, con cuatro decimales (<c>numeric(18,4)</c>).</summary>
    public decimal Porcentaje { get; set; }

    /// <summary>Sobre qué importe se aplica: subtotal, ITBIS o total.</summary>
    public BaseRetencion Base { get; set; }

    /// <summary>Tipo marcado por defecto: es el que se propone al agregar la línea de retención.</summary>
    public bool EsDefecto { get; set; }

    public bool Activo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}