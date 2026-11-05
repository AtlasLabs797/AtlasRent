using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta de una fianza de contrato (F3-09, docs/ESPECIFICACION.md §3.11).
/// <para>
/// Lo exige, su importe, su moneda y cuántas fianzas cubre. El contrato es obligatorio y es el único
/// dato que no viene del cuerpo del contrato porque una fianza no se puede dar sin él: es la cuenta
/// del contrato (§3.13).
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FianzaPeticion
{
    /// <summary>Contrato cuya fianza se crea.</summary>
    [Required]
    public required Guid ContratoId { get; init; }

    /// <summary>
    /// Cuánto se exige en total, en la moneda de la fianza. Tiene que ser mayor que cero.
    /// </summary>
    [Required]
    public required decimal ImporteExigido { get; init; }

    /// <summary>
    /// Moneda en que se exige y se devuelve. Va como texto —«USD»— y no como número, igual que en
    /// <c>CobroPeticion.MonedaRecibida</c>: un enum en la API es un nombre que se lee en un log y en
    /// una petición, no un entero cuyo significado hay que ir a buscar al enum.
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>
    /// Cuántas fianzas cubre este depósito (§3.11). Entero de negocio y mayor que cero.
    /// </summary>
    [Required]
    public required int NumeroFianzas { get; init; }
}

/// <summary>
/// Devolución de una fianza (F3-09, docs/ESPECIFICACION.md §3.11 y §3.6).
/// <para>
/// <b>El motivo es obligatorio cuando hay descuento o cuando la devolución no deja la fianza a
/// cero.</b> El modelo de datos no lo puede decidir —no sabe cuál era el saldo—, así que lo exige
/// <c>ServicioFianzas</c> contra el saldo real, y por eso las reglas de <c>Motivo</c> y de
/// <c>ImporteDescontado</c> no están como <c>[Required]</c>: se describirían mal.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FianzaDevolucionPeticion
{
    /// <summary>Lo que se devuelve al cliente, en la moneda de la fianza. Nunca negativo.</summary>
    [Required]
    public required decimal Importe { get; init; }

    /// <summary>
    /// Lo que se retiene y no se devuelve, normalmente por faltantes de inventario (§3.6). Nunca
    /// negativo. Si es mayor que cero, el motivo es obligatorio.
    /// </summary>
    [Required]
    public required decimal ImporteDescontado { get; init; }

    /// <summary>
    /// Por qué. Obligatorio cuando hay descuento o cuando la devolución no deja la fianza a cero.
    /// </summary>
    [StringLength(500)]
    public string? Motivo { get; init; }

    /// <summary>Día de negocio en que se devuelve. Nulo es hoy, que es lo que se espera casi siempre.</summary>
    public DateOnly? Fecha { get; init; }
}

/// <summary>
/// Fila de la lista de fianzas (F3-09, docs/ESPECIFICACION.md §3.11).
/// <para>
/// <b>Es exactamente lo que F3-10 necesita para pintar la fila de fianza de la vista única de
/// cuentas</b> (§3.13): lo exigido, cuántas fianzas son, en qué moneda, cuánto se ha cobrado, qué
/// queda y en qué estado está, con el contrato al que pertenece.
/// </para>
/// <para>
/// <see cref="Cobrado"/> y <see cref="Saldo"/> no son columnas: los calcula la consulta de lectura, que
/// es lo mismo que hace F3-06 con las facturas.
/// </para>
/// </summary>
public sealed record FianzaResumen(
    Guid Id,
    Guid ContratoId,
    Guid ClienteId,
    string ClienteNombre,
    Guid UnidadId,
    string UnidadNombre,
    decimal ImporteExigido,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    int NumeroFianzas,
    decimal Cobrado,
    decimal Devuelto,
    decimal Descontado,
    decimal Saldo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFianza>))] EstadoFianza Estado,
    DateTimeOffset? RetenidaAt,
    DateTimeOffset? EnLiquidacionAt,
    DateTimeOffset? DevueltaAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Fianza con sus devoluciones (F3-09). La cabecera es la misma que la de la lista; las devoluciones
/// son el rastro de lo que salió y lo que se retuvo, que es lo que §3.6 hay que poder leer después.
/// </summary>
public sealed record FianzaRespuesta(
    FianzaResumen Fianza,
    IReadOnlyList<FianzaDevolucionRespuesta> Devoluciones);

/// <summary>Una devolución tal y como queda guardada.</summary>
public sealed record FianzaDevolucionRespuesta(
    Guid Id,
    Guid FianzaId,
    decimal Importe,
    decimal ImporteDescontado,
    string? Motivo,
    DateOnly Fecha,
    DateTimeOffset CreatedAt);