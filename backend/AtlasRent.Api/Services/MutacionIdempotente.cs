using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AtlasRent.Api.Services;

/// <summary>
/// Datos de la cabecera <c>Idempotency-Key</c> de una mutación (docs/DOC_TECNICA.md §6).
/// <para>
/// La huella es del <em>cuerpo</em> de la petición: repetir la misma clave con otro cuerpo no es
/// un reintento, es una equivocación, y se rechaza en vez de devolver la respuesta anterior.
/// </para>
/// </summary>
public sealed record MutacionIdempotente(string Clave, string Ruta, string Huella)
{
    /// <summary>Límite de la clave: es una referencia corta, no un documento.</summary>
    public const int LongitudMaximaClave = 128;

    /// <summary>Nulo si la petición no trae clave: entonces la mutación no es idempotente.</summary>
    public static MutacionIdempotente? Crear(string? clave, string ruta, object cuerpo)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            return null;
        }

        var valor = clave.Trim();
        if (valor.Length > LongitudMaximaClave)
        {
            throw new ArgumentException(
                $"La cabecera Idempotency-Key no puede pasar de {LongitudMaximaClave} caracteres.",
                nameof(clave));
        }

        return new MutacionIdempotente(valor, ruta, HuellaDe(cuerpo));
    }

    private static string HuellaDe(object cuerpo) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cuerpo))));
}

/// <summary>
/// La misma clave de idempotencia se usó antes con un cuerpo distinto.
/// </summary>
public sealed class IdempotenciaEnConflictoException(string clave)
    : Exception($"La clave de idempotencia '{clave}' ya se usó con otra petición.");