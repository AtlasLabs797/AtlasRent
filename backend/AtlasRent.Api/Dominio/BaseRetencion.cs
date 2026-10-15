namespace AtlasRent.Api.Dominio;

/// <summary>
/// Sobre qué importe se calcula una retención (docs/ESPECIFICACION.md §3.9). Es una elección de la
/// empresa, no una convención del sistema: por eso es un dato y no una regla del cálculo.
/// </summary>
public enum BaseRetencion
{
    Subtotal = 1,
    Itbis = 2,
    Total = 3,
}