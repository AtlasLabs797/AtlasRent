namespace AtlasRent.Api.Dominio;

/// <summary>
/// Prorrateo de renta por días facturables (DOC_TECNICA §5, ESPECIFICACION 3.8).
/// Fórmula fija: <c>renta / díasDelMes × díasFacturables</c>, días inclusivos.
/// </summary>
public static class Prorrateo
{
    /// <summary>Días del mes natural. Necesario para que el prorrateo dependa del mes real.</summary>
    public static int DiasDelMes(int anio, int mes) => DateTime.DaysInMonth(anio, mes);

    /// <summary>
    /// Importe de un periodo parcial. <paramref name="diasFacturables"/> va de 1 a
    /// <paramref name="diasDelMes"/> inclusive.
    /// </summary>
    public static decimal Mensual(decimal renta, int diasDelMes, int diasFacturables)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(diasDelMes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(diasFacturables, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(diasFacturables, diasDelMes);

        return Redondeo.Importe(renta / diasDelMes * diasFacturables);
    }
}