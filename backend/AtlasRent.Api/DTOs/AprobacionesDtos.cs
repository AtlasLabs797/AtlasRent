using AtlasRent.Api.Dominio;
using System.Text.Json.Serialization;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Una fila de la bandeja de aprobaciones (F2-10, docs/ESPECIFICACION.md §3.16: "Muestra quién,
/// qué, valor actual y propuesto").
/// <para>
/// <b>Los dos valores de renta son lo que la hace útil y lo que hay que leer con cuidado.</b>
/// <see cref="RentaVigente"/> es lo que el contrato tiene hoy según su última versión escrita, o
/// <c>null</c> si el contrato nunca estuvo activo y no hay ninguna. <see cref="RentaPropuesta"/> es
/// lo que dice el contrato que espera decisión.
/// </para>
/// <para>
/// Cuando no hay cambio de renta los dos valores son el mismo número, y <see cref="HayCambioDeRenta"/>
/// es <c>false</c>: la bandeja no fabrica una diferencia que no existe. Cuando sí lo hay, el
/// <c>null</c> de la vigente es un contrato nuevo y se ve como tal, no como un cero.
/// </para>
/// </summary>
public sealed record AprobacionBandejaRespuesta(
    Guid SolicitudId,
    Guid ContratoId,
    Guid? SolicitadoPorId,
    string? SolicitadoPor,
    string? Cliente,
    string? Unidad,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoContrato>))]
    EstadoContrato Estado,
    DateOnly FechaInicio,
    DateOnly FechaFinVigente,
    decimal? RentaVigente,
    decimal RentaPropuesta,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    Moneda Moneda,
    bool HayCambioDeRenta,
    string HuellaContenido,
    DateTimeOffset SolicitadoAt,
    string? Comentario);