namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado en que se encuentra un elemento del inventario al revisar la salida del cliente
/// (F2-08, docs/ESPECIFICACION.md §3.6).
/// <para>
/// Los valores son explícitos y no van por su posición: el estado acaba en una columna de la base y
/// en la revisión firmada, y reordenar el enum un día no puede querer decir que la revisión firmada
/// ayer con "dañado" hoy se lea como "ausente".
/// </para>
/// </summary>
public enum EstadoRevisionInventario
{
    /// <summary>Está y está como debe. No lleva nota obligatoria.</summary>
    Presente = 1,

    /// <summary>No está. No es lo mismo que roto: falta, y el importe del faltante se liquidará.</summary>
    Ausente = 2,

    /// <summary>Está, pero roto o inservible. Se liquida aparte del que falta.</summary>
    Danado = 3,
}