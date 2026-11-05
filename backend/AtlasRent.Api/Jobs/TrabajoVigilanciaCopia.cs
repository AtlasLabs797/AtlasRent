using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Vigila que la copia de seguridad del dia exista de verdad. Lo programa
/// <see cref="ProgramacionJobs"/> a las 04:00 de Santo Domingo, dos horas despues de la copia.
/// <para>
/// Cubre el fallo mas peligroso y mas silencioso de todo el respaldo: el que no se ejecuta. Todo lo
/// demas (volcado vacio, descifrado, rotacion) es un fallo que alguien ve porque el trabajo lanza.
/// Pero si el proceso estaba apagado a las 02:00, si el planificador no disparo el trabajo, o si
/// alguien desactivo el servidor de jobs, no hay ningun error, ningun log de fallo y ninguna
/// excepcion: simplemente no hay copia, y eso es indistinguible de que todo vaya bien hasta que
/// hace falta restaurar.
/// <para>
/// Por eso no pregunta si el trabajo de las 02:00 termino, que seria preguntarse a si mismo, sino
/// algo externo: abre el archivo que deberia haber en disco, lo descifra y lo comprueba con el
/// mismo <see cref="ServicioBackup.VerificarAsync"/> que usa el resto del sistema.
/// </para>
/// </summary>
public sealed class TrabajoVigilanciaCopia(ServicioBackup copias, IRegistroAlertas alertas)
{
    /// <summary>Tipo de la alerta, el mismo que usa el fallo del propio respaldo.</summary>
    private const string Tipo = "backup";

    /// <summary>
    /// La clave lleva la fecha y es distinta de la del fallo (<c>backup:{fecha}</c>) a proposito. No
    /// es la misma alerta contada dos veces: una dice que el respaldo fallo y el motivo es este, la
    /// otra dice que a las 04:00 no habia copia del dia. El operador quiere las dos, y solo hay una
    /// de cada por dia.
    /// </summary>
    private static string ClaveAusente(DateOnly fecha) => $"backup-ausente:{fecha:yyyy-MM-dd}";

    public async Task EjecutarAsync(CancellationToken ct)
    {
        var fecha = copias.FechaDeHoy;
        var ruta = Path.Combine(copias.Carpeta, ServicioBackup.NombreDeCopia(fecha));

        var copia = await copias.VerificarAsync(ruta, ct);
        if (copia is not null)
        {
            Log.Information(
                "Vigilancia: la copia del {Fecha} existe y verifica ({Documentos} documentos)",
                fecha, copia.Documentos().Count);
            return;
        }

        // Se distingue el motivo porque no es lo mismo y el operador actua distinto: si el archivo
        // esta pero no verifica, el problema es la copia; si no esta, el problema es que no se hizo.
        var motivo = File.Exists(ruta)
            ? $"el archivo {Path.GetFileName(ruta)} existe pero no supera la verificacion"
            : $"no hay ningun archivo {Path.GetFileName(ruta)} en la carpeta de copias";
        var mensaje =
            $"A las 04:00 no hay copia de seguridad utilizable del {fecha:yyyy-MM-dd}: {motivo}. "
            + "Lo mas probable es que el trabajo de las 02:00 no llegara a ejecutarse.";

        Log.Error(mensaje);
        await alertas.RegistrarAsync(Tipo, ClaveAusente(fecha), mensaje, ct);
    }
}