using System.Net;
using System.Net.Http.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-06: los dos límites del token provisional y del segundo factor, que son los únicos tests de
/// MFA que mueven el reloj.
/// <para>
/// Van en su propia clase (y con su propia instancia y su propia IP) porque el JWT de acceso lo
/// valida JwtBearer contra la hora real: en una clase que advanced el reloj, ninguna sesión
/// emitida después serviría para las siguientes pruebas.
/// </para>
/// <para>
/// El test de bloqueo empieza avanzando 20 minutos para salir de la ventana de fallos por IP
/// (15 min), como hace BloqueoCuentaTests.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class DesafioMfaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElTokenProvisionalCaducaAlPasarLosCincoMinutos()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);
        var codigo = new { codigo = AyudaMfa.CodigoTotp(mfa.Secreto, api.Reloj) };

        api.Reloj.Avanzar(TimeSpan.FromMinutes(ServicioMfa.MinutosDesafio + 1));

        var caducado = await cliente.PostAsJsonAsync("/api/v1/auth/mfa/verificar", codigo, ct);

        caducado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await caducado.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.MfaInvalido);

        // Lo que falla es el token provisional, no el código: con un token nuevo el mismo código
        // entra, así que el secreto sigue siendo el bueno.
        await AyudaMfa.LoginConDesafioAsync(cliente, admin, Contrasena, ct);
        (await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Un intento con TOTP incorrecto cuenta para el bloqueo de cuenta igual que una contraseña
    /// incorrecta (docs/SEGURIDAD.md A.2).
    /// </summary>
    [Fact]
    public async Task CincoCodigosTotpIncorrectosBloqueanLaCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);

        for (var intento = 1; intento <= 5; intento++)
        {
            var fallo = await cliente.PostAsJsonAsync(
                "/api/v1/auth/mfa/verificar",
                new { codigo = AyudaMfa.CodigoTotpEquivocado(api.Reloj) },
                ct);
            fallo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Usuario.SingleAsync(u => u.Id == admin.Id, ct);
        fila.BloqueadaHasta.Should().NotBeNull();
        fila.BloqueadaHasta.Should()
            .BeCloseTo(api.Reloj.GetUtcNow().AddMinutes(30), TimeSpan.FromSeconds(5));

        // Los cinco intentos van a intento_login con su IP, igual que los de contraseña mala.
        var intentos = await contexto.IntentoLogin.Where(i => i.UsuarioId == admin.Id).ToListAsync(ct);
        intentos.Should().HaveCount(5);
        intentos.Should().OnlyContain(i => !i.Exito && i.Ip == api.IpCliente);
    }
}