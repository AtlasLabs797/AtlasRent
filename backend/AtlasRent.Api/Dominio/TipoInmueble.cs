using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Tipo del inmueble: el edificio o la villa (docs/ESPECIFICACION.md §3.1).
/// <para>
/// La especificación pide "tipo" sin enumerarlo. Estos son los valores habituales del alquiler en
/// República Dominicana, más <see cref="Otro"/> para lo que no encaje.
/// </para>
/// <para>
/// TODO(A#): confirmar la lista con el responsable. Cambiarla no rompe datos: se añade el valor al
/// final y el resto sigue leyéndose.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoInmueble
{
    Edificio = 1,
    Villa = 2,
    Local = 3,
    Terreno = 4,
    Otro = 5,
}