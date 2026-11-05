using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Tipo de la unidad: lo alquilable (docs/ESPECIFICACION.md §3.1). La aplicación trata todo lo
/// alquilable como una unidad, llámese recurso en pantalla.
/// <para>
/// TODO(A#): la especificación no enumera los tipos. Esta lista cubre los espacios que se alquilan
/// en la casa; lo que falte se añade al final.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoUnidad
{
    Oficina = 1,
    Sala = 2,
    Apartamento = 3,
    Local = 4,
    Bodega = 5,
    Terreno = 6,
    Otro = 7,
}