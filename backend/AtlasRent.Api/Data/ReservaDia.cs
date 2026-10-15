using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Un día reservado de una reserva corta: fecha, hora de entrada, hora de salida y el tipo que se
/// tipo derivado de esas horas (<see cref="TipoOcupacionDia.MedioDia"/> si son 4 horas exactas,
/// <see cref="TipoOcupacionDia.DiaCompleto"/> si son la jornada entera).
/// <para>
/// <b>Esto es negocio, no bloqueo.</b> <c>ocupacion_dia</c> es el candado que escribe
/// <c>ServicioDisponibilidad</c>, y solo sabe día y tipo; aquí además están las horas, que ni el
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

    /// <summary>Hora de entrada acordada.</summary>
    public TimeOnly HoraEntrada { get; set; }

    /// <summary>
    /// Hora de salida acordada. Siempre el mismo día que la entrada (docs/ESPECIFICACION.md §3.4);
    /// el servicio rechaza cualquier otra cosa antes de llegar aquí.
    /// </summary>
    public TimeOnly HoraSalida { get; set; }

    /// <summary>Tipo de la jornada de ese día, derivado de las horas.</summary>
    public TipoOcupacionDia Tipo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}