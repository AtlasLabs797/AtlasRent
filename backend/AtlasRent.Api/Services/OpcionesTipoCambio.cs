namespace AtlasRent.Api.Services;

/// <summary>
/// Configuración de la fuente del tipo de cambio (F3-01, D22), de la sección <c>TipoCambio</c> de la
/// configuración (<c>TipoCambio__Url</c> y <c>TipoCambio__ApiKey</c>).
/// <para>
/// <b>Sin clave funciona el endpoint abierto</b>, que es lo que hace que esto no dependa de una
/// decisión comercial de la empresa para arrancar. Con clave, el plan de pago vive en otra ruta y
/// devuelve la tabla con otro nombre (<c>conversion_rates</c> en vez de <c>rates</c>); por eso
/// <see cref="ClienteTipoCambio"/> entiende las dos formas.
/// </para>
/// <para>
/// La clave no aparece nunca en un mensaje: la URL con clave es una credencial y los mensajes de
/// este servicio acaban en la tabla <c>alerta</c>, que se lee fuera.
/// </para>
/// </summary>
public sealed class OpcionesTipoCambio
{
    public const string Seccion = "TipoCambio";

    /// <summary>
    /// Endpoint abierto de ExchangeRate-API, sin clave. Es el que se usa mientras no haya ninguna
    /// configurada, porque es el único que no exige dar de alta nada antes de que el trabajo diario
    /// pueda funcionar.
    /// </summary>
    public const string UrlAbiertaPorDefecto = "https://open.er-api.com/v6/latest/USD";

    /// <summary>
    /// Base del endpoint con clave. No es configurable: es la ruta del plan de pago, y ponerla en
    /// configuración invitaría a apuntar la clave a un sitio que no es el suyo.
    /// </summary>
    public const string BaseConClave = "https://v6.exchangerate-api.com/v6";

    /// <summary>Consulta sin clave. Solo se usa cuando <see cref="ApiKey"/> está vacía.</summary>
    public string Url { get; init; } = UrlAbiertaPorDefecto;

    /// <summary>Clave del plan de pago. Vacía = endpoint abierto.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// La URL que se consulta de verdad. Con clave manda la ruta con clave, porque la de
    /// <see cref="Url"/> es de otro servicio y devuelve otra forma de respuesta.
    /// </summary>
    public string UrlDeConsulta => string.IsNullOrWhiteSpace(ApiKey)
        ? Url
        : $"{BaseConClave}/{ApiKey.Trim()}/latest/USD";

    /// <summary>Si hay clave configurada, que es lo que decide qué respuesta espera el cliente.</summary>
    public bool HayClave => !string.IsNullOrWhiteSpace(ApiKey);

    public static OpcionesTipoCambio Desde(IConfiguration configuracion)
    {
        var url = configuracion[$"{Seccion}:Url"];
        var clave = configuracion[$"{Seccion}:ApiKey"];

        return new OpcionesTipoCambio
        {
            Url = string.IsNullOrWhiteSpace(url) ? UrlAbiertaPorDefecto : url.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(clave) ? null : clave.Trim(),
        };
    }
}
