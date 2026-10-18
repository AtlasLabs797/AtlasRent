using System.Text.Json.Serialization;
using AtlasRent.Api.Data;

namespace AtlasRent.Api.DTOs;

/// <summary>Alta de un token de agente (F6-01).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AgenteTokenPeticion
{
    /// <summary>Nombre con el que el administrador lo reconocerá en el listado.</summary>
    public string Nombre { get; init; } = string.Empty;

    /// <summary>Propietario del token: el agente correrá con su rol y sus permisos.</summary>
    public required Guid UsuarioId { get; init; }

    /// <summary>
    /// Acciones que el token puede pedir. Nunca amplían el techo del propietario: si una de ellas no
    /// está en su rol, la petición sale denegada.
    /// </summary>
    public required string[] Acciones { get; init; }

    /// <summary>
    /// Vida en días. Si no viene, la de <c>Agentes__VidaDiasToken</c>. No hay tokens sin caducidad.
    /// </summary>
    public int? DiasValidez { get; init; }

    /// <summary>
    /// Consultas por día permitidas. Si no viene, la de <c>Agentes__LimiteConsultasDia</c>.
    /// </summary>
    public int? LimiteConsultasDia { get; init; }
}

/// <summary>
/// Fila de un token en el listado y en la respuesta del alta.
/// <para>
/// <see cref="Token"/> solo viene en el alta y es la única vez que sale el secreto. No se puede
/// volver a mostrar porque en la base de datos solo está su hash.
/// </para>
/// </summary>
public sealed record AgenteTokenRespuesta(
    Guid Id,
    string Nombre,
    Guid UsuarioId,
    string[] Acciones,
    DateTimeOffset Creado,
    DateTimeOffset? Caduca,
    DateTimeOffset? Revocado,
    DateTimeOffset? UltimoUso,
    int LimiteConsultasDia,
    // Solo en el alta: en el listado la propiedad no aparece, para que no se lea como un token
    // vacío que se puede volver a pedir.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token = null)
{
    public static AgenteTokenRespuesta De(AgenteToken fila, string? token = null) => new(
        fila.Id,
        fila.Nombre,
        fila.UsuarioId,
        fila.Acciones,
        fila.Creado,
        fila.Caduca,
        fila.Revocado,
        fila.UltimoUso,
        fila.LimiteConsultasDia,
        token);
}