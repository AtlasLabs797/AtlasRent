using AtlasRent.Api.Dominio;
using System.Text.Json.Serialization;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Un aviso de pago en la lista de revisión y en el detalle (docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>No lleva importes.</b> Lo que se le dice al cliente está escrito en el cuerpo del envío, y lo
/// que se le adjunta en la copia del estado de cuenta: ni la fila ni su respuesta necesitan una
/// tercera copia de la cifra que ya está en dos sitios y que se puede desincronizar.
/// </para>
/// </summary>
public sealed record RecordatorioFila(
    Guid Id,
    Guid ClienteId,
    string Cliente,
    Guid? UnidadId,
    string? Unidad,
    DateOnly Fecha,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoRecordatorio>))] EstadoRecordatorio Estado,
    Guid? EnvioId,
    string? Destinatario,
    string? MotivoOmision,
    DateTimeOffset CreatedAt);

/// <summary>Activar o levantar la pausa de recordatorios de un cliente (§3.14, D12).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordatoriosPausadosPeticion
{
    /// <summary><c>true</c> deja de avisar a este cliente; <c>false</c> vuelve a avisarlo.</summary>
    public bool Pausados { get; init; }
}