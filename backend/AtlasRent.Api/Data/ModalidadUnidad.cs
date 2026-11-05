using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Modalidad que una unidad tiene permitida (docs/ESPECIFICACION.md §3.1). Es una tabla aparte y no
/// una columna booleana por modalidad porque el conjunto de modalidades permitidas es la regla que
/// no se puede violar: una tarifa o un contrato sobre una modalidad que no está aquí no es válido.
/// <para>
/// Sin columnas de trazabilidad: la fila no tiene vida propia, se crea y se borra con su unidad.
/// </para>
/// </summary>
public sealed class ModalidadUnidad
{
    public Guid UnidadId { get; set; }

    public ModalidadAlquiler Modalidad { get; set; }

    public Unidad Unidad { get; set; } = null!;
}