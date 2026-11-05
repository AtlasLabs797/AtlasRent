using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Un día reservado de una reserva corta: fecha, hora orientativa opcional y tipo
/// (<see cref="TipoOcupacionDia.MedioDia"/> o <see cref="TipoOcupacionDia.DiaCompleto"/>).
/// <para>
/// <b>Esto es negocio, no bloqueo.</b> <c>ocupacion_dia</c> es el candado que escribe
/// <c>ServicioDisponibilidad</c>, y solo sabe día y tipo; aquí además está la hora orientativa, que ni el
/// bloqueo ni la ocupación necesitan. Por eso las dos cosas no se pueden fusionar: al cancelar una
/// reserva se apagan sus filas de <c>ocupacion_dia</c> y estas siguen ahí.
/// </para>
/// <para>
/// Una misma fecha puede repetirse en la reserva: dos personas en la misma unidad y el mismo día son
/// dos medios días, y el <c>(reserva_id, fecha)</c> no es único por eso.
/// </para>
/// </summary>
public sealed class ReservaDia : IEntidadTrazada
{
    public Guid ReservaId { get; set; }

    public Reserva? Reserva { get; set; }

    /// <summary>Día reservado.</summary>
    public DateOnly Fecha { get; set; }

    /// <summary>
    /// Hora orientativa en texto libre y opcional (D11, §3.4: "sin franjas fijas"). No decide nada:
    /// la capacidad sale del <see cref="Tipo"/>.
    /// </summary>
    public string? HoraOrientativa { get; set; }

    /// <summary>Tipo de la jornada de ese día: el de la modalidad de la reserva.</summary>
    public TipoOcupacionDia Tipo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}