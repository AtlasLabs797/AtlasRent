using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Cobro: dinero que entró por una factura (F3-06, docs/ESPECIFICACION.md §3.10.1).
/// <para>
/// <b>Nada de esto se borra nunca</b> (AGENTS.md §6). Un cobro es un movimiento de dinero: lo que se
/// corrige es anularlo con motivo, y sus asignaciones dejan de contar sin desaparecer de la tabla.
/// </para>
/// <para>
/// <b>El importe recibido y su tipo de cambio son los del cobro, no los de la factura.</b> Un cobro en
/// DOP puede pagar facturas en USD (D8, §3.10.2): por eso el cambio se congela aquí al registrar (D9) y
/// las asignaciones guardan el importe en la moneda de su factura.
/// <para>
/// <b>Este cambio es siempre el del día</b>, aunque la asignación guarde el que el gerente fijó por
/// petición (§3.10.2, F3-07): el del día es el dato de referencia con el que se justifican los
/// informes, y pisarlo con el aplicado haría que un informe justificara un cobro con un cambio que no
/// fue el que se usó.
/// </para>
/// </para>
/// <para>
/// El <see cref="ContratoId"/> es nulo a propósito: un cobro sin contrato es legal (§3.10.1 no
/// exige contrato), y en ese caso el excedente no genera saldo a favor —el crédito es del
/// contrato— y se queda en este cobro. Ese camino lo cubre la ayuda de pruebas y el propio
/// servicio, que rechazan el uso de crédito sin contrato.
/// </para>
/// </summary>
public sealed class Cobro : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <summary>Cliente al que se cobra. Obligatorio: todo cobro es de alguien.</summary>
    public Guid ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>
    /// Contrato de origen. Nulo en un cobro suelto: el excedente de ese cobro no genera saldo a
    /// favor, porque el saldo a favor es del contrato (§3.10.1).
    /// </summary>
    public Guid? ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Día en que el dinero entró, en día de negocio. Lo escribe quien registra el cobro.</summary>
    public DateOnly FechaEfectiva { get; set; }

    /// <summary>Moneda en la que se recibió el dinero (D8). Es la del importe recibido.</summary>
    public Moneda MonedaRecibida { get; set; }

    /// <summary>Importe recibido, en la moneda recibida. Siempre mayor que cero.</summary>
    public decimal ImporteRecibido { get; set; }

    /// <summary>Forma de pago configurada (§3.10.1). Obligatoria.</summary>
    public Guid FormaPagoId { get; set; }

    public FormaPago? FormaPago { get; set; }

    /// <summary>
    /// Referencia que da el banco o el cliente (transferencia, cheque). Texto libre y opcional:
    /// no se valida ni se compara con nada, es para que quien lo busque lo encuentre.
    /// </summary>
    public string? Referencia { get; set; }

    /// <summary>Cuenta en la que entró el dinero. Obligatoria: el cobro dice dónde se hizo.</summary>
    public Guid CuentaReceptoraId { get; set; }

    public CuentaReceptora? CuentaReceptora { get; set; }

    /// <summary>
    /// Tipo de cambio congelado al registrar (D9), <c>numeric(18,6)</c>. Solo para informes
    /// consolidados: no convierte ni un solo importe de este cobro.
    /// <para>
    /// Es obligatorio para toda moneda que no sea el dólar, por la misma razón que lo es en la
    /// emisión de facturas (F3-02): sin cambio congelado el movimiento no se puede consolidar en
    /// dólares. Para USD vale 1 y no se guarda fila de <c>tipo_cambio</c> (F3-01).
    /// </para>
    /// </summary>
    public decimal TipoCambioValor { get; set; }

    /// <summary>Día de negocio al que corresponde el cambio congelado.</summary>
    public DateOnly TipoCambioFecha { get; set; }

    /// <summary>
    /// Fila de <c>tipo_cambio</c> (F3-01) de la que salió el valor, para saber de dónde vino. Nulo
    /// en dólares, que no se guarda.
    /// </summary>
    public Guid? TipoCambioId { get; set; }

    public TipoCambio? TipoCambio { get; set; }

    /// <summary>
    /// Importe de crédito ya cobrado que este pago aplica a las facturas del contrato. Sale de
    /// <c>saldo_a_favor</c> y se descuenta de ahí en la misma transacción.
    /// <para>
    /// <b>El crédito se usa de forma explícita, nunca sola.</b> Aplicarlo por las reglas al emitir
    /// una factura sería inventar una regla que la especificación no dice (§3.10.1 no dice a qué
    /// factura se aplica el saldo a favor).
    /// </para>
    /// <para>
    /// TODO(A#): la especificación no dice si el crédito se descuenta solo de la primera factura
    /// del contrato o si lo elige quien registra el cobro. Aquí lo elige quien registra, con
    /// <c>usarSaldoAFavor</c>; la respuesta de la simulación dice el crédito disponible para que la
    /// interfaz pueda proponerlo.
    /// </para>
    /// </summary>
    public decimal SaldoFavorUsado { get; set; }

    /// <summary>
    /// Lo que queda del cobro después de asignarlo: importe recibido más crédito aplicado menos
    /// asignaciones. Sin contrato no sale a <c>saldo_a_favor</c> y se queda aquí.
    /// </summary>
    public decimal Excedente { get; set; }

    /// <summary>
    /// Parte del cobro que se da por devuelta porque la factura que pagaba se anuló (BUG-020), en la
    /// moneda recibida. Decisión del responsable (26/10/2026): nada de saldo a favor, algo simple; si
    /// se anula A, A se cancela y su parte queda marcada como devuelta.
    /// <para>
    /// Invariante: <c>ImporteRecibido + SaldoFavorUsado = asignado vigente + Excedente +
    /// ImporteDevuelto</c>. El detalle (fecha, quién y motivo) vive en la asignación anulada
    /// (<see cref="CobroAsignacion.AnuladaAt"/>, <see cref="CobroAsignacion.AnuladaPor"/>,
    /// <see cref="CobroAsignacion.MotivoAnulacion"/>), así que el cobro solo guarda el total.
    /// </para>
    /// </summary>
    public decimal ImporteDevuelto { get; set; }

    public EstadoCobro Estado { get; set; }

    /// <summary>Cuándo se anuló. Nulo mientras el cobro está registrado.</summary>
    public DateTimeOffset? AnuladoAt { get; set; }

    /// <summary>Quién lo anuló.</summary>
    public Guid? AnuladoPor { get; set; }

    /// <summary>Por qué se anuló. Obligatorio: una corrección de dinero se explica siempre.</summary>
    public string? MotivoAnulacion { get; set; }

    /// <summary>
    /// A qué facturas se aplica este cobro. Las filas no se borran al anular: quedan con
    /// <see cref="CobroAsignacion.Vigente"/> en falso y dejan de contar.
    /// </summary>
    public List<CobroAsignacion> Asignaciones { get; set; } = [];

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}