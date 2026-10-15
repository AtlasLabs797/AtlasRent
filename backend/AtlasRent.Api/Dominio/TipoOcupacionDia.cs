using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Cuánto ocupa una fila de <c>ocupacion_dia</c> el día que bloquea (docs/ESPECIFICACION.md §3.4):
/// la unidad admite <strong>2 medios días o 1 día completo</strong>.
/// <para>
/// Enum mapeado a entero, como el resto de enums del dominio. El trigger de capacidad de
/// PostgreSQL compara contra estos números (1 y 2): si se reordenan, hay que actualizar el SQL de
/// la migración <c>DisponibilidadOcupacion</c>, que EF no genera.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoOcupacionDia
{
    /// <summary>Media unidad del día. Caben dos el mismo día.</summary>
    MedioDia = 1,

    /// <summary>El día entero. Uno solo ya no cabe nada más.</summary>
    DiaCompleto = 2,
}