namespace AtlasRent.Api.Data;

/// <summary>
/// Columnas de trazabilidad de toda fila que la aplicación escribe: quién la creó y quién la tocó
/// por última vez. <see cref="IEntidadConfiguracion"/> es este mismo conjunto dentro de la pantalla
/// de configuración; aquí vive el que usan clientes, prospectos y contactos.
/// </summary>
public interface IEntidadTrazada
{
    Guid Id { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }

    Guid? UpdatedBy { get; set; }
}