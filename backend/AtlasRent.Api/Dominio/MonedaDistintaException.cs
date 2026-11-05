namespace AtlasRent.Api.Dominio;

/// <summary>Error de dominio al mezclar importes de monedas distintas (D7, AGENTS.md §6).</summary>
public sealed class MonedaDistintaException(Moneda una, Moneda otra)
    : InvalidOperationException($"No se pueden combinar importes en {una} y {otra}. Cada documento tiene una sola moneda.")
{
    public Moneda Una { get; } = una;

    public Moneda Otra { get; } = otra;
}