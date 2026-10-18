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

    /// <summary>
    /// Nombre de la plantilla de PDF de esta serie. Nulo = la de por defecto.
    /// <para>
    /// Se valida contra <c>Services.RegistroPlantillasPdf</c> al guardar: un nombre desconocido es
    /// un 400 con la lista de los válidos (F4-01).
    /// </para>
    /// </summary>
    [StringLength(120)]
    public string? PlantillaPdf { get; init; }

    /// <summary>Condiciones de pago que se imprimen en el pie del documento (F4-01).</summary>
    [StringLength(500)]
    public string? CondicionesPago { get; init; }

    public bool SumaAFacturado { get; init; }

    public bool SumaASaldos { get; init; }

    public bool GeneraComprobante { get; init; }

    public bool Activa { get; init; } = true;
}

/// <summary>
/// Alta o cambio del numerador de comprobante fiscal de una serie (F3-03,
/// docs/ESPECIFICACION.md §3.7).
/// <para>
/// Los valores los escribe el administrador y no hay ninguno por defecto en el código: un rango de
/// comprobantes es una autorización externa, no un dato que la aplicación pueda inventar.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NumeradorFiscalPeticion
{
    /// <summary>
    /// Tipo o prefijo del comprobante que autoriza este numerador. Se copia a cada comprobante
    /// emitido, así que cambiarlo aquí no reescribe los ya emitidos.
    /// </summary>
    [Required]
    [StringLength(16, MinimumLength = 1)]
    public required string TipoPrefijo { get; init; }

    /// <summary>Primer número autorizado del rango.</summary>
    [Required]
    public required int RangoDesde { get; init; }

    /// <summary>Último número autorizado del rango.</summary>
    [Required]
    public required int RangoHasta { get; init; }

    /// <summary>Día en que el rango deja de valer. En ese día todavía vale.</summary>
    [Required]
    public required DateOnly FechaVencimiento { get; init; }

    /// <summary>
    /// Número de comprobante que se emite a continuación. Se permite correrlo hasta
    /// <c>RangoHasta + 1</c> porque así se declara agotado un rango que ya se terminó; correrlo más
    /// allá rompe la emisión entera.
    /// </summary>
    [Required]
    public required int SiguienteNumero { get; init; }

    /// <summary>
    /// A partir de cuántos números restantes salta el aviso.
    /// <para>
    /// Es <c>required</c> y no solo <c>[Required]</c> porque, al ser un <c>int</c> no anulable, sin
    /// <c>required</c> la deserialización de System.Text.Json no exige que venga en la petición:
    /// una petición que lo omita se materializaría como 0 y el servicio respondería con un 409 de
    /// negocio en lugar del 400 que corresponde a una petición incompleta (BUG-012).
    /// </para>
    /// </summary>
    [Required]
    public required int UmbralAlerta { get; init; }
}

/// <summary>
/// Numerador de una serie fiscal, tal y como queda guardado. Los números que quedan son
/// <c>RangoHasta - SiguienteNumero + 1</c>, y van calculados porque es lo que decide el aviso y el
/// bloqueo: que quien lo lee no tenga que reconstruirlos.
/// </summary>
public sealed record NumeradorFiscalRespuesta(
    Guid Id,
    Guid SerieId,
    int SerieNumero,
    string TipoPrefijo,
    int RangoDesde,
    int RangoHasta,
    DateOnly FechaVencimiento,
    int SiguienteNumero,
    int UmbralAlerta)
{
    /// <summary>
    /// Números que quedan por sacar (<c>RangoHasta - SiguienteNumero + 1</c>). Cero significa
    /// agotado, y es lo que decide el aviso y el bloqueo de la emisión.
    /// </summary>
    public int Restantes { get; init; }
}

/// <summary>Fila de la lista y respuesta de una serie.</summary>
public sealed record SerieRespuesta(
    Guid Id,
    int Numero,
    string? Prefijo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? Moneda,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie Tipo,
    string? PlantillaPdf,
    string? CondicionesPago,
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

    /// <summary>
    /// RNC de la subempresa. F4-01 lo imprime la cabecera del PDF. Anulable porque una subempresa
    /// sin RNC todavía es válida: lo que no es válido es apuntar a una plantilla que no existe.
    /// </summary>
    [StringLength(32)]
    public string? Rnc { get; init; }

    /// <summary>Domicilio fiscal, para la cabecera del PDF de factura (docs/DESIGN.md §7).</summary>
    [StringLength(300)]
    public string? Direccion { get; init; }

    [StringLength(40)]
    public string? Telefono { get; init; }

    [StringLength(200)]
    public string? Email { get; init; }

    public bool Propietaria { get; init; }

    public bool Facturadora { get; init; }

    public bool Activa { get; init; } = true;
}

public sealed record SubempresaRespuesta(
    Guid Id,
    string Nombre,
    string? Rnc,
    string? Direccion,
    string? Telefono,
    string? Email,
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

    /// <summary>
    /// Días desde el vencimiento hasta el primer aviso (§3.14). Se puede dejar a nula a propósito:
    /// el trabajo de recordatorios no manda nada y deja una alerta diciendo qué configurar
    /// (criterio de BUG-007).
    /// </summary>
    [Range(typeof(int), "1", "365")]
    public int? DiasPrimerRecordatorio { get; init; }

    /// <summary>Días entre un aviso y el siguiente mientras siga habiendo saldo.</summary>
    [Range(typeof(int), "1", "365")]
    public int? DiasEntreRecordatorios { get; init; }
}

public sealed record AjusteGeneralRespuesta(
    Guid Id,
    decimal? ToleranciaCambioUsd,
    decimal? ToleranciaCambioDop,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModoRecordatorios>))] ModoRecordatorios ModoRecordatorios,
    int? DiasPrimerRecordatorio,
    int? DiasEntreRecordatorios);