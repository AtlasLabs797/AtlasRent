using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// F4-03: el trabajo que manda <b>un</b> envío del lote de §3.14.
/// <para>
/// Es una clase fina a propósito, como todos los trabajos de W4 y W5: solo dispara al servicio. La
/// regla está en <see cref="ServicioEnvios.EnviarPendienteAsync"/> y no aquí, que es lo que permite
/// que las pruebas la llamen directamente —sin planificador y sin almacenamiento— y que el
/// comportamiento sea el mismo en la cola y en la prueba.
/// </para>
/// <para>
/// <b>Trabaja sobre la fila, no sobre el documento.</b> Recibe el identificador del envío y manda
/// esa fila: el texto, el destinatario y la copia del PDF ya están decididos en el resumen que leyó
/// alguien. Volver a calcularlos aquí sería volver a decidir, en un trabajo sin nadie mirando, lo
/// que esa persona confirmó.
/// </para>
/// </summary>
public sealed class TrabajoEnvio(ServicioEnvios envios)
{
    public async Task EjecutarAsync(Guid envioId, Guid usuario, string ip, CancellationToken ct)
    {
        var estado = await envios.EnviarPendienteAsync(envioId, usuario, ip, ct);

        if (estado == EstadoEnvio.Enviado)
        {
            Log.Information("Envío masivo: el envío {EnvioId} salió.", envioId);
            return;
        }

        // Fallido no es una excepción: el fallo de un correo es un dato del lote que se consulta y se
        // reintenta, no un fallo del trabajo. Lanzar aquí haría que Hangfire lo reintentara por su
        // cuenta, sin que nadie lo hubiera pedido, y §3.14 deja el reintento en la persona que mira
        // el progreso.
        Log.Warning(
            "Envío masivo: el envío {EnvioId} no salió ({Estado}). Queda en el lote para reintentar.",
            envioId,
            estado);
    }
}