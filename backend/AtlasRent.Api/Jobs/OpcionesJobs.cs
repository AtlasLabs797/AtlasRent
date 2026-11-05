namespace AtlasRent.Api.Jobs;

/// <summary>
/// Opciones del servidor de jobs, de la sección <c>Jobs</c> de la configuración
/// (<c>Jobs__ServidorActivo</c>).
/// <para>
/// Encendido por defecto, y apagado solo a propósito: <c>false</c> es para arrancar la API sin
/// planificador (una segunda instancia que no debe ejecutar los jobs, o los tests de integración,
/// donde un servidor real se llevaría por delante las copias de seguridad de la máquina de pruebas).
/// </para>
/// </summary>
public sealed class OpcionesJobs
{
    public const string Seccion = "Jobs";

    /// <summary>
    /// Si el proceso levanta el servidor de jobs. Apagarlo no desregistra el almacenamiento ni los
    /// trabajos programados: simplemente la API no los ejecuta.
    /// </summary>
    public bool ServidorActivo { get; init; } = true;

    public static OpcionesJobs Desde(IConfiguration configuracion)
    {
        var valor = configuracion[$"{Seccion}:ServidorActivo"];

        if (string.IsNullOrWhiteSpace(valor))
        {
            return new OpcionesJobs();
        }

        if (!bool.TryParse(valor, out var activo))
        {
            throw new InvalidOperationException(
                $"El valor de {Seccion}__ServidorActivo no es true ni false: '{valor}'.");
        }

        return new OpcionesJobs { ServidorActivo = activo };
    }
}