namespace AtlasRent.Api.Data;

/// <summary>
/// Columnas comunes de todo lo que se configura desde la pantalla de configuración
/// (docs/DOC_TECNICA.md §4.2). Existe para que el CRUD pueda escribir las marcas de creación y
/// modificación sin repetir las mismas cuatro propiedades en cada entidad.
/// </summary>
public interface IEntidadConfiguracion
{
    Guid Id { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }

    Guid? UpdatedBy { get; set; }
}