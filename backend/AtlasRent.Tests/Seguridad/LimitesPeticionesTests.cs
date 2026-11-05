using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05: superado el límite de peticiones la respuesta es 429 con <c>ProblemDetails</c> y código de
/// referencia. Los límites del checklist (300/min global, 20/min de login) no se pueden probar
/// sin miles de peticiones, así que esta fábrica los baja a 3 y 600.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class LimitesPeticionesTests(ContenedorBdFixture bd, ApiTestFactoryLimites api)
    : IClassFixture<ApiTestFactoryLimites>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task SuperadoElLimiteDeLoginDevuelve429ConCodigoDeReferencia()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        for (var peticion = 1; peticion <= 3; peticion++)
        {
            var dentro = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);
            dentro.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rechazado = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

        rechazado.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var cuerpo = await rechazado.Content.ReadAsStringAsync(ct);
        cuerpo.Should().Contain(Problemas.LimitePeticiones);
        cuerpo.Should().Contain("\"status\":429");
        // Y no dice nada más que el código: ni la cuenta, ni su estado.
        AyudaAuth.CookiesDe(rechazado).Should().BeEmpty();
    }
}