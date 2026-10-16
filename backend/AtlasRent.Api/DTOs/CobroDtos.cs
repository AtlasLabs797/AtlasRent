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

    /// <summary>
    /// Tipo de cambio <b>realmente aplicado</b> en este cobro (F3-07, §3.10.2, D8).
    /// <para>
    /// Si no viene, se usa el cambio del día de la moneda recibida, que es lo que guarda
    /// <c>ServiceTiposCambio</c> y lo que además queda congelado en el cobro. Si viene, es porque el
    /// gerente cobró a un cambio distinto del de la casa —el del banco, el delanton, el que puso el
    /// cliente— y quiere dejar constancia de ese, que es el único que explica el importe recibido.
    /// </para>
    /// <para>
    /// <b>El cambio aplicado va en cada asignación, y el del día sigue en el cobro.</b> No se
    /// pisa uno con el otro: el del día es el dato de referencia que congela D9 y con él se
    /// justifican los informes; el aplicado es el dato del cobro. Los dos conviven.
    /// </para>
    /// <para>
    /// Tiene que ser mayor que cero: un cambio de cero no convierte nada y uno negativo cambiaría el
    /// signo de la deuda. <b>Lo comprueba el servicio y sale en 400</b>, no con un atributo
    /// <c>[Range]</c>: ese atributo parsea sus límites con la cultura del proceso, que aquí escribe
    /// <c>59,50</c>, así que un rango escrito con punto no se lee nunca y dejaría todas las peticiones
    /// de cobro en 400.
    /// </para>
    /// </summary>
    public decimal? TipoCambioAplicado { get; init; }
}

/// <summary>
/// Alta de un ajuste por diferencia de cambio (F3-07, docs/ESPECIFICACION.md §3.10.2).
/// <para>
/// <b>El importe no se pide</b>: es el pendiente real de la factura en el momento del ajuste, y que
/// quien lo firma elija de cuánto es la diferencia convertiría el ajuste en la manera cómoda de tapar
/// un saldo del tamaño que quiera. El servicio lo calcula y solo lo acepta si cabe en la tolerancia
/// configurada.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AjusteDiferenciaCambioPeticion
{
    /// <summary>Factura cuyo pendiente se cierra. Obligatoria.</summary>
    [Required]
    public required Guid FacturaId { get; init; }

    /// <summary>
    /// Tipo de cambio que dejó la diferencia, <c>numeric(18,6)</c>. Obligatorio y mayor que cero:
    /// sin él, la fila no dice por qué el pendiente es el que es y no se puede reconstruir nada. Lo
    /// comprueba el servicio (400), por el mismo motivo que en <see cref="CobroPeticion.TipoCambioAplicado"/>.
    /// </summary>
    public decimal TipoCambioValor { get; init; }

    /// <summary>
    /// Por qué se ajusta. Obligatorio y no vacío (400 sin él): sin motivo, un euro de diferencia no
    /// se distingue de un error de captura.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Motivo { get; init; }
}

/// <summary>Anulación de un ajuste por diferencia de cambio (F3-07).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AjusteDiferenciaCambioAnulacionPeticion
{
    /// <summary>
    /// Por qué se anula. Obligatorio por la misma razón que en el alta: el ajuste equivocado se
    /// corrige, no se borra (AGENTS.md §6), y corregido significa explicado.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Motivo { get; init; }
}

/// <summary>Ajuste por diferencia de cambio tal y como queda guardado.</summary>
public sealed record AjusteDiferenciaCambioRespuesta(
    Guid Id,
    Guid FacturaId,
    decimal Importe,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal TipoCambioValor,
    string Motivo,
    string? Ip,
    bool Vigente,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnuladoAt,
    Guid? AnuladoPor,
    string? MotivoAnulacion);

/// <summary>
/// Aplicación de un cobro a una factura concreta (F3-06) o a una fianza (F3-09, §3.11).
/// <para>
/// <b>Desde F3-09 exactamente uno de los dos destinos</b>, y el servicio lo comprueba: los dos, ninguno
/// o una factura repetida son 409. No son <c>[Required]</c> porque la regla depende del otro campo, y
/// un <c>[Required]</c> aquí daría un 400 que blames a la factura cuando lo que falta es la fianza.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CobroAsignacionPeticion
{
    /// <summary>Factura a la que se aplica este importe. Nulo cuando es una fianza.</summary>
    public Guid? FacturaId { get; init; }

    /// <summary>
    /// Fianza a la que se aplica este importe (F3-09, §3.11). Nulo cuando es una factura.
    /// </summary>
    public Guid? FianzaId { get; init; }

    /// <summary>
    /// Importe aplicado, en la moneda del destino (la de la factura o la de la fianza). Tiene que ser
    /// mayor que cero.
    /// </summary>
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

/// <summary>
/// Asignación tal y como queda guardada, vigente o no.
/// <para>
/// <b><paramref name="ImporteRecibido"/> y <paramref name="TipoCambioValor"/> vienen a null cuando el
/// cobro y la factura comparten moneda</b>, porque ahí no se aplicó ninguna conversión. Cuando la
/// factura es de otra moneda, el importe es su equivalente: <c>ImporteRecibido / TipoCambioValor</c>.
/// </para>
/// <para>
/// <b>Desde F3-09 los campos de la factura son nulos cuando lo que se pagó fue una fianza</b>, y
/// <paramref name="FianzaId"/> es el que viene informado. El destino es uno solo y por eso el de la
/// factura y el de la fianza no van en un objeto anidado: separarlos cuesta más de lo que ahorra.
/// </para>
/// </summary>
public sealed record CobroAsignacionRespuesta(
    Guid Id,
    Guid? FacturaId,
    Guid? FianzaId,
    int? FacturaNumero,
    DateOnly? FacturaVencimiento,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? FacturaMoneda,
    decimal Importe,
    decimal? ImporteRecibido,
    decimal? TipoCambioValor,
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

/// <summary>
/// Una factura sugerida por la FIFO, con lo que se le aplicaría y lo que le quedaría.
/// <para>
/// Desde F3-07 la FIFO también propone facturas de <b>otra moneda</b> que la del pago, con su
/// equivalente en la moneda recibida; por eso la línea lleva <paramref name="ImporteRecibido"/> y
/// <paramref name="TipoCambioValor"/>, que son null cuando la factura está en la moneda del pago.
/// </para>
/// <para>
/// <b>Desde F3-09 una línea puede ser de una fianza</b> (§3.11): entonces lo de la factura es nulo y
/// <paramref name="FianzaId"/> viene informado. La simulación dice lo mismo que el registro —ambos
/// comparten la planificación— así que quien ve aquí una línea de fianza sabe que al confirmar se va a
/// guardar.
/// </para>
/// </summary>
public sealed record CobroAsignacionSimulada(
    Guid? FacturaId,
    Guid? FianzaId,
    int? Numero,
    DateOnly? FechaVencimiento,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda MonedaDestino,
    decimal Importe,
    decimal? ImporteRecibido,
    decimal? TipoCambioValor,
    decimal PendienteAntes);

/// <summary>
/// Deuda pendiente del mismo contrato en una moneda distinta de la recibida que <b>esta propuesta no
/// cubre</b> (F3-07, §3.10.2).
/// <para>
/// Desde F3-07 la FIFO sí propone facturas de otra moneda, así que esto ya no es «la deuda que
/// ignora»: es «la deuda que queda después de aplicar este cobro». Se sigue devolviendo, y con el
/// mismo sentido, porque es lo que permite que la interfaz diga «este pago no cubre todo» en vez de
/// dejar a alguien creyendo que sí.
/// </para>
/// </summary>
public sealed record CobroDeudaOtraMoneda(
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Pendiente,
    int Facturas);