using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

// Catálogos de solo lectura para elegir (api/v1/catalogos). Solo lo necesario para mostrar la opción:
// nada de contadores, plantillas, RNC ni datos de contacto de la configuración.

public sealed record SerieCatalogo(
    Guid Id,
    int Numero,
    string? Prefijo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? Moneda,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie Tipo,
    bool GeneraComprobante);

public sealed record FormaPagoCatalogo(Guid Id, string Nombre);

public sealed record CuentaReceptoraCatalogo(
    Guid Id,
    string Nombre,
    string? Banco,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda);

public sealed record SubempresaCatalogo(Guid Id, string Nombre);

/// <summary>Tolerancia de diferencia de cambio que aplica el registro de cobros. Nula = sin configurar.</summary>
public sealed record ParametrosCobroRespuesta(decimal? ToleranciaCambioUsd, decimal? ToleranciaCambioDop);

public sealed record ModoRecordatoriosRespuesta(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModoRecordatorios>))] ModoRecordatorios Modo);
