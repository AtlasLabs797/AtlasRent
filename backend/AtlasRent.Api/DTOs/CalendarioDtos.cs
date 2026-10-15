using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Calendario de disponibilidad (docs/ESPECIFICACION.md §3.1): un día de una unidad dentro del rango
/// pedido, con lo que hay en él.
/// <para>
/// Siempre es una lista de unidades, aunque se pida una sola: así el frontend pinta el mismo
/// componente para el detalle de una unidad y para el inmueble entero, sin dos formas de respuesta.
/// Los cuatro enums que aparecen aquí ya llevan su <c>JsonStringEnumConverter</c> a nivel de tipo,
/// así que en la respuesta salen por su nombre ("Contrato", "MedioDia", "Mantenimiento").
/// </para>
/// </summary>
public sealed record CalendarioRespuesta(
    IReadOnlyList<CalendarioUnidadRespuesta> Unidades);

/// <summary>
/// El calendario de una unidad. <see cref="EstadoManual"/> es el estado a mano que alguien le puso
/// a la unidad, que es distinto de si está ocupada: "ocupada" y "reservada" se calculan por fechas
/// y no son ningún campo (docs/ESPECIFICACION.md §3.1, AGENTS.md §6).
/// <para>
/// <see cref="Desde"/> y <see cref="Hasta"/> son <strong>ambos incluidos</strong>, a diferencia del
/// rango de <c>ocupacion_rango</c>, que es <c>[inicio, fin)</c>: quien pide el calendario piensa en
/// "del 1 al 31", no en "del 1 al 30".
/// </para>
/// </summary>
public sealed record CalendarioUnidadRespuesta(
    Guid UnidadId,
    string Unidad,
    Guid InmuebleId,
    string Inmueble,
    EstadoUnidad EstadoManual,
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyList<CalendarioDiaRespuesta> Dias);

/// <summary>
/// Un día del rango, exista lo que exista en él: un día libre sale igual que uno con contrato, para
/// que la pantalla tenga siempre las mismas casillas.
/// </summary>
public sealed record CalendarioDiaRespuesta(
    DateOnly Fecha,

    /// <summary>
    /// Qué manda en ese día, después de aplicar la regla de desempate del servicio. Los demás
    /// motivos, si los hay, siguen en <see cref="Ocupaciones"/>.
    /// </summary>
    TipoDiaCalendario Tipo,

    /// <summary>Si ese día se puede pintar como libre. Es <c>tipo == Libre</c> y no otra cosa.</summary>
    bool Disponible,

    /// <summary>El motivo ya escrito, para que el frontend no tenga que deducirlo.</summary>
    string Motivo,

    /// <summary>
    /// Todo lo que hay en ese día, no solo lo que manda. Un día con dos medios días trae las dos
    /// entradas: un calendario que se come un medio día no puede pintar el día bien.
    /// </summary>
    IReadOnlyList<CalendarioOcupacionRespuesta> Ocupaciones);

/// <summary>
/// Un bloqueo concreto del día. Viene de <c>ocupacion_rango</c> (un contrato o una reserva mensual)
/// o de <c>ocupacion_dia</c> (una reserva corta o un mantenimiento con fecha).
/// <para>
/// <see cref="Origen"/> y <see cref="OrigenId"/> son la fila de negocio que dejó el bloqueo. No se
/// leen ni el contrato ni la reserva para nada: aquí solo se copia lo que ya está en la tabla de
/// ocupación, que es la fuente de verdad de la disponibilidad (F2-01).
/// </para>
/// </summary>
public sealed record CalendarioOcupacionRespuesta(
    OrigenOcupacion Origen,
    Guid OrigenId,

    /// <summary>Medio día o día completo. Nulo cuando el bloqueo es un rango.</summary>
    TipoOcupacionDia? TipoDia,

    /// <summary>
    /// Primer día libre del rango, excluido, tal y como está en <c>ocupacion_rango.fecha_fin</c>.
    /// Nulo cuando el rango es abierto (una reserva mensual no tiene fin hasta que haya contrato) o
    /// cuando el bloqueo es un día suelto.
    /// </summary>
    DateOnly? FinExclusivo);