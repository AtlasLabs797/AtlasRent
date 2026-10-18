namespace AtlasRent.Api.Services;

/// <summary>
/// Opciones de los tokens de agente (F6-01), leídas de la sección <c>Agentes</c> de la
/// configuración. Valores por defecto que valen en cualquier instalación: un token sin caducidad
/// configurable sería un token para siempre.
/// </summary>
public sealed class OpcionesAgentes
{
    public const string Seccion = "Agentes";

    /// <summary>
    /// Vida por defecto del token en días (<c>Agentes__VidaDiasToken</c>). El administrador puede
    /// pedir otra en el alta, siempre dentro del máximo.
    /// </summary>
    public int VidaDiasToken { get; init; } = 90;

    /// <summary>
    /// Tope de la vida por defecto en días (<c>Agentes__VidaDiasTokenMaxima</c>). Un token que
    /// dura un año sin renewal es una credencial olvidada.
    /// </summary>
    public int VidaDiasTokenMaxima { get; init; } = 365;

    /// <summary>
    /// Consultas por día que trae el token si el administrador no dice otra cosa
    /// (<c>Agentes__LimiteConsultasDia</c>).
    /// </summary>
    public int LimiteConsultasDia { get; init; } = 500;

    public static OpcionesAgentes Desde(IConfiguration configuracion) => new()
    {
        VidaDiasToken = configuracion.GetValue($"{Seccion}:VidaDiasToken", 90),
        VidaDiasTokenMaxima = configuracion.GetValue($"{Seccion}:VidaDiasTokenMaxima", 365),
        LimiteConsultasDia = configuracion.GetValue($"{Seccion}:LimiteConsultasDia", 500),
    };
}