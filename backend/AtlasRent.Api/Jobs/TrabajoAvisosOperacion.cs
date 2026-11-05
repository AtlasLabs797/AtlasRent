using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Los avisos de operación de §3.16 que no dependen de las tablas de F3 (F2-10): baja programada
/// próxima, unidad que se libera en 30 días, reserva en conflicto e IP bloqueada.
/// <para>
/// Va a las 08:00, detrás de todos los demás, por el mismo motivo que el fin por fecha efectiva
/// (F2-09): es el que escribe en <c>alerta</c> después de que la copia de las 02:00, la integridad y
/// el trabajo de fin ya han pasado. Si el servidor se apagó esa madrugada, la copia falta y avisar
/// sobre un estado que nadie ha verificado sería avisar de algo que puede que ya no sea cierto.
/// </para>
/// <para>
/// El trabajo solo dispara <see cref="ServicioAvisosOperacion"/>, que lleva la lógica, como la
/// renovación (F2-06) y el fin (F2-09): así las pruebas pueden correr los avisos sin pasar por el
/// planificador.
/// </para>
/// </summary>
public sealed class TrabajoAvisosOperacion(ServicioAvisosOperacion avisos)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resumen = await avisos.AvisarAsync(ct);

        Log.Information(
            "Trabajo de avisos terminado: {Bajas} baja(s) próxima(s), {Liberaciones} liberación(es), "
                + "{Conflictos} conflicto(s), {Bloqueos} IP bloqueada(s), {Cerradas} alerta(s) cerradas",
            resumen.BajasProgramadas,
            resumen.Liberaciones,
            resumen.Conflictos,
            resumen.IpsBloqueadas,
            resumen.AlertasCerradas);
    }
}