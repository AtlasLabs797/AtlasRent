namespace AtlasRent.Api.Dominio;

/// <summary>
/// Importe con su moneda (AGENTS.md §6: <c>decimal</c>, nunca <c>double</c>/<c>float</c>;
/// <c>numeric(18,2)</c> en base de datos).
/// <para>
/// El importe se redondea a dos decimales al construirse, de modo que ningún valor con
/// centimas ocultas llega a la base de datos ni a una suma posterior.
/// </para>
/// <para>
/// Sumar o comparar importes de monedas distintas es un error, no una conversión (D7).
/// </para>
/// </summary>
public readonly record struct Dinero
{
    public Dinero(decimal importe, Moneda moneda)
    {
        Importe = Redondeo.Importe(importe);
        Moneda = moneda;
    }

    public decimal Importe { get; }

    public Moneda Moneda { get; }

    public static Dinero Cero(Moneda moneda) => new(0m, moneda);

    public Dinero Sumar(Dinero otro)
    {
        ExigirMismaMoneda(otro);
        return new Dinero(Importe + otro.Importe, Moneda);
    }

    public Dinero Sumar(decimal importe) => new(Importe + importe, Moneda);

    /// <summary>Resta sin dejar saldo negativo: un importe negativo no es un saldo.</summary>
    public Dinero Restar(Dinero otro)
    {
        ExigirMismaMoneda(otro);
        return new Dinero(Math.Max(0m, Importe - otro.Importe), Moneda);
    }

    public bool MayorQue(Dinero otro)
    {
        ExigirMismaMoneda(otro);
        return Importe > otro.Importe;
    }

    public bool MenorQue(Dinero otro)
    {
        ExigirMismaMoneda(otro);
        return Importe < otro.Importe;
    }

    public bool IgualA(Dinero otro)
    {
        ExigirMismaMoneda(otro);
        return Importe == otro.Importe;
    }

    public bool EsCero => Importe == 0m;

    public static Dinero operator +(Dinero a, Dinero b) => a.Sumar(b);

    public static Dinero operator -(Dinero a, Dinero b) => a.Restar(b);

    public override string ToString() => $"{Importe:0.00} {Moneda}";

    private void ExigirMismaMoneda(Dinero otro)
    {
        if (Moneda != otro.Moneda)
        {
            throw new MonedaDistintaException(Moneda, otro.Moneda);
        }
    }
}