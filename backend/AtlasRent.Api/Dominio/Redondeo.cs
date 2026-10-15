namespace AtlasRent.Api.Dominio;

/// <summary>
/// Redondeo único del sistema (D27, AGENTS.md §6).
/// <para>
/// <c>decimal.Round</c> usa half-even por defecto, que NO es lo que quiere la empresa:
/// 0,005 se quedaría en 0,00. Aquí el redondeo es half-up, alejándose del cero en negativos.
/// </para>
/// </summary>
public static class Redondeo
{
    /// <summary>Half-up a 2 decimales: es el redondeo de un importe (D27).</summary>
    public static decimal Importe(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);

    /// <summary>Half-up a 6 decimales: es el redondeo de un tipo de cambio (<c>numeric(18,6)</c>).</summary>
    public static decimal TipoCambio(decimal tipoCambio) => Math.Round(tipoCambio, 6, MidpointRounding.AwayFromZero);

    /// <summary>Half-up a 4 decimales: porcentajes de ITBIS y retenciones (F1-08).</summary>
    public static decimal Porcentaje(decimal porcentaje) => Math.Round(porcentaje, 4, MidpointRounding.AwayFromZero);
}