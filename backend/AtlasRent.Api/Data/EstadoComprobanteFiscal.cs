namespace AtlasRent.Api.Data;

/// <summary>
/// Estado de un comprobante fiscal (docs/ESPECIFICACION.md §3.7). Anulado no significa libre: el
/// número sigue ocupado para siempre (D28).
/// </summary>
public enum EstadoComprobanteFiscal
{
    Emitido = 1,
    Anulado = 2,
}