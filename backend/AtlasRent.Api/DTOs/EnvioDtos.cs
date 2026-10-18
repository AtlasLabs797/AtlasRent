using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Petición de envío: un documento a un destinatario (docs/ESPECIFICACION.md §3.14).
/// <para>
/// El destinatario se puede escribir de tres maneras y en ese orden: <see cref="ContactoClienteId"/>
/// (el de la ficha del cliente), <see cref="Email"/> a mano, o solo <see cref="ClienteId"/> y que
/// salga el correo del cliente. No se acepta un destinatario vacío: se responde 400 diciendo cuál de
/// las tres falta, en vez de dejar que falle más tarde al construir el mensaje.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnvioPeticion
{
    /// <summary>Qué clase de documento se manda. Obligatorio: sin él no hay nada que generar.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TipoDocumentoEnvio>))]
    public required TipoDocumentoEnvio TipoDocumento { get; init; }

    /// <summary>
    /// Identificador del documento. Para <c>EstadoCuenta</c> es el del cliente y el año va en
    /// <see cref="DocumentoAnio"/>.
    /// </summary>
    public required Guid DocumentoId { get; init; }

    /// <summary>
    /// Año del estado de cuenta. Solo se usa con <see cref="TipoDocumentoEnvio.EstadoCuenta"/> y es
    /// opcional: si no viene, sale el año en curso, que es lo que quiere quien manda «el estado de
    /// cuenta» sin más.
    /// </summary>
    public int? DocumentoAnio { get; init; }

    /// <summary>Contacto de la ficha del cliente al que se manda.</summary>
    public Guid? ContactoClienteId { get; init; }

    /// <summary>Cliente al que se manda, si el documento no lo trae ya (un estado de cuenta).</summary>
    public Guid? ClienteId { get; init; }

    /// <summary>
    /// Correo escrito a mano. Entra en minúsculas, que es como se compara para no mandar dos veces al
    /// mismo por escribirlo con distinta capitalización.
    /// </summary>
    [EmailAddress]
    public string? Email { get; init; }

    /// <summary>
    /// Idioma de la plantilla. Si no viene se usa <c>es</c>, que es la única que se siembra. Un idioma
    /// sin plantilla no es un error: sale la de «es» y la fila guarda el idioma real, para que nadie
    /// piense que se mandó en el que se pidió.
    /// </summary>
    [MaxLength(10)]
    public string? Idioma { get; init; }

    /// <summary>
    /// Reenvía aunque el documento ya se haya enviado a este destinatario (§3.14). Sin esto, el
    /// segundo envío al mismo correo es un 409.
    /// </summary>
    public bool Forzar { get; init; }
}

/// <summary>
/// Cómo se ve un envío en la lista. <b>No lleva el PDF</b>: la copia son bytes y la lista se pinta
/// entera en una pantalla. La copia sale por <c>GET /api/v1/envios/{id}/pdf</c>.
/// </summary>
public sealed record EnvioFila(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoDocumentoEnvio>))] TipoDocumentoEnvio Tipo,
    Guid DocumentoId,
    int? DocumentoAnio,
    Guid? ClienteId,
    string DestinatarioEmail,
    string? DestinatarioNombre,
    string Asunto,
    string Idioma,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoEnvio>))] EstadoEnvio Estado,
    string NombreArchivo,
    DateTimeOffset? DocumentoModificadoAt,
    int Intentos,
    string? UltimoError,
    DateTimeOffset? EnviadoAt,
    DateTimeOffset CreatedAt);