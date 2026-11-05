using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>Resultado de la comprobación de una contraseña contra HaveIBeenPwned.</summary>
/// <param name="Comprobada">
/// Falso si no se pudo consultar la API. En ese caso la operación no se bloquea, pero queda registrado.
/// </param>
public sealed record ComplicidadContrasena(bool Comprobada, bool ApareceEnFiltraciones, int Apariciones);

/// <summary>
/// Comprobación de contraseñas filtradas con el modelo de k-anonimato de HaveIBeenPwned: solo
/// viaja el prefijo de 5 caracteres del SHA-1 de la contraseña y se recibe el sufijo con la lista
/// de sufijos filtrados. Ni la contraseña ni su hash completo salen de la máquina
/// (docs/DOC_TECNICA.md §7, docs/SEGURIDAD.md A.2).
/// </summary>
public sealed class ServicioComplicidad(HttpClient http)
{
    private const string UrlRangos = "https://api.pwnedpasswords.com/range/";

    /// <summary>Cifras de apariciones en filtraciones de la contraseña dada.</summary>
    public async Task<ComplicidadContrasena> ComprobarAsync(string contrasena, CancellationToken ct)
    {
        // SHA-1 en mayúsculas: es lo que publica HaveIBeenPwned, no una elección criptográfica.
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(contrasena))).ToUpperInvariant();
        var prefijo = sha1[..5];
        var sufijo = Encoding.ASCII.GetBytes(sha1[5..]);

        try
        {
            using var respuesta = await http.GetAsync(UrlRangos + prefijo, ct);
            respuesta.EnsureSuccessStatusCode();
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            var apariciones = 0;
            var encontrado = false;
            foreach (var linea in cuerpo.Split('\n'))
            {
                var separador = linea.IndexOf(':');
                if (separador <= 0)
                {
                    continue;
                }

                // Comparación en tiempo constante y sin salir en el primer acierto: el bucle recorre
                // toda la respuesta para que el tiempo no dependa de la posición del sufijo.
                var candidato = Encoding.ASCII.GetBytes(linea[..separador].Trim());
                if (CryptographicOperations.FixedTimeEquals(candidato, sufijo))
                {
                    encontrado = true;
                    apariciones += int.TryParse(linea[(separador + 1)..], out var n) ? n : 1;
                }
            }

            return new ComplicidadContrasena(true, encontrado, apariciones);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // TODO(A#) ¿Debe una caída de HaveIBeenPwned impedir cambiar la contraseña? Ahora no
            // bloquea, pero solo se registra el fallo; la respuesta pendiente la decide el
            // responsable (permitir con aviso o denegar cuando la API no responde).
            Log.Warning(
                "HaveIBeenPwned no respondió ({TipoError}); la contraseña no se pudo comprobar.",
                e.GetType().Name);

            return new ComplicidadContrasena(false, false, 0);
        }
    }
}