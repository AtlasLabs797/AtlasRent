namespace AtlasRent.Api.Services;

/// <summary>
/// De dónde saca la API la IP real del cliente.
/// <para>
/// Hoy es la de la conexión. Cuando haya un proxy delante (Caddy o Cloudflare,
/// docs/DOC_TECNICA.md §9) la resolución pasa a estar aquí del todo, leyendo
/// <c>X-Forwarded-For</c> solo de los proxies de confianza de <c>Seguridad__ProxiesConfianza</c>;
/// con eso queda en un único sitio, que es donde tiene que estar para no fiarse de cabeceras que
/// pueda mandar cualquiera.
/// </para>
/// </summary>
public interface IResolutorIpPeticion
{
    string Resolver(HttpContext http);
}

/// <inheritdoc cref="IResolutorIpPeticion"/>
public sealed class ResolutorIpPeticion : IResolutorIpPeticion
{
    /// <summary>
    /// Valor con el que se marca una petición sin IP conocida. No es una IP real, así que no
    /// bloquea a nadie por accidente.
    /// </summary>
    public const string Desconocida = "0.0.0.0";

    public string Resolver(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? Desconocida;
}