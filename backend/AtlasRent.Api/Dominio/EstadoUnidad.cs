using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado <strong>manual</strong> de la unidad (docs/ESPECIFICACION.md §3.1): lo que alguien
/// decide a mano y que se puede cambiar sin que nadie lo toque.
/// <para>
/// <strong>No es ocupación.</strong> "Ocupada" y "Reservada" se calculan por fechas sobre contratos
/// y reservas (AGENTS.md §6: la disponibilidad se calcula en la base de datos, nunca un campo
/// "ocupado"), y llegan en una fase posterior. Mantenimiento y fuera de servicio tampoco dependen
/// de fechas: cortan el alquiler aunque la unidad esté libre.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoUnidad
{
    Disponible = 1,
    Mantenimiento = 2,
    FueraDeServicio = 3,
}