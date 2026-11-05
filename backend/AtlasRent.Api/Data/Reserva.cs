using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Reserva: el asiento que deja el cliente sobre una unidad antes de que exista contrato
/// (docs/ESPECIFICACION.md §3.4).
/// <para>
/// <b>Reserva no es ocupación.</b> <see cref="ReservaDia"/> es el negocio de la reserva corta (qué
/// días pidió y a qué hora, que es lo que se ve en pantalla y lo que se factura); la ocupación es
/// el candado (<c>ocupacion_dia</c> / <c>ocupacion_rango</c>), que solo sabe día y tipo de
/// bloqueo, la escribe y la libera <c>ServicioDisponibilidad</c>, y puede haber más de una
/// ocupación viva para el mismo negocio. Cancelar una reserva deja sus
/// <see cref="ReservaDia"/> en su sitio y apaga su ocupación: el historial se conserva y el candado
/// se suelta.
/// </para>
/// <para>
/// Fila de negocio: nunca se borra (AGENTS.md §6). La baja es lógica con <see cref="Activo"/> y
/// estado <see cref="EstadoReserva.Cancelada"/>.
/// </para>
/// </summary>
public sealed class Reserva : IEntidadTrazada
{
    /// <summary>Unidad reservada. Obligatoria siempre: la reserva cuelga de la unidad, no al revés.</summary>
    public Guid UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    /// <summary>
    /// Cliente de la reserva. <see cref="ProspectoId"/> y este campo son excluyentes: el
    /// <c>CHECK</c> <c>(cliente_id IS NOT NULL) &lt;&gt; (prospecto_id IS NOT NULL)</c> lo exige
    /// también en la base de datos.
    /// </summary>
    public Guid? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>
    /// Prospecto que reserva sin ser cliente todavía (la especificación lo pide así: una reserva
    /// corta la puede pedir alguien que aún no es cliente).
    /// </summary>
    public Guid? ProspectoId { get; set; }

    public Prospecto? Prospecto { get; set; }

    public TipoReserva Tipo { get; set; }

    /// <summary>
    /// Fecha en que se hizo la reserva, en día de negocio (D16), no la hora del servidor.
    /// Para la corta no es lo mismo que <see cref="EntradaPrevista"/>: una reserva de dentro de
    /// tres semanas se hace hoy y entra dentro de tres semanas.
    /// </summary>
    public DateOnly FechaReserva { get; set; }

    /// <summary>
    /// Día en que entra el cliente. Obligatoria en las dos clases de reserva: es lo que se bloquea.
    /// En la mensual es además el inicio del rango abierto que se escribe en
    /// <c>ocupacion_rango</c> con <c>fecha_fin</c> nulo.
    /// </summary>
    public DateOnly EntradaPrevista { get; set; }

    /// <summary>
    /// Fecha de salida de la reserva corta. La especificación exige entrada y salida el mismo día,
    /// así que siempre vale <see cref="EntradaPrevista"/> o es nula; se guarda para que el dato
    /// quede escrito tal cual vino y para que la regla se pueda auditar.
    /// </summary>
    public DateOnly? SalidaPrevista { get; set; }

    /// <summary>Entrada real. La rellena la operación (F2-04), no la reserva.</summary>
    public DateOnly? EntradaReal { get; set; }

    /// <summary>Salida real. La rellena la operación (F2-04), no la reserva.</summary>
    public DateOnly? SalidaReal { get; set; }

    public EstadoReserva Estado { get; set; }

    /// <summary>
    /// Modalidad de la reserva: mensual, o medio día / día completo en la corta (D11). Una reserva
    /// corta tiene una sola modalidad para todos sus días, que es lo que hace falta para guardar un
    /// único <see cref="Importe"/> de tarifa.
    /// </summary>
    public ModalidadAlquiler Modalidad { get; set; }

    /// <summary>Moneda del importe y del anticipo (D7: una moneda por documento).</summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Importe de la reserva, como foto de la tarifa en el momento de reservarla. No es un precio
    /// vivo: si mañana sube la tarifa, esta reserva conserva lo que se le dijo al cliente.
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>
    /// Anticipo anotado (D10). <b>No es un cobro ni una factura</b>: no hay pasarela de pago, así
    /// que esto es una nota con número. El cobro se registra cuando se paga la factura, en F3-01,
    /// contra <c>cobro</c>. La razón está desarrollada en el servicio de alertas y en el
    /// comentario de la migración de <c>reserva</c>.
    /// </summary>
    public decimal? AnticipoImporte { get; set; }

    /// <summary>
    /// Nota del anticipo: qué se acordó y, si la reserva se cancela, si se devuelve
    /// o se retiene (D10). Texto libre, no una cuenta que cuadrar.
    /// </summary>
    public string? AnticipoNota { get; set; }

    /// <summary>Observaciones de negocio.</summary>
    public string? Observaciones { get; set; }

    /// <summary>Baja lógica. Una reserva cancelada sigue en la base de datos.</summary>
    public bool Activo { get; set; }

    /// <summary>Días de la reserva corta. Vacío en la mensual, que bloquea un rango.</summary>
    public List<ReservaDia> Dias { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}