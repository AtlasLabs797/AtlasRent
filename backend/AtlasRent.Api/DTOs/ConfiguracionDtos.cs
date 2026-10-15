using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>Alta o cambio de una serie (docs/ESPECIFICACION.md §3.7).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SeriePeticion
{
    /// <summary>Número visible de la serie.</summary>
    [Range(1, 999)]
    public required int Numero { get; init; }

    /// <summary>Prefijo del número emitido. Una serie sin prefijo no puede emitir.</summary>
    [StringLength(16, MinimumLength = 1)]
    public required string Prefijo { get; init; }

    /// <summary>Moneda fija de la serie. Nula = moneda libre.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public Moneda? Moneda { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))]
    public required TipoSerie Tipo { get; init; }

    [StringLength(120)]
    public string? PlantillaPdf { get; init; }

    public bool SumaAFacturado { get; init; }

    public bool SumaASaldos { get; init; }

    public bool GeneraComprobante { get; init; }

    public bool Activa { get; init; } = true;
}

/// <summary>Fila de la lista y respuesta de una serie.</summary>
public sealed record SerieRespuesta(
    Guid Id,
    int Numero,
    string? Prefijo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? Moneda,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie Tipo,
    string? PlantillaPdf,
    bool SumaAFacturado,
    bool SumaASaldos,
    bool GeneraComprobante,
    int Siguiente,
    bool Activa);

/// <summary>Alta o cambio de un tipo de ITBIS (docs/ESPECIFICACION.md §3.9, D14).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TipoItbisPeticion
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    /// <summary>Porcentaje de ITBIS. Lo pone el administrador; en el código no hay ninguno.</summary>
    [Range(typeof(decimal), "0", "100")]
    public required decimal Porcentaje { get; init; }

    public bool Activo { get; init; } = true;
}

public sealed record TipoItbisRespuesta(Guid Id, string Nombre, decimal Porcentaje, bool Activo);

/// <summary>Alta o cambio de un tipo de retención (docs/ESPECIFICACION.md §3.9, D14).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TipoRetencionPeticion
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    /// <summary>Porcentaje de retención. Lo pone el administrador; en el código no hay ninguno.</summary>
    [Range(typeof(decimal), "0", "100")]
    public required decimal Porcentaje { get; init; }

    /// <summary>Sobre qué se aplica: <c>Subtotal</c>, <c>Itbis</c> o <c>Total</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BaseRetencion>))]
    public required BaseRetencion Base { get; init; }

    /// <summary>Marcar este tipo como el de por defecto quita el distintivo al que lo tuviera.</summary>
    public bool EsDefecto { get; init; }

    public bool Activo { get; init; } = true;
}

public sealed record TipoRetencionRespuesta(
    Guid Id,
    string Nombre,
    decimal Porcentaje,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BaseRetencion>))] BaseRetencion Base,
    bool EsDefecto,
    bool Activo);

/// <summary>Alta o cambio de una forma de pago (docs/ESPECIFICACION.md §3.10.1).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FormaPagoPeticion
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    public bool Activa { get; init; } = true;
}

public sealed record FormaPagoRespuesta(Guid Id, string Nombre, bool Activa);

/// <summary>Alta o cambio de una cuenta receptora (docs/ESPECIFICACION.md §3.10.1).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CuentaReceptoraPeticion
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [StringLength(120)]
    public string? Banco { get; init; }

    /// <summary>Moneda en la que se recibe en esta cuenta.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    public bool Activa { get; init; } = true;
}

public sealed record CuentaReceptoraRespuesta(
    Guid Id,
    string Nombre,
    string? Banco,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    bool Activa);

/// <summary>Alta o cambio de una subempresa propietaria o facturadora.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SubempresaPeticion
{
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    public bool Propietaria { get; init; }

    public bool Facturadora { get; init; }

    public bool Activa { get; init; } = true;
}

public sealed record SubempresaRespuesta(
    Guid Id,
    string Nombre,
    bool Propietaria,
    bool Facturadora,
    bool Activa);

/// <summary>Ajustes generales: tolerancias de cambio y modo de recordatorios.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AjusteGeneralPeticion
{
    /// <summary>Nula si el administrador decide que no hay diferencia cerrable.</summary>
    [Range(typeof(decimal), "0", "1000000")]
    public decimal? ToleranciaCambioUsd { get; init; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? ToleranciaCambioDop { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ModoRecordatorios>))]
    public required ModoRecordatorios ModoRecordatorios { get; init; }
}

public sealed record AjusteGeneralRespuesta(
    Guid Id,
    decimal? ToleranciaCambioUsd,
    decimal? ToleranciaCambioDop,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModoRecordatorios>))] ModoRecordatorios ModoRecordatorios);