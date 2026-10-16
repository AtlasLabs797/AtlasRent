using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Qué se va a facturar de un mes natural, antes de hacerlo (F3-05, §3.8 «Periódica»).
/// <para>
/// La simulación no escribe nada —ni factura, ni línea, ni número de serie, ni tipo de cambio—: es
/// una vista de lo que el mismo servicio crearía. Por eso comparte el cálculo con la generación y
/// no puede dar un número distinto del que sale al ejecutar.
/// </para>
/// <para>
/// <b>La serie es obligatoria</b> y única para toda la ejecución. No hay serie «de la casa»: §3.7
/// deja la numeración en manos de la serie, con su moneda fija y su numerador de comprobante, y
/// elegirla por el sistema sería inventar una regla que nadie ha escrito. Un contrato cuya moneda no
/// la admite la serie aparece en la simulación como omitido, con su motivo, y se factura en otra
/// ejecución con la serie que sí.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SimulacionPeriodicaPeticion
{
    /// <summary>Cualquier día del mes que se quiere facturar. El mes natural es el que sale de aquí.</summary>
    [Required]
    public required DateOnly Periodo { get; init; }

    /// <summary>Serie con la que se emitirían todas las facturas de la ejecución (§3.7).</summary>
    [Required]
    public required Guid SerieId { get; init; }

    /// <summary>
    /// Contratos concretos. Nulo es lo normal: todos los candidatos del mes. Con la lista explícita,
    /// quien ejecuta está diciendo qué vio en la simulación, y eso es lo que queda en la auditoría.
    /// </summary>
    public Guid[]? ContratosIds { get; init; }
}

/// <summary>
/// La ejecución de la facturación periódica (F3-05). Mismo cuerpo que la simulación porque la
/// ejecución se hace sobre lo que se simuló: si se ejecuta sin mirar la simulación, la petición
/// llega sin <see cref="GeneracionPeriodicaPeticion.ContratosIds"/> y eso queda escrito en la
/// auditoría de la ejecución.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GeneracionPeriodicaPeticion
{
    /// <summary>Cualquier día del mes que se quiere facturar.</summary>
    [Required]
    public required DateOnly Periodo { get; init; }

    /// <summary>Serie con la que se emiten las facturas (§3.7).</summary>
    [Required]
    public required Guid SerieId { get; init; }

    /// <summary>
    /// Contratos concretos de la simulación. Nulo factura todos los candidatos del mes, y la
    /// auditoría lo dice: es la diferencia entre «se facturó lo que se vio» y «se facturó lo que
    /// había», y solo la segunda se acepta sin mirar.
    /// </summary>
    public Guid[]? ContratosIds { get; init; }
}

/// <summary>
/// Un contrato y lo que su factura de este mes costaría. Es lo que devuelve la simulación y también
/// lo que devuelve la ejecución, con <see cref="FacturaItemSimulado.FacturaId"/> y
/// <see cref="FacturaItemSimulado.FacturaNumero"/> puestos.
/// </summary>
public sealed record FacturaItemSimulado(
    Guid ContratoId,
    Guid ClienteId,
    Guid UnidadId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly PeriodoInicio,
    DateOnly PeriodoFin,
    int DiasDelMes,
    int DiasFacturables,
    decimal Renta,
    decimal Importe,
    bool Prorrateado,
    DateOnly Vencimiento,
    Guid SerieId,
    int SerieNumero,
    bool SerieGeneraComprobante,
    bool YaExiste,
    Guid? FacturaExistenteId,
    int? FacturaExistenteNumero,
    string? MotivoOmision,
    Guid? FacturaId,
    int? FacturaNumero)
{
    /// <summary>Se va a emitir: es candidato y no hay factura de este periodo.</summary>
    public bool SeEmite => MotivoOmision is null && !YaExiste;
}

/// <summary>Respuesta de la simulación: el mes, la serie que lo factura y una línea por contrato.</summary>
public sealed record SimulacionPeriodicaRespuesta(
    DateOnly Periodo,
    DateOnly PeriodoInicio,
    DateOnly PeriodoFin,
    int DiasDelMes,
    Guid SerieId,
    int SerieNumero,
    string SerieTipo,
    bool SerieGeneraComprobante,
    decimal ImporteTotal,
    IReadOnlyList<FacturaItemSimulado> Items);

/// <summary>
/// Respuesta de la ejecución: qué facturas salieron, con su número, y qué contratos se quedaron
/// fuera y por qué. La segunda parte es la que hace legible una doble ejecución: no falla, dice
/// que ya estaban.
/// </summary>
public sealed record GeneracionPeriodicaRespuesta(
    DateOnly Periodo,
    DateOnly PeriodoInicio,
    DateOnly PeriodoFin,
    Guid SerieId,
    int SerieNumero,
    bool SerieGeneraComprobante,
    bool ContratosIndicados,
    int Creadas,
    int Omitidas,
    decimal ImporteEmitido,
    IReadOnlyList<FacturaEmitida> Facturas,
    IReadOnlyList<ContratoOmitido> Omitidos);

/// <summary>Una factura que esta ejecución ha emitido de verdad: número y comprobante, si la serie lo lleva.</summary>
public sealed record FacturaEmitida(
    Guid FacturaId,
    Guid ContratoId,
    int? Numero,
    Guid? ComprobanteId,
    int? ComprobanteNumero,
    decimal Importe,
    DateOnly Vencimiento);

/// <summary>Un contrato que no se ha facturado en esta ejecución, con el motivo por el que no.</summary>
public sealed record ContratoOmitido(Guid ContratoId, string Motivo, Guid? FacturaExistenteId, int? FacturaExistenteNumero);

/// <summary>
/// Alta de la factura de una reserva corta (§3.4: «se cobra antes»).
/// <para>
/// Solo lleva la serie: el importe, la moneda, el cliente y el vencimiento son los de la reserva y
/// no se teclean aquí. Teclarlos sería una segunda fuente de la verdad para el mismo dato.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaReservaPeticion
{
    /// <summary>Serie con la que se emite (F3-05 emite siempre: quien llama necesita <c>factura.emitir</c>).</summary>
    [Required]
    public required Guid SerieId { get; init; }
}

/// <summary>
/// La factura de una reserva corta con el aviso del anticipo de §3.4.
/// <para>
/// <b>El anticipo no se cobra aquí.</b> D10 lo deja como nota de la reserva y no como cobro, y el
/// indicador «sin cobrar» de §3.4 sale de los cobros de F3-06; convertir la nota en un cobro sería
/// inventar un segundo camino de dinero. Lo que §3.4 pide es el aviso, y por eso va aquí.
/// </para>
/// </summary>
public sealed record FacturaReservaRespuesta(
    Guid FacturaId,
    Guid ReservaId,
    int? Numero,
    decimal Total,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly Vencimiento,
    string? AnticipoAviso);