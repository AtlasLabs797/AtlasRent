using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Categoría del documento de propiedad (docs/ESPECIFICACION.md §3.1): compraventa, título y
/// otros. Los tres valores que da la especificación, sin añadir ninguno.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CategoriaDocumento
{
    Compraventa = 1,
    Titulo = 2,
    Otros = 3,
}