using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// La fábrica normal <b>no</b> puede traer puesto el límite de login de producción (SEG-002: 20
/// por minuto y por IP).
/// <para>
/// El límite es por IP y la IP es una por instancia de fábrica, o sea una por clase: las pruebas de
/// una clase comparten instancia, IP y ventana de un minuto. El login con MFA cuenta dos veces
/// (entrar y verificar el segundo factor), así que una clase normal con unas diez sesiones se come
/// el límite entero y revienta con un 429 que no dice nada de lo que la prueba mide. Pasó con
/// <c>IA/PropuestaAprobacionTests</c>, que tiene diez pruebas.
/// </para>
/// <para>
/// El límite estrecho de <c>LimitesPeticionesTests</c> es el único que tiene que existir, y vive en
/// su propia fábrica: aquí lo que se comprueba es justo lo contrario, que la fábrica de uso general
/// deja pasar una batería de logins.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class LoginFabricaNormalTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Más de 20 logins seguidos contra la misma instancia. Con el límite de producción, el número
    /// 21 sale con 429 y la prueba falla; con la fábrica normal, ninguno.
    /// </summary>
    [Fact]
    public async Task LaFabricaNormalNoCortaLaBateriaDeLoginsDeUnaClase()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var cliente = api.CrearCliente();

        for (var intento = 1; intento <= 25; intento++)
        {
            var respuesta = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

            respuesta.StatusCode.Should().Be(
                HttpStatusCode.OK,
                $"el límite de login no está probando aquí: la fábrica normal tiene que "
                    + $"dejar pasar los logins de una clase entera ({intento} de 25)");
        }
    }
}