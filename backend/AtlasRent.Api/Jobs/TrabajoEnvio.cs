using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Hangfire;
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
    // BUG-038: tres intentos y no los diez por defecto. Un fallo del servidor de correo no llega aquí
    // (EnviarPendienteAsync lo convierte en Fallido); lo que se reintenta es un fallo transitorio de BD,
    // y reintentar es seguro porque la fila se reclama antes de enviar y un reintento no reenvía.
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task EjecutarAsync(Guid envioId, Guid usuario, string ip, CancellationToken ct)
    {
        EstadoEnvio estado;

        try
        {
            estado = await envios.EnviarPendienteAsync(envioId, usuario, ip, ct);
        }
        catch (PermisoDenegadoException)
        {
            // Reintentar no cambia un permiso. La fila queda Fallido (visible en el lote) en vez de
            // Pendiente, o el barrido la reencolaría para siempre.
            await envios.MarcarFallidoAsync(
                envioId, "El usuario que pidió el envío ya no tiene permiso para mandarlo.", usuario, ip, ct);
            Log.Warning("Envío masivo: el envío {EnvioId} se descarta por falta de permiso.", envioId);
            return;
        }

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