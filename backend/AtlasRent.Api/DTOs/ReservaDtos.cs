using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta y edición de una reserva. Un día de la corta se pide con sus horas, no con su tipo: el tipo
/// lo deduce el servicio de las horas (4 h exactas = medio día; la jornada entera = día completo,
/// D11).
/// <para>
/// Las reglas del documento van en el servicio, que las devuelve como 409, no como atributos: lo que
/// los atributos rechazan es solo lo que no es ni siquiera una forma de reserva.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReservaPeticion
{
    /// <summary>Unidad reservada. Obligatoria.</summary>
    public required Guid UnidadId { get; init; }

    /// <summary>Cliente de la reserva. Excluyente con <see cref="ProspectoId"/>.</summary>
    public Guid? ClienteId { get; init; }

    /// <summary>Prospecto de la reserva. Excluyente con <see cref="ClienteId"/>.</summary>
    public Guid? ProspectoId { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoReserva>))]
    public required TipoReserva Tipo { get; init; }

    /// <summary>Día de entrada. Obligatoria en las dos clases de reserva.</summary>
    public required DateOnly EntradaPrevista { get; init; }

    /// <summary>
    /// Día de salida. En la corta tiene que ser el mismo que la entrada; en la mensual no se
    /// escribe, porque el bloqueo va hasta que haya contrato.
    /// </summary>
    public DateOnly? SalidaPrevista { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ModalidadAlquiler>))]
    public required ModalidadAlquiler Modalidad { get; init; }

    /// <summary>Moneda única de la reserva (D7), igual que en la cotización.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>Importe de la reserva (foto de la tarifa en el momento de reservarla).</summary>
    public decimal Importe { get; init; }

    /// <summary>
    /// Anticipo anotado (D10). Es una nota con número, no un cobro: aquí no se crea ninguna fila de
    /// cobro ni de factura, porque el cobro se registra al pagar la factura (F3-01).
    /// </summary>
    public decimal? AnticipoImporte { get; init; }

    /// <summary>Nota del anticipo: qué se acordó y, al cancelar, si se devuelve o se retiene.</summary>
    [StringLength(300)]
    public string? AnticipoNota { get; init; }

    [StringLength(1000)]
    public string? Observaciones { get; init; }

    /// <summary>Días de la corta, con horas. Vacío en la mensual.</summary>
    public IReadOnlyList<ReservaDiaPeticion> Dias { get; init; } = [];
}

/// <summary>Un día de reserva corta con su franja acordada.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReservaDiaPeticion
{
    public required DateOnly Fecha { get; init; }

    public required TimeOnly HoraEntrada { get; init; }

    /// <summary>Siempre el mismo día que la entrada (docs/ESPECIFICACION.md §3.4).</summary>
    public required TimeOnly HoraSalida { get; init; }
}

/// <summary>Filtros de la lista paginada de reservas.</summary>
public sealed class ReservaFiltro
{
    public EstadoReserva? Estado { get; init; }

    public TipoReserva? Tipo { get; init; }

    public Guid? UnidadId { get; init; }

    public Guid? ClienteId { get; init; }

    public Guid? ProspectoId { get; init; }

    /// <summary>Reservas cuya entrada prevista cae en este día o después.</summary>
    public DateOnly? Desde { get; init; }

    /// <summary>Reservas cuya entrada prevista cae en este día o antes.</summary>
    public DateOnly? Hasta { get; init; }

    /// <summary>Texto libre: nombre del cliente, del prospecto o de la unidad.</summary>
    public string? Texto { get; init; }

    /// <summary>Incluye las dadas de baja. Por defecto solo las activas.</summary>
    public bool IncluirInactivas { get; init; }
}

/// <summary>Una fila de la lista: lo justo para pintar una tabla.</summary>
public sealed record ReservaResumen(
    Guid Id,
    Guid UnidadId,
    string Unidad,
    Guid? ClienteId,
    Guid? ProspectoId,
    string? Cliente,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoReserva>))] TipoReserva Tipo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoReserva>))] EstadoReserva Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModalidadAlquiler>))] ModalidadAlquiler Modalidad,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Importe,
    decimal? AnticipoImporte,
    DateOnly FechaReserva,
    DateOnly EntradaPrevista,
    DateOnly? SalidaPrevista,
    DateOnly? EntradaReal,
    DateOnly? SalidaReal,
    bool Activo,
    int Dias,
    bool TieneAnticipo,
    DateTimeOffset CreatedAt);

/// <summary>La reserva entera, con sus días.</summary>
public sealed record ReservaRespuesta(
    Guid Id,
    Guid UnidadId,
    string Unidad,
    Guid? ClienteId,
    Guid? ProspectoId,
    string? Cliente,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoReserva>))] TipoReserva Tipo,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoReserva>))] EstadoReserva Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModalidadAlquiler>))] ModalidadAlquiler Modalidad,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Importe,
    decimal? AnticipoImporte,
    string? AnticipoNota,
    string? Observaciones,
    DateOnly FechaReserva,
    DateOnly EntradaPrevista,
    DateOnly? SalidaPrevista,
    DateOnly? EntradaReal,
    DateOnly? SalidaReal,
    bool Activo,
    IReadOnlyList<ReservaDiaRespuesta> Dias,
    DateTimeOffset CreatedAt);

/// <summary>Un día de la corta, tal como se pidió y tal como quedó clasificado.</summary>
public sealed record ReservaDiaRespuesta(
    Guid Id,
    DateOnly Fecha,
    TimeOnly HoraEntrada,
    TimeOnly HoraSalida,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoOcupacionDia>))]
    TipoOcupacionDia Tipo);