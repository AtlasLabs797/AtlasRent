using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Qué deja un día del calendario como no disponible (docs/ESPECIFICACION.md §3.1, "Calendario de
/// disponibilidad").
/// <para>
/// No es un campo de la unidad ni de la reserva: es lo que el calendario <strong>calcula</strong>
/// leyendo <c>ocupacion_rango</c> y <c>ocupacion_dia</c> (F2-01). Los valores 2 a 5 son
/// <see cref="OrigenOcupacion"/> en su orden y los dos últimos son cosas que no son bloqueos:
/// </para>
/// <list type="bullet">
/// <item><see cref="EstadoManual"/> es el estado manual de la unidad (mantenimiento o fuera de
/// servicio), que no depende de ninguna fecha y no sale de ninguna tabla de ocupación.</item>
/// <item><see cref="Libre"/> es "nada bloquea este día".</item>
/// </list>
/// <para>
/// Enum mapeado a entero, como el resto del dominio del proyecto. El orden es el de la regla de
/// desempate de <c>ServicioCalendario</c> y no se reordena: los valores se guardan en la respuesta
/// de la API y en la caché del cliente.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoDiaCalendario
{
    /// <summary>Nada bloquea este día: la unidad está libre.</summary>
    Libre = 1,

    /// <summary>Un contrato vigente, que ocupa la unidad en un rango.</summary>
    Contrato = 2,

    /// <summary>Una reserva mensual, que ocupa la unidad en un rango abierto desde su entrada.</summary>
    ReservaMensual = 3,

    /// <summary>Una reserva corta, que ocupa un medio día o el día entero de <c>ocupacion_dia</c>.</summary>
    ReservaCorta = 4,

    /// <summary>Un bloqueo de mantenimiento con fecha.</summary>
    Mantenimiento = 5,

    /// <summary>
    /// El estado manual de la unidad: está en mantenimiento o fuera de servicio. No sale de
    /// <c>ocupacion_*</c> sino de <c>unidad.estado_manual</c>, y por eso va el último: no explica un
    /// día concreto, sino todos los del rango.
    /// </summary>
    EstadoManual = 6,
}