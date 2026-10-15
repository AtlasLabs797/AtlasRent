using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// Caducidad por inactividad. El tiempo se mueve con el reloj inyectado, así que el test no
/// espera 30 minutos.
/// <para>
/// El corte se comprueba en el refresco porque es donde cae una sesión inactiva en la vida real:
/// con un JWT de 15 minutos, toda petición de más de 15 minutos ya recibe 401 por token caducado.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InactividadTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task SesionSinActividadMasDe30MinutosNoSePuedeRefrescar()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = AyudaAuth.Cookie(login, NombresCookies.Csrf).Valor;

        // Un refresco a los 10 minutos entra por la ventana de los 30 y, al heredar la última
        // actividad del login, sigue sin reiniciarla: rotar el token no es actividad.
        api.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        var aLosDiez = await EnviarRefresco(cliente, csrf, ct);
        aLosDiez.StatusCode.Should().Be(HttpStatusCode.OK);
        // El refresco rota también el token de CSRF, así que el siguiente va con el nuevo.
        csrf = AyudaAuth.Cookie(aLosDiez, NombresCookies.Csrf).Valor;

        api.Reloj.Avanzar(TimeSpan.FromMinutes(21));

        var demasiadoTarde = await EnviarRefresco(cliente, csrf, ct);

        demasiadoTarde.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // El refresco no dice por qué falla (mismo código que un token desconocido); lo que decide
        // la regla es el motivo con el que queda revocada en la base de datos, comprobado abajo.
        (await demasiadoTarde.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.RefrescoInvalido);

        // No basta con responder 401: toda la familia queda revocada en la base de datos.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var sesiones = await contexto.Sesion.Where(s => s.UsuarioId == usuario.Id).ToListAsync(ct);
        sesiones.Should().HaveCount(2);
        sesiones.Should().OnlyContain(s => s.RevocadaAt != null);
        // La rotada conserva su motivo de rotación; la que estaba en uso cae por inactividad.
        sesiones.Should().Contain(s => s.MotivoRevocacion == MotivosRevocacion.Inactividad);
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