namespace AtlasRent.Api.Services;

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

/// <summary>
/// El envío real, con MailKit (F4-02). Va detrás de <see cref="IServicioCorreo"/> para que las
/// pruebas no salgan a internet.
/// <para>
/// El mensaje va en <b>texto plano</b> (<c>BodyBuilder.TextBody</c>, sin <c>HtmlBody</c>) y con el
/// adjunto en el <c>multipart/mixed</c> que arma la propia biblioteca. Es la misma regla que los PDF
/// por código y no por HTML y navegador (SEC-001 en docs/SEGURIDAD.md): el nombre del cliente y el
/// número de la factura van literales, sin escapar, y un correo en texto plano no puede ejecutar
/// nada ni llamar a ninguna red por su cuenta.
/// </para>
/// <para>
/// <b>Lo que falla aquí no lleva credenciales.</b> El mensaje de una excepción de SMTP puede
/// contener el host y la respuesta del servidor, así que aquí no se registra ni se propaga nada: el
/// servicio que llama es quien decide qué texto se guarda en <c>envio.ultimo_error</c>, y ese texto
/// sale de <see cref="ServicioEnvios.MotivoDe"/> con el criterio de F3-01 (DOC_TECNICA §8.0.1).
/// </para>
/// </summary>
public sealed class ServicioCorreoMailKit(
    OpcionesCorreo opciones,
    ILogger<ServicioCorreoMailKit> logger) : IServicioCorreo
{
    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
    {
        if (!opciones.Configurado)
        {
            // No debería llegar aquí (ServicioEnvios lo comprueba antes de generar el PDF), pero si
            // llegara es mejor un 409 que un intento de conexión con un host vacío.
            throw new CorreoNoConfiguradoException("falta el host o el remitente");
        }

        // Configurado ya ha comprobado que host y remitente no están vacíos, y que hay clave si hay
        // usuario. Los «!» son la traducción de esa comprobación al analizador nulo, no una supresión
        // por comodidad.
        var host = opciones.Host!;
        var remitente = opciones.Remitente!;

        // El cuerpo va por <c>TextBody</c> y no por <c>HtmlBody</c>: BodyBuilder arma un
        // text/plain y, como hay adjunto, lo envuelve en el multipart/mixed de RFC 2046. No hay
        // forma de que salga HTML por un camino que no se haya pedido.
        var cuerpo = new BodyBuilder
        {
            TextBody = mensaje.Cuerpo,
        };

        // Los bytes del adjunto, tal cual: los mismos que se guardan en <c>envio.pdf_copia</c>.
        cuerpo.Attachments.Add(mensaje.NombreAdjunto, mensaje.Adjunto);

        var correo = new MimeMessage
        {
            Body = cuerpo.ToMessageBody(),
        };

        correo.From.Add(new MailboxAddress(opciones.NombreRemitente, remitente));
        correo.To.Add(new MailboxAddress(mensaje.DestinatarioNombre, mensaje.DestinatarioEmail));
        correo.Subject = mensaje.Asunto;

        using var cliente = new SmtpClient();
        var seguridad = opciones.Ssl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.SslOnConnect;

        await cliente.ConnectAsync(host, opciones.Puerto, seguridad, ct);

        if (!string.IsNullOrWhiteSpace(opciones.Usuario))
        {
            await cliente.AuthenticateAsync(opciones.Usuario, opciones.Clave!, ct);
        }

        await cliente.SendAsync(correo, ct);
        await cliente.DisconnectAsync(true, ct);

        // La traza lleva destinatario y nombre del adjunto, nunca usuario, clave ni host: un log se
        // lee en cuanto sale algo mal y los tres son credenciales o infraestructura.
        logger.LogInformation(
            "Correo enviado a {Destinatario} con el adjunto {Adjunto}.",
            mensaje.DestinatarioEmail,
            mensaje.NombreAdjunto);
    }
}