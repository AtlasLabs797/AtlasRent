using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// BUG-044: peticiones simultaneas de una misma sesion y refrescos simultaneos del mismo token no
/// pueden acabar en 500.
/// <para>
/// Ojo: el reloj simulado se comparte en la clase y el JWT se valida con el reloj real, asi que un
/// test que adelante el reloj deja los JWT posteriores con <c>nbf</c> en el futuro. Los que avanzan
/// mas de lo que el JWT aguanta no usan la API autenticada y viven en <c>InactividadTests</c>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class SesionesConcurrenciaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    private static async Task<HttpResponseMessage[]> EnParaleloAsync(
        int n, Func<int, Task<HttpResponseMessage>> envio)
    {
        using var barrera = new Barrier(n);
        var tareas = Enumerable.Range(0, n).Select(i => Task.Run(async () =>
        {
            barrera.SignalAndWait();
            return await envio(i);
        }));

        return await Task.WhenAll(tareas);
    }

    [Fact]
    public async Task DiezPeticionesSimultaneasConCacheFriaNoDan500YAnotanLaActividad()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearClienteSinCookies();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var (acceso, csrf, _) = AyudaAuth.TokensDe(login);

        // Dentro de los 30 minutos y con mas de 30 segundos desde la ultima actividad: todas las
        // peticiones intentan anotar la actividad a la vez, y la cache no tiene la sesion.
        api.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var respuestas = await EnParaleloAsync(10, _ => cliente.SendAsync(
            AyudaAuth.ConCookies(HttpMethod.Get, "/api/v1/auth/yo",
                [(NombresCookies.Acceso, acceso), (NombresCookies.Csrf, csrf)]),
            ct));

        respuestas.Select(r => r.StatusCode).Should().OnlyContain(c => c == HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var sesion = await contexto.Sesion.AsNoTracking().SingleAsync(s => s.UsuarioId == usuario.Id, ct);
        sesion.UltimaActividad.Should().BeCloseTo(api.Reloj.GetUtcNow(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DosRefrescosSimultaneosDelMismoTokenDanUnoOkYOtro401()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var ronda = 0; ronda < 3; ronda++)
        {
            var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
            using var cliente = api.CrearClienteSinCookies();
            var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var (acceso, csrf, refresco) = AyudaAuth.TokensDe(login);

            var respuestas = await EnParaleloAsync(2, _ => cliente.SendAsync(
                AyudaAuth.ConCookies(HttpMethod.Post, "/api/v1/auth/refresh",
                    [(NombresCookies.Acceso, acceso), (NombresCookies.Csrf, csrf), (NombresCookies.Refresco, refresco)]),
                ct));

            respuestas.Select(r => r.StatusCode).Order().Should()
                .Equal([HttpStatusCode.OK, HttpStatusCode.Unauthorized]);

            // Solo una sesion nueva: el perdedor no emitio nada.
            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            var sesiones = await contexto.Sesion.AsNoTracking().Where(s => s.UsuarioId == usuario.Id).ToListAsync(ct);
            sesiones.Should().HaveCount(2);
            sesiones.Count(s => s.RevocadaAt == null).Should().Be(1);
        }
    }
}
