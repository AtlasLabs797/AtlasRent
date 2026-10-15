using AtlasRent.Api.Services;
using Hangfire;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Fin por fecha efectiva (F2-09). Corre a las 07:00 y es el trabajo que hace que el criterio de
/// aceptación sea cierto: la unidad queda libre desde la fecha efectiva, no desde que alguien se acuerde
/// de apretar el botón.
/// <para>
/// El trabajo no pide permisos —va por detrás de la API, no hay sesión— pero sí audita, y la fila de
/// auditoría sale con <c>usuario_id</c> nulo y <c>origen = "job"</c>: es el sistema el que cerró el
/// contrato, no un gerente, y el historial tiene que poder distinguir las dos cosas.
/// </para>
/// </summary>
public sealed class TrabajoFinContratos(ServicioFines fines)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resumen = await fines.FinalizarVencidosAsync(ct);

        Log.Information(
            "Trabajo de fin terminado: {Finalizados} finalizado(s), {Omitidos} omitido(s), {Alertas} "
                + "contrato(s) esperando liquidación",
            resumen.ContratosFinalizados,
            resumen.Omitidos,
            resumen.AlertasVivas);
    }
}