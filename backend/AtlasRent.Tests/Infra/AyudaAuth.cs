using System.Net.Http.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Infra;

/// <summary>Cookie que devuelve la API, con sus atributos para poder comprobarlos en los tests.</summary>
public sealed record CookieFijada(string Nombre, string Valor, string Cabecera)
{
    public bool HttpOnly => Cabecera.Contains("httponly", StringComparison.OrdinalIgnoreCase);

    public bool Secure => Cabecera.Contains("secure", StringComparison.OrdinalIgnoreCase);

    public bool SameSiteEstricto => Cabecera.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase);

    public string? Path => Cabecera.Split(';')
        .Select(atributo => atributo.Trim())
        .Where(atributo => atributo.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
        .Select(atributo => atributo["path=".Length..])
        .SingleOrDefault();
}

/// <summary>Utilidades de las pruebas de autenticación.</summary>
public static class AyudaAuth
{
    /// <summary>
    /// Crea un usuario en el contenedor con su rol. El nombre de acceso lleva un sufijo único
    /// porque el índice de <c>usuario.nombre_acceso</c> es único y todas las pruebas comparten base
    /// de datos.
    /// </summary>
    public static async Task<Usuario> CrearUsuarioAsync(
        ContenedorBdFixture bd,
        string contrasena,
        bool activo = true,
        string rol = Roles.Empleado,
        CancellationToken ct = default)
    {
        var nombreAcceso = $"usuario{Guid.NewGuid():N}"[..20];
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            NombreAcceso = nombreAcceso,
            Nombre = "Usuario de prueba",
            Email = $"{nombreAcceso}@example.test",
            PasswordHash = new ServicioContrasenas(new OpcionesSeguridad
            {
                ClaveJwt = new byte[64],
                RondasBcrypt = OpcionesSeguridad.RondasBcryptMinimas,
            }).Hashar(contrasena),
            Activo = activo,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        contexto.Usuario.Add(usuario);
        contexto.UsuarioRol.Add(new UsuarioRol
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            RolClave = rol,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync(ct);

        return usuario;
    }

    public static List<CookieFijada> CookiesDe(HttpResponseMessage respuesta)
    {
        if (!respuesta.Headers.TryGetValues("Set-Cookie", out var valores))
        {
            return [];
        }

        return
        [
            .. valores.Select(cabecera => Parsear(cabecera))
                .Where(cookie => !string.IsNullOrEmpty(cookie.Nombre))
        ];
    }

    public static CookieFijada Cookie(HttpResponseMessage respuesta, string nombre) =>
        CookiesDe(respuesta).Single(cookie => cookie.Nombre == nombre);

    private static CookieFijada Parsear(string cabecera)
    {
        var pareja = cabecera.Split(';')[0].Split('=', 2);
        return new CookieFijada(pareja[0], pareja[1], cabecera);
    }

    public static Task<HttpResponseMessage> LoginAsync(
        HttpClient cliente,
        string nombreAcceso,
        string contrasena,
        CancellationToken ct) =>
        cliente.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { nombreAcceso, contrasena },
            ct);

    /// <summary>Los tres valores de las cookies que devuelve un login o un refresco.</summary>
    public static (string Acceso, string Csrf, string Refresco) TokensDe(HttpResponseMessage respuesta) =>
        (Cookie(respuesta, NombresCookies.Acceso).Valor,
         Cookie(respuesta, NombresCookies.Csrf).Valor,
         Cookie(respuesta, NombresCookies.Refresco).Valor);

    /// <summary>
    /// Petición con las cookies escritas a mano. Se usa con <see cref="ApiTestFactory.CrearClienteSinCookies"/>
    /// porque un cliente con almacén de cookies manda las suyas y acabaría mandando la cookie de
    /// sesión dos veces (la buena y la que el test quiere probar).
    /// </summary>
    public static HttpRequestMessage ConCookies(
        HttpMethod metodo,
        string ruta,
        (string Nombre, string Valor)[] cookies,
        object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta)
        {
            Content = cuerpo is null
                ? new StringContent(string.Empty)
                : JsonContent.Create(cuerpo),
        };
        var csrf = Array.Find(cookies, cookie => cookie.Nombre == NombresCookies.Csrf);
        if (csrf.Valor is { Length: > 0 } valor)
        {
            peticion.Headers.Add("X-CSRF-Token", valor);
        }

        peticion.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Nombre}={cookie.Valor}")));

        return peticion;
    }
}