using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// Doble de HaveIBeenPwned para las pruebas que levantan la API: ninguna prueba sale a la red real.
/// Por defecto nada está filtrado; <see cref="Filtradas"/> y <see cref="Caido"/> fijan los otros casos.
/// </summary>
public sealed class ManejadorHibpFalso : HttpMessageHandler
{
    private int _peticiones;

    /// <summary>Contraseñas que "aparecen en filtraciones".</summary>
    public HashSet<string> Filtradas { get; } = [];

    /// <summary>Si es cierto, la API "no responde".</summary>
    public bool Caido { get; set; }

    public int Peticiones => _peticiones;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _peticiones);
        if (Caido)
        {
            throw new HttpRequestException("HaveIBeenPwned simulado: sin red.");
        }

        var prefijo = peticion.RequestUri!.AbsolutePath.Split('/')[^1];
        var lineas = Filtradas
            .Select(c => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(c))).ToUpperInvariant())
            .Where(sha1 => sha1.StartsWith(prefijo, StringComparison.Ordinal))
            .Select(sha1 => $"{sha1[5..]}:7")
            .Append("0000000000000000000000000000000000A:1");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\r\n", lineas)),
        });
    }
}
