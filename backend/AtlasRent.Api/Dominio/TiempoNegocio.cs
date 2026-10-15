namespace AtlasRent.Api.Dominio;

/// <summary>
/// Fechas de negocio y zona horaria de la empresa (AGENTS.md §6, DOC_TECNICA §5).
/// <para>
/// Las fechas de negocio son <see cref="DateOnly"/> y las marcas de tiempo
/// <see cref="DateTimeOffset"/> en UTC. "Hoy" para Atlas Rent es hoy en
/// America/Santo_Domingo (UTC−4 todo el año, sin horario de verano), que puede ser un
/// día distinto al del servidor UTC.
/// </para>
/// </summary>
public static class TiempoNegocio
{
    public const string ZonaPorDefecto = "America/Santo_Domingo";

    /// <summary>
    /// Resuelve un identificador de zona. Acepta el identificador IANA y, por si la máquina
    /// no lo reconoce, su equivalente de Windows.
    /// </summary>
    public static TimeZoneInfo BuscarZona(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id == ZonaPorDefecto)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
        }
    }

    public static TimeZoneInfo Zona => BuscarZona(ZonaPorDefecto);

    /// <summary>Instante actual en la zona de negocio.</summary>
    public static DateTimeOffset Ahora() => DateTimeOffset.UtcNow.ToOffset(Zona.GetUtcOffset(DateTimeOffset.UtcNow));

    /// <summary>Fecha de negocio de un instante UTC dado.</summary>
    public static DateOnly FechaEnZonaNegocio(DateTimeOffset instante)
        => DateOnly.FromDateTime(instante.ToOffset(Zona.GetUtcOffset(instante)).DateTime);

    /// <summary>Fecha de negocio de hoy.</summary>
    public static DateOnly Hoy() => FechaEnZonaNegocio(DateTimeOffset.UtcNow);

    /// <summary>
    /// Una factura está vencida si hoy (en Santo Domingo) supera su vencimiento.
    /// Vencimiento el mismo día no está vencido: vence al final de ese día.
    /// </summary>
    public static bool EstaVencida(DateOnly vencimiento, DateOnly hoy) => hoy > vencimiento;

    /// <summary>Días transcurridos desde el vencimiento; 0 si aún no venció.</summary>
    public static int DiasDeVencida(DateOnly vencimiento, DateOnly hoy)
        => Math.Max(0, hoy.DayNumber - vencimiento.DayNumber);
}