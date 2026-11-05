using AtlasRent.Api.Jobs;
using Hangfire;

namespace AtlasRent.Api.Services;

/// <summary>
/// La cola de envíos en masa (F4-03, docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>No es un job recurrente.</b> <c>ProgramacionJobs</c> solo programa lo que se repite por reloj
/// y su propio comentario dice que los envíos no se inventan ahí: un trabajo de Hangfire se crea
/// porque alguien ha pedido mandar algo, no porque sea la hora. Por eso esto se encola con
/// <see cref="BackgroundJobClient"/> desde el servicio y no aparece en la tabla de jobs de
/// <c>DOC_TECNICA.md §8</c> como uno más.
/// </para>
/// <para>
/// <b>Un trabajo por envío</b>, no uno por lote. Un lote son cientos de correos y un trabajo que
/// recorre los cientos se pierde entero si se corta a mitad; uno por documento se reintenta solo y
/// el resto del lote sigue.
/// </para>
/// </summary>
public interface IColaEnvios
{
    /// <summary>
    /// Encola el envío de la fila <paramref name="envioId"/>.
    /// </summary>
    /// <param name="usuario">
    /// Usuario al que el trabajo le comprobará el permiso: un trabajo no tiene sesión HTTP, y el
    /// permiso se le comprueba a quien pidió el envío.
    /// </param>
    /// <param name="ip">IP de quien confirmó, que es la que va en la auditoría del envío.</param>
    void EncolarEnvio(Guid envioId, Guid usuario, string ip);
}

/// <summary>
/// La cola cuando el proceso <b>no tiene servidor de trabajos</b> (<c>Jobs__ServidorActivo=false</c>:
/// una segunda instancia, o una pruebas de integración).
/// <para>
/// No es una cola que no hace nada: es una cola que se niega. Un envío masivo sobre una instancia sin
/// servidor dejaría filas <c>Pendiente</c> para siempre y el lote se quedaría en «pendientes» sin que
/// nadie pueda saber por qué. Fallar aquí lo dice al confirmar.
/// </para>
/// </summary>
public sealed class ColaEnviosSinServidor : IColaEnvios
{
    public void EncolarEnvio(Guid envioId, Guid usuario, string ip) =>
        throw new InvalidOperationException(
            "Esta instancia no tiene servidor de trabajos, así que no hay cola donde poner el envío. "
            + "Arranca con Jobs__ServidorActivo=true.");
}

/// <inheritdoc />
public sealed class ColaEnviosHangfire(IBackgroundJobClient cola) : IColaEnvios
{
    // El `CancellationToken` del final va como `default` y no como un valor, por el mismo motivo que en
    // `ProgramacionJobs`: Hangfire reconoce ese parámetro y lo sustituye por el suyo al ejecutar, que
    // es lo que cancela el trabajo si se apaga el proceso. Pasarlo como constante explícita es lo que
    // Hangfire sabe resolver al serializar la expresión.
    public void EncolarEnvio(Guid envioId, Guid usuario, string ip) =>
        cola.Enqueue<TrabajoEnvio>(j => j.EjecutarAsync(envioId, usuario, ip, default(CancellationToken)));
}