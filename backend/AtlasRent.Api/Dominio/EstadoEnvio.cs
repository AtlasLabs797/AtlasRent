namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado de un envío de correo (docs/ESPECIFICACION.md §3.14). Son los cinco estados de la
/// especificación y ni uno más.
/// <para>
/// <c>Entregado</c> y <c>Leido</c> no los pone este servicio: salen de las respuestas del servidor
/// de correo (un enlace de seguimiento o un retorno), y hasta que exista esa integración ningún
/// sistema sabe cuándo llegó el mensaje. Escribir «entregado» en el momento del <c>Send</c> sería
/// una afirmación que nadie ha comprobado, así que la fila sale en <c>Enviado</c> y se queda ahí.
/// </para>
/// <para>
/// <c>Pendiente</c> tampoco lo escribe este servicio, que manda y escribe el resultado en el mismo
/// intento: lo escribe el envío masivo de W4, que encola lo que hay que mandar y devuelve la fila
/// antes de que el trabajo la mande.
/// </para>
/// </summary>
public enum EstadoEnvio
{
    /// <summary>En la cola, todavía sin llamar al servidor de correo (envío masivo de W4).</summary>
    Pendiente = 1,

    /// <summary>El servidor de correo aceptó el mensaje. Es lo máximo que se puede afirmar sin más.</summary>
    Enviado = 2,

    /// <summary>Confirmado por el servidor de correo o por un enlace de seguimiento.</summary>
    Entregado = 3,

    /// <summary>Confirmada la apertura por el destinatario.</summary>
    Leido = 4,

    /// <summary>
    /// El envío se intentó y no salió. El motivo va en <c>ultimo_error</c>, y el documento se puede
    /// volver a mandar porque el antidiplicado (§3.14) solo mira los envíos que sí salieron.
    /// </summary>
    Fallido = 5,
}