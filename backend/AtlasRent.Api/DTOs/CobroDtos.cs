using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Registro de un cobro (F3-06, docs/ESPECIFICACION.md §3.10.1).
/// <para>
/// <b>Las asignaciones son opcionales.</b> Si no vienen, el servicio propone la FIFO dentro del
/// contrato y de la moneda del pago (§3.10.1: «la más antigua primero»); si vienen, son las que se
/// aplican y el servicio no las mueve. Editable significa eso: que quien cobra decide a qué factura
/// va el dinero, no que el sistema lo reparta por su cuenta.
/// </para>
/// <para>
/// Las reglas de negocio van en el servicio, que devuelve 409; los atributos solo cubren lo que se
/// puede rechazar sin abrir la base de datos.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CobroPeticion
{
    /// <summary>Cliente al que se cobra. Obligatorio.</summary>
    [Required]
    public required Guid ClienteId { get; init; }

    /// <summary>
    /// Contrato de origen. Nulo en un cobro suelto, que es legal (§3.10.1 no lo exige): en ese caso
    /// no hay a qué facturas asignar y el excedente no genera saldo a favor.
    /// </summary>
    public Guid? ContratoId { get; init; }

    /// <summary>Día en que entró el dinero, en día de negocio. Lo pone quien registra el cobro.</summary>
    [Required]
    public required DateOnly FechaEfectiva { get; init; }

    /// <summary>Moneda en la que se recibió el dinero (D8).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    [Required]
    public required Moneda MonedaRecibida { get; init; }

    /// <summary>Importe recibido. Tiene que ser mayor que cero.</summary>
    [Required]
    public required decimal ImporteRecibido { get; init; }

    /// <summary>Forma de pago configurada (§3.10.1). Obligatoria.</summary>
    [Required]
    public required Guid FormaPagoId { get; init; }

    /// <summary>
    /// Referencia que da el banco o el cliente. Opcional y de libre: no se valida ni se contrasta con
    /// nada, es para que quien lo busque lo encuentre.
    /// </summary>
    [StringLength(120)]
    public string? Referencia { get; init; }

    /// <summary>Cuenta en la que entró el dinero. Obligatoria.</summary>
    [Required]
    public required Guid CuentaReceptoraId { get; init; }

    /// <summary>
    /// Crédito ya cobrado que este pago aplica a las facturas del contrato, en la moneda recibida.
    /// Cero si no se usa.
    /// <para>
    /// <b>Se aplica de forma explícita y nunca solo.</b> La especificación no dice a qué factura se
    /// descuenta el saldo a favor, así que decide quien registra; la simulación devuelve el crédito
    /// disponible para que la interfaz pueda proponerlo.
    /// </para>
    /// </summary>
    public decimal UsarSaldoAFavor { get; init; }

    /// <summary>
    /// Asignaciones explícitas. Si viene vacía, el servicio propone la FIFO del contrato.
    /// <para>
    /// El importe va en la moneda de cada factura. Ninguna puede pasar de su pendiente, ni la suma
    /// puede pasar de <c>importeRecibido + usarSaldoAFavor</c>: lo que sobra es el excedente.
    /// </para>
    /// </summary>
    public IReadOnlyList<CobroAsignacionPeticion>? Asignaciones { get; init; }
}

/// <summary>Aplicación de un cobro a una factura concreta.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CobroAsignacionPeticion
{
    /// <summary>Factura a la que se aplica este importe.</summary>
    [Required]
    public required Guid FacturaId { get; init; }

    /// <summary>Importe aplicado, en la moneda de la factura. Tiene que ser mayor que cero.</summary>
    [Required]
    public required decimal Importe { get; init; }
}

/// <summary>Anulación de un cobro (F3-06, docs/ESPECIFICACION.md §3.10.1).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CobroAnulacionPeticion
{
    /// <summary>
    /// Por qué se anula. Obligatorio y no vacío: una corrección de dinero se explica siempre, y sin
    /// motivo no hay corrección, hay un borrado con otro nombre.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Motivo { get; init; }
}

/// <summary>Fila de la lista de cobros: la cabecera y sus totales, sin el detalle de asignaciones.</summary>
public sealed record CobroResumen(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? ContratoId,
    DateOnly FechaEfectiva,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda MonedaRecibida,
    decimal ImporteRecibido,
    Guid FormaPagoId,
    string? FormaPagoNombre,
    Guid CuentaReceptoraId,
    string? CuentaReceptoraNombre,
    decimal TipoCambioValor,
    DateOnly TipoCambioFecha,
    decimal SaldoFavorUsado,
    decimal Excedente,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoCobro>))] EstadoCobro Estado,
    DateTimeOffset CreatedAt);

/// <summary>
/// Cobro con sus asignaciones, que es como lo lee el detalle y como se imprime el recibo.
/// <para>
/// Las asignaciones <b>de un cobro anulado siguen ahí</b>, con su importe: se conservan como
/// historial y lo que dice de ellas es que ya no vigoran (§3.10.1, AGENTS.md §6).
/// </para>
/// <para>
/// <b>El recibo en PDF no está aquí</b> (TODO(F4-01)): es de la fase 4, con QuestPDF y las
/// plantillas por serie. Aquí no hay ningún endpoint que devuelva un PDF provisional, porque un
/// recibo de dinero provisional es un documento que alguien puede imprimir y archivar.
/// </para>
/// </summary>
public sealed record CobroRespuesta(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? ContratoId,
    DateOnly FechaEfectiva,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda MonedaRecibida,
    decimal ImporteRecibido,
    Guid FormaPagoId,
    string? FormaPagoNombre,
    string? Referencia,
    Guid CuentaReceptoraId,
    string? CuentaReceptoraNombre,
    decimal TipoCambioValor,
    DateOnly TipoCambioFecha,
    Guid? TipoCambioId,
    decimal SaldoFavorUsado,
    decimal Excedente,
    decimal TotalAsignado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoCobro>))] EstadoCobro Estado,
    DateTimeOffset? AnuladoAt,
    Guid? AnuladoPor,
    string? MotivoAnulacion,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CobroAsignacionRespuesta> Asignaciones);

/// <summary>Asignación tal y como queda guardada, vigente o no.</summary>
public sealed record CobroAsignacionRespuesta(
    Guid Id,
    Guid FacturaId,
    int? FacturaNumero,
    DateOnly? FacturaVencimiento,
    decimal Importe,
    bool Vigente,
    DateTimeOffset CreatedAt);

/// <summary>
/// Propuesta de asignación de un cobro, sin escribir nada (F3-06, docs/ESPECIFICACION.md §3.13).
/// <para>
/// <b>No muta nada</b>: es lo que ve quien registra el cobro antes de decidir, y lo que la interfaz
/// enseña para que se pueda corregir a mano.
/// </para>
/// </summary>
public sealed record CobroSimulacionRespuesta(
    Guid ClienteId,
    Guid? ContratoId,
    DateOnly FechaEfectiva,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda MonedaRecibida,
    decimal ImporteRecibido,
    decimal SaldoFavorUsado,
    decimal SaldoAFavorDisponible,
    decimal Disponible,
    decimal TotalAsignado,
    decimal Excedente,
    bool SaldoAFavorSeActualiza,
    IReadOnlyList<CobroAsignacionSimulada> Asignaciones,
    IReadOnlyList<CobroDeudaOtraMoneda> DeudaEnOtraMoneda);

/// <summary>Una factura sugerida por la FIFO, con lo que se le aplicaría y lo que le quedaría.</summary>
public sealed record CobroAsignacionSimulada(
    Guid FacturaId,
    int? Numero,
    DateOnly FechaVencimiento,
    decimal Importe,
    decimal PendienteAntes);

/// <summary>
/// Deuda pendiente del mismo contrato en otra moneda. <b>No se asigna</b> en esta fase (D7, §3.10.1):
/// pagar en una moneda deuda de otra es el pago en otra moneda de §3.10.2, que es de F3-07. Se
/// devuelve para que la interfaz pueda decirlo en vez de dejar a alguien creyendo que el cobro paga
/// toda la deuda del contrato.
/// </summary>
public sealed record CobroDeudaOtraMoneda(
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Pendiente,
    int Facturas);