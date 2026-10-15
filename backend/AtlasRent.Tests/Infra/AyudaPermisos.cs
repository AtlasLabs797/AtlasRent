using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// Cliente con la sesión de un usuario ya dentro, con las cookies que hacen falta para las
/// peticiones que modifican estado (CSRF double-submit).
/// </summary>
public sealed class SesionCliente(HttpClient cliente, (string Nombre, string Valor)[] cookies) : IDisposable
{
    public Task<HttpResponseMessage> GetAsync(string ruta, CancellationToken ct) =>
        cliente.GetAsync(ruta, ct);

    public Task<HttpResponseMessage> PostAsync(string ruta, object cuerpo, CancellationToken ct) =>
        Enviar(HttpMethod.Post, ruta, cuerpo, null, ct);

    /// <summary>Mutación con <c>Idempotency-Key</c> (docs/DOC_TECNICA.md §6).</summary>
    public Task<HttpResponseMessage> PostAsync(string ruta, object cuerpo, string claveIdempotencia, CancellationToken ct) =>
        Enviar(HttpMethod.Post, ruta, cuerpo, claveIdempotencia, ct);

    public Task<HttpResponseMessage> PutAsync(string ruta, object cuerpo, CancellationToken ct) =>
        Enviar(HttpMethod.Put, ruta, cuerpo, null, ct);

    public Task<HttpResponseMessage> PutAsync(string ruta, object cuerpo, string claveIdempotencia, CancellationToken ct) =>
        Enviar(HttpMethod.Put, ruta, cuerpo, claveIdempotencia, ct);

    public Task<HttpResponseMessage> DeleteAsync(string ruta, CancellationToken ct) =>
        Enviar(HttpMethod.Delete, ruta, null, null, ct);

    private Task<HttpResponseMessage> Enviar(
        HttpMethod metodo,
        string ruta,
        object? cuerpo,
        string? claveIdempotencia,
        CancellationToken ct)
    {
        var peticion = AyudaAuth.ConCookies(metodo, ruta, cookies, cuerpo ?? new { });
        if (claveIdempotencia is not null)
        {
            peticion.Headers.Add("Idempotency-Key", claveIdempotencia);
        }

        return cliente.SendAsync(peticion, ct);
    }

    public void Dispose() => cliente.Dispose();
}

/// <summary>
/// Utilidades de las pruebas de permisos: usuario con su rol y sesión HTTP lista.
/// <para>
/// El login del administrador sale con desafío de segundo factor (D4), así que esta ayuda hace los
/// dos pasos: alta del MFA y entrada con el primer código TOTP. Para los demás roles el login ya
/// devuelve la sesión.
/// </para>
/// </summary>
public static class AyudaPermisos
{
    public static async Task<SesionCliente> SesionAsync(
        ApiTestFactory api,
        Usuario usuario,
        string contrasena,
        CancellationToken ct)
    {
        var cliente = api.CrearCliente();
        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, contrasena, ct);
        login.EnsureSuccessStatusCode();

        var entrada = login;
        if (await TieneSegundoFactorAsync(login, ct))
        {
            var uri = await AyudaMfa.ActivarAsync(cliente, ct);
            var secreto = AyudaMfa.SecretoDe(uri);
            await AyudaMfa.ConfirmarAsync(cliente, secreto, api.Reloj, ct);
            entrada = await AyudaMfa.EntrarConTotpAsync(cliente, secreto, api.Reloj, ct);
            entrada.EnsureSuccessStatusCode();
        }

        return new SesionCliente(
            cliente,
            [.. AyudaAuth.CookiesDe(entrada)
                .Where(cookie => cookie.Nombre is NombresCookies.Acceso or NombresCookies.Csrf)
                .Select(cookie => (cookie.Nombre, cookie.Valor))]);
    }

    private static async Task<bool> TieneSegundoFactorAsync(HttpResponseMessage login, CancellationToken ct)
    {
        var cuerpo = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
        return cuerpo.TryGetProperty("requiereMfa", out var requiere) && requiere.GetBoolean();
    }
}