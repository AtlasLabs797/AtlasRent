using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>Alta o cambio de un cliente (docs/ESPECIFICACION.md §3.2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ClientePeticion
{
    /// <summary>Nombre de la persona física o razón social de la empresa.</summary>
    [Required]
    [UnaLinea]
    [StringLength(200, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))]
    public required TipoCliente Tipo { get; init; }

    /// <summary>
    /// Cédula, pasaporte o RNC. Se guarda cifrado y además se indexa su HMAC para buscarlo y
    /// detectar duplicados. Opcional: hay altas de importación sin documento.
    /// </summary>
    [StringLength(64)]
    public string? Documento { get; init; }

    /// <summary>
    /// Número de cliente. Solo lo manda la importación de Factusol, que conserva el número que ya
    /// existía; si no viene, lo asigna la secuencia de la base de datos.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? Numero { get; init; }

    [EmailSeguro]
    [StringLength(200)]
    public string? Email { get; init; }

    [StringLength(40)]
    public string? Telefono { get; init; }

    [StringLength(300)]
    public string? Direccion { get; init; }

    [StringLength(1000)]
    public string? Notas { get; init; }

    public bool AgenteRetencion { get; init; }

    public bool EnGestionCobro { get; init; }

    public bool RecordatoriosPausados { get; init; }
}

/// <summary>
/// Cliente en la lista y en las respuestas de alta y cambio.
/// <para>
/// <strong>No lleva el documento</strong>, y es a propósito: las respuestas de una mutación se
/// guardan en <c>idempotencia</c> para poder repetir la llamada, y ahí el documento se quedaría en
/// claro. El documento completo se ve en la ficha (<c>GET /api/v1/clientes/{id}</c>), que es donde
/// hace falta (decisión D16).
/// </para>
/// </summary>
public sealed record ClienteResumen(
    Guid Id,
    int Numero,
    string Nombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))] TipoCliente Tipo,
    string? Email,
    string? Telefono,
    bool AgenteRetencion,
    bool EnGestionCobro,
    bool RecordatoriosPausados,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>
/// Ficha del cliente: la lista más el documento de identidad descifrado, que se ve completo
/// (decisión D16: sin enmascarar) y que en reposo está cifrado.
/// </summary>
public sealed record ClienteRespuesta(
    Guid Id,
    int Numero,
    string Nombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))] TipoCliente Tipo,
    string? Documento,
    string? Email,
    string? Telefono,
    string? Direccion,
    string? Notas,
    bool AgenteRetencion,
    bool EnGestionCobro,
    bool RecordatoriosPausados,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>Alta o cambio de un contacto de cliente (docs/ESPECIFICACION.md §3.2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ContactoPeticion
{
    [Required]
    [UnaLinea]
    [StringLength(200, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [StringLength(120)]
    public string? Relacion { get; init; }

    [EmailSeguro]
    [StringLength(200)]
    public string? Email { get; init; }

    [StringLength(40)]
    public string? Telefono { get; init; }

    /// <summary>Deja a este contacto como principal y quita el flag a los demás del cliente.</summary>
    public bool EsPrincipal { get; init; }
}

public sealed record ContactoRespuesta(
    Guid Id,
    Guid ClienteId,
    string Nombre,
    string? Relacion,
    string? Email,
    string? Telefono,
    bool EsPrincipal);

/// <summary>Alta o cambio de un prospecto (docs/ESPECIFICACION.md §3.2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProspectoPeticion
{
    [Required]
    [UnaLinea]
    [StringLength(200, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))]
    public required TipoCliente Tipo { get; init; }

    /// <summary>Documento de identidad: cifrado en reposo, con índice ciego para buscarlo.</summary>
    [StringLength(64)]
    public string? Documento { get; init; }

    [EmailSeguro]
    [StringLength(200)]
    public string? Email { get; init; }

    [StringLength(40)]
    public string? Telefono { get; init; }

    [StringLength(1000)]
    public string? Notas { get; init; }
}

/// <summary>Prospecto en la lista y en las respuestas de alta y cambio. Sin documento, por lo mismo
/// que en <see cref="ClienteResumen"/>.</summary>
public sealed record ProspectoResumen(
    Guid Id,
    string Nombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))] TipoCliente Tipo,
    string? Email,
    string? Telefono,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>Prospecto en la ficha, con el documento descifrado.</summary>
public sealed record ProspectoRespuesta(
    Guid Id,
    string Nombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoCliente>))] TipoCliente Tipo,
    string? Documento,
    string? Email,
    string? Telefono,
    string? Notas,
    bool Activo,
    DateTimeOffset CreatedAt);