using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Carga manual de un tipo de cambio (F3-01, docs/ESPECIFICACION.md §3.12: "si la API falla: alerta
/// y carga manual").
/// <para>
/// La fuente la pone el servicio, no quien escribe: este endpoint es siempre carga manual, y que
/// una persona pueda elegir <c>exchange_rate_api</c> desde aquí sería dejarle poner en la
/// auditoría que se ha consultado la fuente cuando lo ha escrito ella.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TipoCambioPeticion
{
    /// <summary>
    /// Día al que se aplica el valor. Vacío = hoy en Santo Domingo, que es lo que se quiere al
    /// recuperar el día que la API falló. Se puede mandar una fecha pasada porque el histórico se
    /// consulta y, si un informe viejo se consolidó sin cambio, tener el valor de ese día importa.
    /// </summary>
    public DateOnly? Fecha { get; init; }

    /// <summary>Moneda del valor. USD se rechaza: vale 1 por definición y no se guarda.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>
    /// Cuántas unidades de la moneda vale un dólar. El rango solo acota el orden de magnitud que
    /// cabe en <c>numeric(18,6)</c>; que el valor sea mayor que cero es una regla de negocio y la
    /// rechaza el servicio con un 409, no el modelo.
    /// </summary>
    [Range(typeof(decimal), "0", "1000000000000")]
    public required decimal Valor { get; init; }
}

/// <summary>
/// Fila del histórico y respuesta de la carga. <c>registradoAt</c> y <c>registradoPor</c> son las
/// columnas de trazabilidad de la tabla (<c>created_at</c>, <c>created_by</c>): quién lo cargó y
/// cuándo, que es lo que hace falta para saber si un valor es fiable.
/// </summary>
public sealed record TipoCambioRespuesta(
    Guid Id,
    DateOnly Fecha,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Valor,
    string Fuente,
    DateTimeOffset RegistradoAt,
    Guid? RegistradoPor,
    DateTimeOffset? ActualizadoAt);

/// <summary>
/// Valor de una moneda en una fecha concreta.
/// <para>
/// <c>id</c> y <c>fuente</c> son nulos para USD: el dólar vale 1 por definición y no tiene fila
/// (docs/ESPECIFICACION.md §3.12). La respuesta lo dice con un nulo en vez de inventar un
/// identificador.
/// </para>
/// </summary>
public sealed record TipoCambioActualRespuesta(
    DateOnly Fecha,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Valor,
    Guid? Id,
    string? Fuente);
