using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// Máximo absoluto de 12 horas. Rotar el token lo hereda: refrescar no alarga la sesión, por muy
/// activo que esté el usuario.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MaximoAbsolutoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task Pasadas12HorasLaSesionSeRevocaAunqueSeRefresqueElToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = AyudaAuth.Cookie(login, NombresCookies.Csrf).Valor;

        // Dentro de las 12 horas, refrescar funciona y la sesión rotada conserva el mismo límite
        // absoluto que la original.
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var dentro = await EnviarRefresco(cliente, csrf, ct);
        dentro.StatusCode.Should().Be(HttpStatusCode.OK);
        csrf = AyudaAuth.Cookie(dentro, NombresCookies.Csrf).Valor;

        api.Reloj.Avanzar(TimeSpan.FromHours(12));

        var pasado = await EnviarRefresco(cliente, csrf, ct);

        pasado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await pasado.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.RefrescoInvalido);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var sesiones = await contexto.Sesion.Where(s => s.UsuarioId == usuario.Id).ToListAsync(ct);
        sesiones.Should().HaveCount(2);
        sesiones.Should().OnlyContain(s => s.RevocadaAt != null);
        // La rotada conserva su motivo de rotación; la que estaba en uso cae por máximo absoluto.
        sesiones.Should().Contain(s => s.MotivoRevocacion == MotivosRevocacion.MaximoAbsoluto);
    }

    private static Task<HttpResponseMessage> EnviarRefresco(HttpClient cliente, string csrf, CancellationToken ct)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent(string.Empty),
        };
        peticion.Headers.Add("X-CSRF-Token", csrf);

        return cliente.SendAsync(peticion, ct);
    }
}