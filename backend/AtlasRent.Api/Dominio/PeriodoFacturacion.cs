namespace AtlasRent.Api.Dominio;

/// <summary>
/// El mes natural que una factura periódica cubre (F3-05, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Existe como tipo y no como dos <c>DateOnly</c> sueltos porque el periodo no es un dato que se
/// escriba: sale del día que se pide facturar. Y las tres reglas que dependen de él —cuántos días
/// tiene el mes, qué días se facturan y qué día vence— son aritmética de calendario, no de
/// negocio, así que viven aquí y no en el servicio: es lo que las hace comprobables sin base de
/// datos.
/// </para>
/// <para>
/// <b>Los días son inclusivos.</b> Un contrato del 15 al 31 de enero factura 17 días, no 16: el
/// día en que empieza a ser suyo se factura.
/// </para>
/// </summary>
/// <param name="Inicio">Primer día del mes natural.</param>
/// <param name="Fin">Último día del mes natural.</param>
public sealed record PeriodoFacturacion(DateOnly Inicio, DateOnly Fin)
{
    /// <summary>
    /// Días del mes natural, que son los que dividen el prorrateo (§3.8). En un bisiesto da 29, y
    /// por eso el divisor sale de <see cref="Fin"/> y no de un número fijo.
    /// </summary>
    public int DiasDelMes => Fin.Day;

    /// <summary>El mes natural del día que se pase, sea el día que sea dentro de él.</summary>
    public static PeriodoFacturacion DelMes(DateOnly dia) => DelMes(dia.Year, dia.Month);

    /// <summary>El mes natural del año y el mes dados.</summary>
    public static PeriodoFacturacion DelMes(int anio, int mes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mes, 12);

        return new PeriodoFacturacion(new DateOnly(anio, mes, 1), new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes)));
    }

    /// <summary>
    /// Días facturables en <c>[desde, hasta)</c>: el inicio es inclusivo y el fin exclusivo, como
    /// <c>FechaFinVigente</c> y la fecha de baja (el día del fin ya no es del inquilino, BUG-024).
    /// Recortados al periodo: un contrato que empieza antes del mes o acaba después solo factura lo que cae
    /// dentro. Cero si el tramo no toca el periodo, que es lo que hace que ese contrato no sea
    /// candidato.
    /// </summary>
    public int DiasFacturables(DateOnly desde, DateOnly hasta)
    {
        var primero = desde > Inicio ? desde : Inicio;
        var finExclusivo = Fin.AddDays(1);
        var ultimo = hasta < finExclusivo ? hasta : finExclusivo;

        return ultimo <= primero ? 0 : ultimo.DayNumber - primero.DayNumber;
    }

    /// <summary>
    /// Importe de un mes completo: la renta íntegra, sin pasar por <see cref="Prorrateo"/>. Dividir y
    /// multiplicar por los mismos días no siempre vuelve a dar la renta exacta en <see cref="decimal"/>
    /// (100 / 28 * 28 no es 100 en la precisión de <c>decimal</c>), y un céntimo de diferencia en
    /// un mes entero no es un prorrateo: es un error de cálculo.
    /// </summary>
    public decimal ImporteDeMesCompleto(decimal renta) => renta;

    /// <summary>
    /// Importe del tramo facturado. Con el mes entero devuelve la renta íntegra; con menos días,
    /// <see cref="Prorrateo"/> (media parte hacia arriba, D27).
    /// </summary>
    public decimal Importe(decimal renta, int diasFacturables) =>
        diasFacturables >= DiasDelMes
            ? ImporteDeMesCompleto(renta)
            : Prorrateo.Mensual(renta, DiasDelMes, diasFacturables);

    /// <summary>
    /// Vencimiento de la factura: el día de pago del contrato dentro del periodo facturado.
    /// <para>
    /// Si ese día ya pasó al facturar, la factura vence en el <b>siguiente ciclo</b> —el mismo día
    /// del mes siguiente— en vez de nacer vencida. Es la misma regla que la de los suministros
    /// (§3.8) y la que decide el orquestador para F3-05: facturar un mes tarde no puede entregar un
    /// documento que nasce con mora.
    /// </para>
    /// <para>
    /// El día se recorta al final del mes: un contrato con día de pago 31 en un mes de 30 días vence
    /// el último día de ese mes, y en el siguiente el 31 sí existe.
    /// </para>
    /// </summary>
    public DateOnly Vencimiento(int diaPago, DateOnly hoy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(diaPago, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(diaPago, 31);

        var dia = Math.Min(diaPago, DiasDelMes);
        var vencimiento = new DateOnly(Inicio.Year, Inicio.Month, dia);

        // TODO(A#): facturar con retraso mueve el vencimiento al siguiente ciclo en vez de al día de
        // pago del periodo facturado; ¿es lo que quiere la casa o debe nacer vencida? (BUG-052).
        return vencimiento >= hoy
            ? vencimiento
            : DelMesDelSiguienteCiclo(dia);
    }

    /// <summary>Mismo día del mes siguiente, recortado a los días que ese mes tenga.</summary>
    private DateOnly DelMesDelSiguienteCiclo(int dia)
    {
        var anio = Inicio.Month == 12 ? Inicio.Year + 1 : Inicio.Year;
        var mes = Inicio.Month == 12 ? 1 : Inicio.Month + 1;

        return new DateOnly(anio, mes, Math.Min(dia, DateTime.DaysInMonth(anio, mes)));
    }
}