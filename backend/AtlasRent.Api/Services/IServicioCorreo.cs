namespace AtlasRent.Api.Services;

/// <summary>
/// Cómo se manda un correo (F4-02). Existe la interfaz para que las pruebas la sustituyan por un
/// doble: el criterio de aceptación de la tarea es qué pasa cuando el servidor de correo falla, y
/// eso no se puede probar esperando al servidor de verdad —igual que <c>IFuenteTipoCambio</c> en
/// F3-01 para no depender de la API de tipos de cambio.
/// <para>
/// La interfaz solo manda. Componer el mensaje, decidir a quién y escribir la fila de
/// <c>envio</c> son cosa de <see cref="ServicioEnvios"/>, para que lo que se prueba sea el
/// servicio y no el transporte.
/// </para>
/// </summary>
public interface IServicioCorreo
{
    /// <summary>
    /// Manda el mensaje. Vuelve cuando el servidor de correo lo ha aceptado; si no lo acepta, lanza.
    /// <para>
    /// Un fallo del SMTP se propaga como excepción y lo trata el servicio que llama, que es quien
    /// escribe la fila <c>Fallido</c>. Aquí no se traga ninguna: si vuelve sin avisar, el envío queda
    /// registrado como enviado sin haber salido.
    /// </para>
    /// </summary>
    /// <exception cref="CorreoNoConfiguradoException">
    /// No hay servidor de correo configurado. Lo escribe el servicio, no el transporte.
    /// </exception>
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct);
}

/// <summary>
/// Un correo ya compuesto y con el adjunto listo. Los bytes del PDF van tal cual, sin volver a
/// generarlos: lo que se guarda en <c>envio.pdf_copia</c> es lo que sale.
/// </summary>
/// <param name="DestinatarioEmail">Dirección a la que se manda.</param>
/// <param name="DestinatarioNombre">Nombre para el encabezado <c>To</c>. Opcional.</param>
/// <param name="Asunto">Asunto con las variables ya sustituidas.</param>
/// <param name="Cuerpo">Cuerpo en <b>texto plano</b>. Sin HTML, sin etiquetas y sin entidades.</param>
/// <param name="NombreAdjunto">Nombre del PDF adjunto.</param>
/// <param name="Adjunto">Los bytes exactos del PDF.</param>
public sealed record MensajeCorreo(
    string DestinatarioEmail,
    string? DestinatarioNombre,
    string Asunto,
    string Cuerpo,
    string NombreAdjunto,
    byte[] Adjunto);