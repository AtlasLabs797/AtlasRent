namespace AtlasRent.Api.Services;

/// <summary>
/// De dónde saca la API la IP real del cliente.
/// <para>
/// Es la IP de la conexión, ya resuelta: cuando hay proxy delante (Caddy o Cloudflare,
/// docs/DOC_TECNICA.md §9), el middleware de <c>ForwardedHeaders</c> de Program.cs ha sustituido
/// <c>RemoteIpAddress</c> por la de <c>X-Forwarded-For</c> y solo si la conexión viene de un proxy de
/// <c>Seguridad__ProxiesConfianza</c> (SEC-014). Aquí no se lee ninguna cabecera, para no fiarse de
/// lo que pueda mandar cualquiera.
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