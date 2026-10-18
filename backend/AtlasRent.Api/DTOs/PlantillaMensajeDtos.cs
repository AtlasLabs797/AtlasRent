using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Texto de una plantilla de mensaje (docs/ESPECIFICACION.md §3.14). Solo el administrador lo edita
/// (<c>plantilla.editar</c>).
/// <para>
/// El cuerpo va en <b>texto plano</b> y las variables se sustituyen de forma literal. No hay forma de
/// escribir HTML ni de meter entidades: si el texto del cliente aparece con una etiqueta alrededor,
/// sale con la etiqueta al lado en el correo y se ve.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlantillaMensajePeticion
{
    [JsonConverter(typeof(JsonStringEnumConverter<TipoMensajeEnvio>))]
    public required TipoMensajeEnvio Tipo { get; init; }

    /// <summary>Código de idioma. «es» es la que se siembra; cualquier otro se puede crear.</summary>
    [Required]
    [MaxLength(10)]
    [MinLength(2)]
    public string Idioma { get; init; } = "es";

    [Required]
    [MaxLength(300)]
    [MinLength(1)]
    public string Asunto { get; init; } = string.Empty;

    [Required]
    [MaxLength(8000)]
    public string Cuerpo { get; init; } = string.Empty;

    /// <summary>Desactivar no borra: la fila sigue contando qué texto encontró un envío antiguo.</summary>
    public bool Activa { get; init; } = true;
}

public sealed record PlantillaMensajeRespuesta(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoMensajeEnvio>))] TipoMensajeEnvio Tipo,
    string Idioma,
    string Asunto,
    string Cuerpo,
    bool Activa,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);