using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// El cambio del día, de la fuente (F3-01, docs/ESPECIFICACION.md §3.12, D22).
/// <para>
/// Va a las 05:00, y es el único trabajo que <b>no</b> se pone detrás de todo lo demás, que es lo que
/// han hecho los de F2. Ahí el motivo era el mismo en todos: escriben en el estado del negocio, así
/// que si el servidor se apagó la copia de las 02:00 falta y no hay nada verificado sobre lo que
/// actuar. Aquí lo que se escribe es un valor de referencia que la factura y el cobro congelan al
/// emitirse, así que lo que importa es que esté antes de que empiece el día, no después.
/// </para>
/// <para>
/// Se solapa a esa hora con el aviso de reservas de 7 días de F2-03. No es una dependencia: los dos
/// trabajos son cortos, ninguno espera al otro y ninguno escribe en la misma fila. Se ha dejado
/// donde está porque es la primera hora del día en Santo Domingo, que es justo lo que el criterio de
/// aceptación del trabajo pide.
/// </para>
/// <para>
/// El trabajo solo dispara <see cref="ServicioTiposCambio"/>, que lleva la lógica, como la renovación
/// (F2-06) y el fin (F2-09): así las pruebas pueden ejercitar la fuente sin pasar por el
/// planificador.
/// </para>
/// </summary>
public sealed class TrabajoTipoCambio(ServicioTiposCambio tiposCambio)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resumen = await tiposCambio.ActualizarDesdeLaFuenteAsync(ct);

        if (resumen.Fallo)
        {
            Log.Warning(
                "Trabajo de tipo de cambio terminado con fallo de la fuente: {Cargados} valor(es) "
                    + "cargados, {Respetados} manual(es) respetados",
                resumen.Cargados,
                resumen.Respetados);

            return;
        }

        Log.Information(
            "Trabajo de tipo de cambio terminado: {Cargados} valor(es) de la fuente, "
                + "{Respetados} manual(es) respetados",
            resumen.Cargados,
            resumen.Respetados);
    }
}
