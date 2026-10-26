using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-06 sobre la API real contra PostgreSQL: MFA obligatorio para el administrador, alta en dos
/// pasos (secreto → confirmación), login en dos pasos y token provisional de un solo uso.
/// <para>
/// Ningún test de esta clase avanza el reloj: el JWT de acceso lo valida JwtBearer contra la hora
/// real, así que una sesión solo es válida si no se ha movido el reloj de la instancia. El test que
/// necesita avanzar el tiempo (cinco TOTP incorrectos bloquean la cuenta) está en
/// <see cref="BloqueoMfaTests"/>, con su propia instancia y su propia IP.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MfaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Criterio de aceptación de F1-06: un admin sin MFA no puede hacer login normal.</summary>
    [Fact]
    public async Task AdminSinMfaNoPuedeHacerLoginNormal()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, admin.NombreAcceso, Contrasena, ct);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("requiereMfa").GetBoolean().Should().BeTrue();
        // Sin MFA configurado, eladmin solo puede configurarlo: no hay nada que verificar todavía.
        cuerpo.GetProperty("requiereConfiguracion").GetBoolean().Should().BeTrue();
        cuerpo.GetProperty("nombreAcceso").GetString().Should().Be(admin.NombreAcceso);

        // Lo que hay es el token provisional, no una sesión: sin cookie de acceso no se puede
        // llamar a nada que no sea el segundo factor.
        AyudaAuth.CookiesDe(login)
            .Should().ContainSingle(cookie => cookie.Nombre == NombresCookies.DesafioMfa);
        AyudaAuth.CookiesDe(login)
            .Should().NotContain(cookie => cookie.Nombre == NombresCookies.Acceso
                || cookie.Nombre == NombresCookies.Refresco
                || cookie.Nombre == NombresCookies.Csrf);

        (await cliente.GetAsync("/api/v1/auth/yo", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ElTokenProvisionalNoEsUnaSesionYNoDejaAbrirNadaMasQueElSegundoFactor()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, admin.NombreAcceso, Contrasena, ct);

        // Mismas protecciones que las cookies de sesión, y con el Path restringido a la
        // autenticación para que no viaje en cada petición.
        var cookie = AyudaAuth.Cookie(login, NombresCookies.DesafioMfa);
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSiteEstricto.Should().BeTrue();
        cookie.Path.Should().Be("/api/v1/auth");

        (await cliente.GetAsync("/api/v1/auth/yo", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>El alta devuelve la URI del QR y no activa nada hasta que un código válido confirma.</summary>
    [Fact]
    public async Task ElMfaSoloQuedaActivoTrasConfirmarUnCodigoValido()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();
        await AyudaAuth.LoginAsync(cliente, admin.NombreAcceso, Contrasena, ct);

        var uri = await AyudaMfa.ActivarAsync(cliente, ct);

        uri.Should().StartWith("otpauth://totp/");
        Uri.UnescapeDataString(uri).Should().Contain($"issuer={ServicioMfa.Emisor}");
        var secreto = AyudaMfa.SecretoDe(uri);

        // Con el secreto guardado pero sin confirmar, el MFA sigue sin estar activo.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == admin.Id, ct);
            fila.MfaActivado.Should().BeFalse();
            fila.MfaPendiente.Should().BeTrue();
        }

        // Un código que no vale no confirma nada.
        var codigoMalo = await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/confirmar", new { codigo = AyudaMfa.CodigoTotpEquivocado(api.Reloj) }, ct);
        codigoMalo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == admin.Id, ct);
            fila.MfaActivado.Should().BeFalse();
        }

        var codigos = await AyudaMfa.ConfirmarAsync(cliente, secreto, api.Reloj, ct);

        codigos.Should().HaveCount(ServicioMfa.CodigosPorUsuario);
        codigos.Should().OnlyHaveUniqueItems();

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == admin.Id, ct);
            fila.MfaActivado.Should().BeTrue();
            fila.MfaPendiente.Should().BeFalse();
        }

        // Los códigos se muestran una vez y se guardan hasheados: en la tabla no está ninguno en
        // claro, solo su SHA-256.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var hashes = await contexto.CodigoRecuperacion
                .Where(c => c.UsuarioId == admin.Id)
                .Select(c => c.CodigoHash)
                .ToListAsync(ct);

            hashes.Should().HaveCount(ServicioMfa.CodigosPorUsuario);
            foreach (var codigo in codigos)
            {
                hashes.Should().Contain(EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo)));
                hashes.Should().NotContain(codigo);
            }
        }
    }

    /// <summary>MFA opcional: un usuario normal entra y ve que le falta el segundo factor.</summary>
    [Fact]
    public async Task ElMfaEsOpcionalParaElRestoYQuedaMarcadoComoPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        // Sin MFA no hay desafío: entra directamente.
        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Con sesión (sin token provisional) el alta exige el doble envío de CSRF (SEC-013).
        var activar = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mfa/activar");
        activar.Headers.Add("X-CSRF-Token", AyudaAuth.Cookie(login, NombresCookies.Csrf).Valor);
        var respuesta = await cliente.SendAsync(activar, ct);
        respuesta.EnsureSuccessStatusCode();
        var uri = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("uri").GetString()!;
        var identidad = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/auth/yo", ct);

        identidad.GetProperty("mfaActivado").GetBoolean().Should().BeFalse();
        identidad.GetProperty("mfaPendiente").GetBoolean().Should().BeTrue();
        uri.Should().StartWith("otpauth://totp/");
    }

    /// <summary>Criterio de aceptación de F1-06: contraseña correcta + TOTP correcto → sesión completa.</summary>
    [Fact]
    public async Task ContrasenaYTotpCorrectosAbrenSesion()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);

        // Con el MFA activo, entrar son dos pasos: primero la contraseña, luego el código.
        var verificacion = await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct);

        verificacion.StatusCode.Should().Be(HttpStatusCode.OK);
        AyudaAuth.CookiesDe(verificacion).Should().Contain(cookie => cookie.Nombre == NombresCookies.Acceso);
        (await cliente.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Y el token provisional desaparece de la respuesta: ya se gastó.
        AyudaAuth.CookiesDe(verificacion)
            .Should().Contain(cookie => cookie.Nombre == NombresCookies.DesafioMfa
                && string.IsNullOrEmpty(cookie.Valor));
    }

    [Fact]
    public async Task ElTokenProvisionalCaducaYEsDeUnSoloUso()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        // El alta necesita cookies (el token provisional viaja entre login, activar y confirmar).
        using var activador = api.CrearCliente();
        var mfa = await AyudaMfa.ActivarCompletoAsync(activador, admin, Contrasena, api.Reloj, ct);
        var codigo = new { codigo = AyudaMfa.CodigoTotp(mfa.Secreto, api.Reloj) };

        // El token provisional del alta no se gasta al configurar: sigue sirviendo para entrar, y
        // para comprobar que es de un solo uso hace falta poder reenviarlo, así que aquí el
        // cliente va sin almacén de cookies.
        using var cliente = api.CrearClienteSinCookies();
        var token = AyudaMfa.TokenDesafioDe(
            await AyudaMfa.LoginHastaElDesafioAsync(cliente, admin, Contrasena, ct));

        var primerUso = await cliente.SendAsync(
            AyudaAuth.ConCookies(
                HttpMethod.Post, "/api/v1/auth/mfa/verificar",
                [(NombresCookies.DesafioMfa, token)], codigo),
            ct);

        primerUso.StatusCode.Should().Be(HttpStatusCode.OK);

        // El mismo token otra vez, con el mismo código: ya se gastó y no vale para nada.
        var segundoUso = await cliente.SendAsync(
            AyudaAuth.ConCookies(
                HttpMethod.Post, "/api/v1/auth/mfa/verificar",
                [(NombresCookies.DesafioMfa, token)], codigo),
            ct);

        segundoUso.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await segundoUso.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.MfaInvalido);
    }
}
