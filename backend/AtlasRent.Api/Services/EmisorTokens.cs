using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AtlasRent.Api.Services;

/// <summary>Tokens que se emiten al iniciar o al refrescar una sesión.</summary>
public sealed record TokensEmitidos(string JwtAcceso, DateTimeOffset CaducaAcceso, string TokenRefresco, string TokenCsrf);

/// <summary>
/// Emite el JWT de acceso, el token de refresco y el token de CSRF.
/// <para>
/// El JWT se firma con HS256 y lleva <c>sid</c> (la fila de <c>sesion</c>), que es lo que permite
/// revocarlo en la siguiente petición. Del token de refresco y del de CSRF solo se guarda el hash
/// o nada, respectivamente.
/// </para>
/// </summary>
public sealed class EmisorTokens(OpcionesSeguridad opciones, TimeProvider reloj)
{
    /// <summary>Vida del JWT de acceso (docs/DOC_TECNICA.md §7).</summary>
    public const int VidaAccesoMinutos = 15;

    private readonly JsonWebTokenHandler _manejador = new();

    public TokensEmitidos Emitir(Guid usuarioId, Guid sesionId, string rol)
    {
        var ahora = reloj.GetUtcNow();
        var caduca = ahora.AddMinutes(VidaAccesoMinutos);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = OpcionesSeguridad.Emisor,
            Audience = OpcionesSeguridad.Audiencia,
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = caduca.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", usuarioId.ToString("D")),
                new Claim("sid", sesionId.ToString("D")),
                // TODO(A#) F1-07: el rol pasará a leerse de la tabla "rol" del usuario.
                new Claim("rol", rol),
                new Claim("jti", Guid.NewGuid().ToString("D")),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(opciones.ClaveJwt), SecurityAlgorithms.HmacSha256),
        };

        return new TokensEmitidos(
            _manejador.CreateToken(descriptor),
            caduca,
            TokenAleatorio(),
            TokenAleatorio());
    }

    /// <summary>
    /// Hash SHA-256 en hexadecimal de un token de refresco. Es lo único que se guarda de él
    /// (docs/SEGURIDAD.md A.2): con el hash no se puede refrescar la sesión.
    /// </summary>
    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    /// <summary>32 bytes aleatorios en base64url: 256 bits de entropía para el token.</summary>
    public static string TokenAleatorio() =>
        System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}