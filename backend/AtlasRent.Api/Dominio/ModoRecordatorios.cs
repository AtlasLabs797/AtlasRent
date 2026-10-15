namespace AtlasRent.Api.Dominio;

/// <summary>
/// Interruptor global de recordatorios (docs/ESPECIFICACION.md §3.14, D12). Es una configuración:
/// qué modo nace por defecto lo decide el administrador, y aquí solo está el vocabulario.
/// </summary>
public enum ModoRecordatorios
{
    Apagado = 1,
    Manual = 2,
    Automatico = 3,
}