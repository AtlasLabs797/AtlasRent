using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Qué fila de negocio deja la unidad bloqueada en <c>ocupacion_rango</c> o en
/// <c>ocupacion_dia</c> (docs/ESPECIFICACION.md §3.4 y §3.5).
/// <para>
/// Enum mapeado a entero, como el resto de enums del dominio. Ojo al añadir o reordenar valores:
/// los triggers de PostgreSQL de la migración <c>DisponibilidadOcupacion</c> compares con estos
/// números, y ese SQL no lo genera EF.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrigenOcupacion
{
    /// <summary>Un contrato ocupa la unidad en un rango de fechas.</summary>
    Contrato = 1,

    /// <summary>Una reserva mensual ocupa la unidad desde su entrada prevista.</summary>
    ReservaMensual = 2,

    /// <summary>Una reserva corta ocupa días sueltos, de medio día o completos.</summary>
    ReservaCorta = 3,

    /// <summary>Bloqueo de mantenimiento: no es una reserva, pero tampoco deja usar la unidad.</summary>
    Mantenimiento = 4,
}