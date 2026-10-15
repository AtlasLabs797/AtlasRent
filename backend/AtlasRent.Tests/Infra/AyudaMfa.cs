using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using OtpNet;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// Utilidades de las pruebas del segundo factor (F1-06). El código TOTP lo calcula la propia prueba
/// con el secreto que devuelve la API y con el reloj del host: así se prueba el camino real
/// (secreto → URI → QR en el móvil → código) sin mockear nada.
/// </summary>
public static class AyudaMfa
{
    /// <summary>MFA ya activo en la API, con lo que devuelve la última respuesta que lo completó.</summary>
    public sealed record MfaActivo(string Secreto, IReadOnlyList<string> CodigosRecuperacion);

    /// <summary>Login de un usuario con MFA: el cliente se queda con la cookie del token provisional.</summary>
    public static Task<HttpResponseMessage> LoginConDesafioAsync(
        HttpClient cliente,
        Usuario usuario,
        string contrasena,
        CancellationToken ct) =>
        AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, contrasena, ct);

    /// <summary>Valor de la cookie del token provisional de una respuesta de login.</summary>
    public static string TokenDesafioDe(HttpResponseMessage login) =>
        AyudaAuth.Cookie(login, NombresCookies.DesafioMfa).Valor;

    /// <summary>Empieza el alta del MFA y devuelve la URI <c>otpauth://</c>.</summary>
    public static async Task<string> ActivarAsync(HttpClient cliente, CancellationToken ct)
    {
        var respuesta = await cliente.PostAsync("/api/v1/auth/mfa/activar", null, ct);
        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("uri").GetString()!;
    }

    /// <summary>Confirma el alta con un código TOTP válido y devuelve los códigos de recuperación.</summary>
    public static async Task<IReadOnlyList<string>> ConfirmarAsync(
        HttpClient cliente,
        string secreto,
        RelojPrueba reloj,
        CancellationToken ct)
    {
        var respuesta = await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/confirmar",
            new { codigo = CodigoTotp(secreto, reloj) },
            ct);
        respuesta.EnsureSuccessStatusCode();

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        return cuerpo.GetProperty("codigosRecuperacion").EnumerateArray()
            .Select(c => c.GetString()!)
            .ToArray();
    }

    /// <summary>
    /// Flujo de alta de un administrador que todavía no tenía MFA: login (sale desafío), alta y
    /// confirmación. Al final el cliente tiene el token provisional, pero aún no una sesión.
    /// </summary>
    public static async Task<MfaActivo> ActivarCompletoAsync(
        HttpClient cliente,
        Usuario usuario,
        string contrasena,
        RelojPrueba reloj,
        CancellationToken ct)
    {
        (await LoginConDesafioAsync(cliente, usuario, contrasena, ct)).EnsureSuccessStatusCode();

        var uri = await ActivarAsync(cliente, ct);
        var secreto = SecretoDe(uri);
        var codigos = await ConfirmarAsync(cliente, secreto, reloj, ct);

        return new MfaActivo(secreto, codigos);
    }

    /// <summary>Segundo paso del login: el token provisional más el código TOTP.</summary>
    public static Task<HttpResponseMessage> EntrarConTotpAsync(
        HttpClient cliente,
        string secreto,
        RelojPrueba reloj,
        CancellationToken ct) =>
        cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/verificar",
            new { codigo = CodigoTotp(secreto, reloj) },
            ct);

    /// <summary>Login ya con MFA activo, hasta el desafío (sin pasar todavía el segundo factor).</summary>
    public static Task<HttpResponseMessage> LoginHastaElDesafioAsync(
        HttpClient cliente,
        Usuario usuario,
        string contrasena,
        CancellationToken ct) =>
        AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, contrasena, ct);

    /// <summary>El secreto que va en el QR, tal cual viene en la URI.</summary>
    public static string SecretoDe(string uri)
    {
        var consulta = new Uri(uri).Query.TrimStart('?');
        var secreto = consulta
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(par => par.Split('=', 2))
            .Where(par => par.Length == 2 && par[0] == "secret")
            .Select(par => par[1])
            .SingleOrDefault();

        return Uri.UnescapeDataString(secreto ?? throw new InvalidOperationException(
            $"La URI de activación no lleva el secreto: {uri}"));
    }

    /// <summary>Código TOTP que la app del móvil mostraría en ese instante.</summary>
    public static string CodigoTotp(string secretoBase32, RelojPrueba reloj) =>
        new Totp(
            Base32Encoding.ToBytes(secretoBase32),
            totpSize: ServicioMfa.Digitos,
            mode: OtpHashMode.Sha1,
            step: ServicioMfa.PeriodoSegundos)
            .ComputeTotp(reloj.GetUtcNow().UtcDateTime);

    /// <summary>Código TOTP de 6 dígitos que no es el válido, para probar el rechazo.</summary>
    public static string CodigoTotpEquivocado(RelojPrueba reloj)
    {
        var codigo = CodigoTotp("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", reloj);
        return codigo == "000000" ? "111111" : "000000";
    }
}