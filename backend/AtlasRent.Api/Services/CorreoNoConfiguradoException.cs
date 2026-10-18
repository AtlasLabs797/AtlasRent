namespace AtlasRent.Api.Services;

/// <summary>
/// El correo no está configurado. Lo lanza <see cref="ServicioEnvios"/> antes de generar el PDF, y el
/// controlador lo convierte en un <b>409</b> que dice exactamente qué escribir y dónde.
/// <para>
/// Es una excepción propia —y no un <see cref="InvalidOperationException"/> cualquiera— porque es la
/// única cuyo mensaje está escrito para que alguien lo lea sin ser del equipo: es lo que ve la
/// persona que pulses «enviar» y no entienda por qué no sale nada.
/// </para>
/// </summary>
public sealed class CorreoNoConfiguradoException : InvalidOperationException
{
    /// <summary>Qué falta para poder mandar. Va en el detalle del 409, no en el mensaje corto.</summary>
    public string QueFalta { get; }

    public CorreoNoConfiguradoException(string queFalta)
        : base("No hay correo configurado, así que no se puede enviar nada.")
    {
        QueFalta = queFalta;
    }
}