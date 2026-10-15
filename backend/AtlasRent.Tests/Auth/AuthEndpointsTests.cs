using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// F1-04 sobre la API real contra PostgreSQL: login, cookies, refresco rotatorio, detección de
/// reutilización y respuesta genérica de credenciales.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AuthEndpointsTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task LoginCorrectoFijaLasCookiesDeAccesoRefrescoYCsrf()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var respuesta = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var acceso = AyudaAuth.Cookie(respuesta, NombresCookies.Acceso);
        acceso.HttpOnly.Should().BeTrue();
        acceso.Secure.Should().BeTrue();
        acceso.SameSiteEstricto.Should().BeTrue();
        acceso.Path.Should().Be("/");
        acceso.Valor.Should().NotBeNullOrWhiteSpace();

        var refresco = AyudaAuth.Cookie(respuesta, NombresCookies.Refresco);
        refresco.HttpOnly.Should().BeTrue();
        refresco.Secure.Should().BeTrue();
        refresco.SameSiteEstricto.Should().BeTrue();
        // El token de refresco solo viaja a las rutas de autenticación.
        refresco.Path.Should().Be("/api/v1/auth");

        var csrf = AyudaAuth.Cookie(respuesta, NombresCookies.Csrf);
        csrf.Valor.Should().NotBeNullOrWhiteSpace();
        csrf.SameSiteEstricto.Should().BeTrue();
        // Sin HttpOnly a propósito: es double-submit y el navegador tiene que poder leerlo.
        csrf.HttpOnly.Should().BeFalse();

        // La contraseña no está en la base de datos en ningún sitio, solo su hash.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var hash = await contexto.Usuario
            .Where(u => u.Id == usuario.Id)
            .Select(u => u.PasswordHash)
            .SingleAsync(ct);
        hash.Should().NotBe(Contrasena).And.NotContain(Contrasena);
    }

    /// <summary>Criterio de aceptación de F1-04: usuario desactivado → 401 en la siguiente petición.</summary>
    [Fact]
    public async Task UsuarioDesactivadoDevuelve401EnLaSiguientePeticion()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Se desactiva la cuenta por la vía real: otra conexión a la base de datos.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == usuario.Id, ct);
            fila.Activo = false;
            await contexto.SaveChangesAsync(ct);
        }

        var yo = await cliente.GetAsync("/api/v1/auth/yo", ct);

        yo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await yo.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.SesionNoValida);
    }

    [Fact]
    public async Task ElRefrescoEmiteUnTokenNuevoYElAnteriorQuedaInutilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearClienteSinCookies();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        var (accesoAntiguo, csrf, refrescoAntiguo) = AyudaAuth.TokensDe(login);

        var refresco = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                [(NombresCookies.Acceso, accesoAntiguo), (NombresCookies.Csrf, csrf), (NombresCookies.Refresco, refrescoAntiguo)]),
            ct);

        refresco.StatusCode.Should().Be(HttpStatusCode.OK);
        var (accesoNuevo, csrfNuevo, refrescoNuevo) = AyudaAuth.TokensDe(refresco);
        refrescoNuevo.Should().NotBe(refrescoAntiguo);

        // El token nuevo vale...
        var yo = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Get, "/api/v1/auth/yo",
                [(NombresCookies.Acceso, accesoNuevo), (NombresCookies.Csrf, csrfNuevo)]),
            ct);
        yo.StatusCode.Should().Be(HttpStatusCode.OK);

        // ...y el antiguo ya no.
        var reutilizado = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                [(NombresCookies.Acceso, accesoNuevo), (NombresCookies.Csrf, csrfNuevo), (NombresCookies.Refresco, refrescoAntiguo)]),
            ct);

        reutilizado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReutilizarUnTokenDeRefrescoRevocaLaFamiliaCompleta()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearClienteSinCookies();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        var (accesoAntiguo, csrf, refrescoAntiguo) = AyudaAuth.TokensDe(login);

        var refresco = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                [(NombresCookies.Acceso, accesoAntiguo), (NombresCookies.Csrf, csrf), (NombresCookies.Refresco, refrescoAntiguo)]),
            ct);
        refresco.StatusCode.Should().Be(HttpStatusCode.OK);
        var (accesoNuevo, csrfNuevo, refrescoNuevo) = AyudaAuth.TokensDe(refresco);

        // Reutilizar un token ya rotado: se cae la familia entera.
        var robo = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                [(NombresCookies.Acceso, accesoNuevo), (NombresCookies.Csrf, csrfNuevo), (NombresCookies.Refresco, refrescoAntiguo)]),
            ct);
        robo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // El token bueno también cae: la familia ya no vale.
        var trasElRobo = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                [(NombresCookies.Acceso, accesoNuevo), (NombresCookies.Csrf, csrfNuevo), (NombresCookies.Refresco, refrescoNuevo)]),
            ct);
        trasElRobo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var sesiones = await contexto.Sesion.Where(s => s.UsuarioId == usuario.Id).ToListAsync(ct);
        sesiones.Should().HaveCount(2);
        sesiones.Should().OnlyContain(s => s.RevocadaAt != null);
        sesiones.Should().Contain(s => s.MotivoRevocacion == MotivosRevocacion.ReutilizacionDetectada);
    }

    [Fact]
    public async Task ElLoginSinFalloNoDistingueEntreUsuarioInexistenteYContrasenaIncorrecta()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var inexistente = await AyudaAuth.LoginAsync(cliente, "nadie.con.este.nombre", Contrasena, ct);
        var incorrecta = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, "otra-contrasena", ct);

        incorrecta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        incorrecta.StatusCode.Should().Be(inexistente.StatusCode);

        var cuerpoIncorrecta = await incorrecta.Content.ReadAsStringAsync(ct);
        var cuerpoInexistente = await inexistente.Content.ReadAsStringAsync(ct);

        cuerpoIncorrecta.Should().Be(cuerpoInexistente);
        cuerpoIncorrecta.Should().Contain(Problemas.CredencialesInvalidas);
        // Sin cookie de sesión en ninguna de las dos respuestas.
        AyudaAuth.CookiesDe(inexistente).Should().BeEmpty();
        AyudaAuth.CookiesDe(incorrecta).Should().BeEmpty();
    }

    [Fact]
    public async Task ElNombreDeAccesoSeNormalizaAMinusculas()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(
            cliente, usuario.NombreAcceso.ToUpperInvariant(), Contrasena, ct);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnPostSinCabeceraCsrfSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();
        await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

        var respuesta = await cliente.PostAsync("/api/v1/auth/logout", null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.CsrfInvalido);
    }
}