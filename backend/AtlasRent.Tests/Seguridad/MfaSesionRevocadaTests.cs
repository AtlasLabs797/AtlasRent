using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// SEC-013: activar y confirmar el MFA son <c>[AllowAnonymous]</c> (el alta obligatoria del
/// administrador llega con token provisional), así que el middleware no valida la sesión. Si el
/// camino de sesión se fiase solo de la firma del JWT, un token robado de una sesión ya cerrada
/// activaría el TOTP del atacante.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MfaSesionRevocadaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Activar = "/api/v1/auth/mfa/activar";

    [Fact]
    public async Task ConUnaSesionValidaYCsrfSePuedeActivar()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var (cliente, cookies) = await EntrarAsync(usuario, ct);

        var respuesta = await cliente.SendAsync(AyudaAuth.ConCookies(HttpMethod.Post, Activar, cookies), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SecretoAsync(usuario.Id, ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task ConLaSesionCerradaActivarYConfirmarDan401YElSecretoNoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var (cliente, cookies) = await EntrarAsync(usuario, ct);

        // El titular ya tenía un alta empezada; el atacante con el JWT robado no debe pisarla.
        (await cliente.SendAsync(AyudaAuth.ConCookies(HttpMethod.Post, Activar, cookies), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var secretoDelTitular = await SecretoAsync(usuario.Id, ct);

        (await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/logout", cookies), ct))
            .EnsureSuccessStatusCode();

        var activar = await cliente.SendAsync(AyudaAuth.ConCookies(HttpMethod.Post, Activar, cookies), ct);
        var confirmar = await cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/mfa/confirmar", cookies, new { codigo = "123456" }),
            ct);

        activar.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        confirmar.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SecretoAsync(usuario.Id, ct)).Should().Be(secretoDelTitular);
    }

    [Fact]
    public async Task ConElUsuarioDesactivadoActivarDa401()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var (cliente, cookies) = await EntrarAsync(usuario, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            await contexto.Usuario.Where(u => u.Id == usuario.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Activo, false), ct);
        }

        var respuesta = await cliente.SendAsync(AyudaAuth.ConCookies(HttpMethod.Post, Activar, cookies), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SecretoAsync(usuario.Id, ct)).Should().BeNull();
    }

    [Fact]
    public async Task SinCabeceraCsrfEnElCaminoDeSesionDa401()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var (cliente, cookies) = await EntrarAsync(usuario, ct);

        var peticion = AyudaAuth.ConCookies(HttpMethod.Post, Activar, cookies);
        peticion.Headers.Remove("X-CSRF-Token");
        var respuesta = await cliente.SendAsync(peticion, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SecretoAsync(usuario.Id, ct)).Should().BeNull();
    }

    private async Task<(HttpClient Cliente, (string Nombre, string Valor)[] Cookies)> EntrarAsync(
        Usuario usuario,
        CancellationToken ct)
    {
        // Sin almacén de cookies: el test manda a mano las que quiere, como un atacante con el JWT.
        var cliente = api.CrearClienteSinCookies();
        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.EnsureSuccessStatusCode();

        return (cliente,
            [.. AyudaAuth.CookiesDe(login)
                .Where(c => c.Nombre is NombresCookies.Acceso or NombresCookies.Csrf)
                .Select(c => (c.Nombre, c.Valor))]);
    }

    private async Task<string?> SecretoAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return (await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == usuarioId, ct)).MfaSecreto;
    }
}
