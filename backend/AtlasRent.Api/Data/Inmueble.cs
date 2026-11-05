using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Inmueble: el edificio o la villa (docs/ESPECIFICACION.md §3.1). Puede tener varias unidades o
/// alquilarse completo; en el segundo caso hay una unidad implícita, que no se ve en pantalla y
/// llega con la fase de contratos.
/// <para>
/// La baja es lógica (<see cref="Activo"/>): un inmueble con contratos y facturas detrás tiene que
/// poder leerse siempre.
/// </para>
/// </summary>
public sealed class Inmueble : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Nombre con el que se conoce el inmueble.</summary>
    public string Nombre { get; set; } = string.Empty;

    public string? Direccion { get; set; }

    public TipoInmueble Tipo { get; set; }

    /// <summary>
    /// Subempresa propietaria. Las subempresas son etiquetas de propietaria/facturadora, no una
    /// multiempresa (docs/ESPECIFICACION.md §1).
    /// </summary>
    public Guid? PropietariaId { get; set; }

    public string? Notas { get; set; }

    /// <summary>Plantilla de contrato que se propone por defecto en los contratos de este inmueble.</summary>
    public string? PlantillaContratoPorDefecto { get; set; }

    /// <summary>
    /// Renovación por defecto en meses. <c>null</c> si no hay ninguna propuesta: la regla de
    /// renovación la pone el contrato, no el inmueble.
    /// </summary>
    public int? RenovacionDefectoMeses { get; set; }

    /// <summary>Baja lógica.</summary>
    public bool Activo { get; set; }

    public List<Unidad> Unidades { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}