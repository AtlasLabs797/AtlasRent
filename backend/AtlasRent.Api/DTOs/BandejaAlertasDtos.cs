using System.Text.Json.Nodes;

namespace AtlasRent.Api.DTOs;

/// <summary>Una alerta de la bandeja (docs/ESPECIFICACION.md §3.16). <c>ResueltaAt</c> nulo = abierta.</summary>
public sealed record AlertaFila(
    Guid Id,
    string Tipo,
    string Mensaje,
    DateTimeOffset OcurridoAt,
    int Repeticiones,
    DateTimeOffset? ResueltaAt);

/// <summary>
/// Una fila de la auditoría para su lectura. No lleva la firma ni la cadena de integridad, y
/// <c>Datos</c> sale con los campos sensibles enmascarados.
/// </summary>
public sealed record AuditoriaFila(
    Guid Id,
    DateTimeOffset Fecha,
    Guid? UsuarioId,
    string? UsuarioNombre,
    string Ip,
    string Accion,
    string? Entidad,
    string? EntidadId,
    string? Resultado,
    string? Origen,
    JsonNode? Datos);
