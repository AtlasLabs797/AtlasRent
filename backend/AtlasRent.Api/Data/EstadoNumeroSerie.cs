namespace AtlasRent.Api.Data;

/// <summary>
/// Estado de un número emitido (docs/ESPECIFICACION.md §3.7). Anulado no significa libre: el número
/// queda ocupado para siempre (D28).
/// </summary>
public enum EstadoNumeroSerie
{
    Emitido = 1,
    Anulado = 2,
}