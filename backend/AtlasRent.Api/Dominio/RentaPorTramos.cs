namespace AtlasRent.Api.Dominio;

/// <summary>Incremento de renta pactado que la facturación mira para saber qué renta toca cada día (BUG-024).</summary>
/// <param name="RentaAntes">Renta justo antes de aplicarlo; solo la tienen los ya aplicados.</param>
public readonly record struct IncrementoPactado(
    DateOnly FechaVigencia,
    TipoIncremento Tipo,
    decimal? Porcentaje,
    decimal? Importe,
    bool Aplicado,
    decimal? RentaAntes);

/// <summary>Tramo de fechas <c>[Desde, Hasta)</c> con una renta constante.</summary>
public readonly record struct TramoDeRenta(DateOnly Desde, DateOnly Hasta, decimal Renta);

/// <summary>
/// Renta que corresponde a cada día cuando hay incrementos con fecha. El incremento manda desde su
/// fecha de vigencia, no desde la renovación que lo escribe en el contrato: la factura del periodo
/// que contiene esa fecha usa la renta nueva y prorratea los dos tramos.
/// </summary>
public static class RentaPorTramos
{
    /// <summary>Divisor de un tanto por ciento de incremento pactado; no es una cifra fiscal.</summary>
    private const decimal PorCien = 100m;

    /// <summary>Renta tras un incremento: porcentaje sobre la vigente o importe fijo, medio hacia arriba.</summary>
    public static decimal Incrementar(TipoIncremento tipo, decimal? porcentaje, decimal? importe, decimal renta) =>
        tipo switch
        {
            TipoIncremento.Porcentaje => Redondeo.Importe(renta * (1 + (porcentaje ?? 0) / PorCien)),
            _ => Redondeo.Importe(renta + (importe ?? 0)),
        };

    /// <summary>
    /// Renta vigente un día dado. <paramref name="rentaActual"/> es la guardada en el contrato, que
    /// ya incluye los incrementos aplicados; los no aplicados se encadenan encima de ella.
    /// <paramref name="incrementos"/> viene sin cancelados y ordenado por fecha de vigencia.
    /// </summary>
    public static decimal RentaEn(DateOnly dia, decimal rentaActual, IReadOnlyList<IncrementoPactado> incrementos)
    {
        var renta = rentaActual;
        decimal? rentaEnElDia = null;

        foreach (var i in incrementos.Where(i => !i.Aplicado))
        {
            renta = Incrementar(i.Tipo, i.Porcentaje, i.Importe, renta);
            if (i.FechaVigencia <= dia)
            {
                rentaEnElDia = renta;
            }
        }

        if (rentaEnElDia is { } r)
        {
            return r;
        }

        var aplicadoPosterior = incrementos.Where(i => i.Aplicado && i.FechaVigencia > dia).Cast<IncrementoPactado?>().FirstOrDefault();

        return aplicadoPosterior?.RentaAntes ?? rentaActual;
    }

    /// <summary>Tramos de <c>[desde, hasta)</c> partidos en cada fecha de vigencia que cae dentro.</summary>
    public static IReadOnlyList<TramoDeRenta> Tramos(
        DateOnly desde,
        DateOnly hasta,
        decimal rentaActual,
        IReadOnlyList<IncrementoPactado> incrementos)
    {
        var cortes = incrementos
            .Select(i => i.FechaVigencia)
            .Where(f => f > desde && f < hasta)
            .Distinct()
            .Order()
            .Prepend(desde)
            .Append(hasta)
            .ToList();

        var tramos = new List<TramoDeRenta>();
        for (var k = 0; k < cortes.Count - 1; k++)
        {
            tramos.Add(new TramoDeRenta(cortes[k], cortes[k + 1], RentaEn(cortes[k], rentaActual, incrementos)));
        }

        return tramos;
    }
}
