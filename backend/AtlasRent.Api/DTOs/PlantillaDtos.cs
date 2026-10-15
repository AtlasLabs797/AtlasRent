using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Un bloque de texto de una plantilla o de un anexo, tal y como lo manda el front.
/// <para>
/// <b>Texto, no marcado.</b> No hay campo que sea "html", "formato" ni "plantilla": lo que se
/// guarda es lo que se imprime. Si algún día hiciera falta un formato, sería un campo nuevo con su
/// propio permiso, no este.
/// </para>
/// </summary>
public sealed class SeccionPeticion
{
    /// <summary>Encabezado del bloque. Puede ir vacío.</summary>
    [StringLength(200)]
    public string? Titulo { get; set; }

    /// <summary>Cuerpo del bloque.</summary>
    [Required]
    [StringLength(20_000)]
    public string? Texto { get; set; }
}

/// <summary>Alta de una plantilla de contrato.</summary>
public sealed class PlantillaContratoPeticion
{
    /// <summary>
    /// Código con el que la citingán los contratos y el inmueble por defecto (F1-10). Es único y no
    /// se reutiliza nunca.
    /// </summary>
    [Required]
    [StringLength(60)]
    public string? Codigo { get; set; }

    [Required]
    [StringLength(120)]
    public string? Nombre { get; set; }

    /// <summary>Texto de la plantilla. Si no viene, la plantilla se crea vacía.</summary>
    public List<SeccionPeticion>? Secciones { get; set; }

    /// <summary>Tipos de anexo que esta plantilla necesita.</summary>
    public List<TipoAnexo>? AnexosTipos { get; set; }
}

/// <summary>
/// Edición del texto de una plantilla. El código no se edita: es su identidad y hay contratos
/// guardados que lo citan.
/// </summary>
public sealed class PlantillaContratoEdicionPeticion
{
    [Required]
    [StringLength(120)]
    public string? Nombre { get; set; }

    public List<SeccionPeticion>? Secciones { get; set; }

    public List<TipoAnexo>? AnexosTipos { get; set; }
}

/// <summary>Plantilla de contrato tal y como la devuelve la API.</summary>
public sealed record PlantillaContratoRespuesta(
    Guid Id,
    string Codigo,
    string Nombre,
    IReadOnlyList<SeccionTexto> Secciones,
    IReadOnlyList<TipoAnexo> AnexosTipos,
    bool Activa,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Alta de un anexo del catálogo.</summary>
public sealed class PlantillaAnexoPeticion
{
    [Required]
    [StringLength(120)]
    public string? Nombre { get; set; }

    /// <summary>
    /// Tipo del anexo. Obligatorio y de la lista de <see cref="TipoAnexo"/>: un tipo que no exista
    /// no llega ni al servicio, falla el enlace del modelo con un 400.
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<TipoAnexo>))]
    public TipoAnexo? Tipo { get; set; }

    public List<SeccionPeticion>? Secciones { get; set; }
}

/// <summary>
/// Edición de un anexo. El tipo tampoco se edita: cambiar de tipo es dar de baja este anexo y
/// crear otro del tipo nuevo, que es lo que permite tener el catálogo con uno activo por tipo.
/// </summary>
public sealed class PlantillaAnexoEdicionPeticion
{
    [Required]
    [StringLength(120)]
    public string? Nombre { get; set; }

    public List<SeccionPeticion>? Secciones { get; set; }
}

/// <summary>Anexo del catálogo tal y como lo devuelve la API.</summary>
public sealed record PlantillaAnexoRespuesta(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoAnexo>))]
    TipoAnexo Tipo,
    string Nombre,
    IReadOnlyList<SeccionTexto> Secciones,
    bool Activa,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// La plantilla congelada de un contrato, tal y como la devuelve la API: una copia, no una
/// referencia.
/// </summary>
public sealed record ContratoPlantillaCongeladaRespuesta(
    string Codigo,
    string Nombre,
    IReadOnlyList<SeccionTexto> Secciones,
    IReadOnlyList<AnexoCongelado> Anexos,
    DateTimeOffset CongeladaAt);