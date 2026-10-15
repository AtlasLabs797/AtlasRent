using System.ComponentModel.DataAnnotations;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Anexo del contrato (docs/ESPECIFICACION.md §3.5: "Anexos: catálogo, uno activo por tipo").
/// <para>
/// El catálogo es de uno activo por <see cref="Tipo"/>: mientras el anexo de un tipo esté activo no
/// puede entrar otro del mismo tipo, y para meter uno nuevo se desactiva el viejo. Desactivar no
/// borra nada, así que el anexo anterior se puede volver a activar y el contrato que lo tenía
/// congelado no se entera.
/// </para>
/// </summary>
public sealed class PlantillaAnexo : IEntidadTrazada
{
    /// <summary>Nombre del anexo.</summary>
    [MaxLength(120)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Tipo en el catálogo. Es lo que hace único al anexo activo: el índice único parcial de la base
    /// rechaza un segundo anexo activo del mismo tipo, no solo el servicio.
    /// </summary>
    public TipoAnexo Tipo { get; set; }

    /// <summary>
    /// Texto del anexo, por bloques. Contenido de usuario: se imprime tal cual y nunca se
    /// interpreta (ver <see cref="SeccionTexto"/>).
    /// </summary>
    public List<SeccionTexto> Secciones { get; set; } = [];

    /// <summary>Si el anexo es el vigente de su tipo.</summary>
    public bool Activa { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}