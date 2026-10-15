namespace AtlasRent.Api.Dominio;

/// <summary>
/// Tipo de serie de numeración (docs/ESPECIFICACION.md §3.7). <c>Historica</c> es solo para
/// importación: guarda los números que ya existen en otro sistema.
/// </summary>
public enum TipoSerie
{
    Normal = 1,
    Proforma = 2,
    Fiscal = 3,
    Historica = 4,
}