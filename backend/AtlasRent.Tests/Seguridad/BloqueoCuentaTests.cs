using System.Net;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05, aceptaciÃ³n: 5 intentos fallidos bloquean la cuenta 30 minutos.
/// <para>
/// El tiempo se mueve con el reloj inyectado, asÃ­ que el test no espera 30 minutos. Cada test
/// empieza avanzando 20 minutos para salir de la ventana de fallos por IP (15 min): si no, los
/// intentos de un test bloquearÃ­an la IP de la instancia y romperÃ­an el siguiente.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class BloqueoCuentaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string ContrasenaMala = "contrasena-equivocada";

    [Fact]
    public async Task CincoIntentosFallidosBloqueanLaCuentaYElLoginCorrectoFalla()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        for (var intento = 1; intento <= 5; intento++)
        {
            var fallo = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);
            fallo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Con la contraseÃ±a correcta tampoco entra: la cuenta estÃ¡ bloqueada.
        var conLaBuena = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

        conLaBuena.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Ni el cÃ³digo ni el mensaje dicen que el bloqueo existe: para no poder enumerar cuentas.
        (await conLaBuena.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.CredencialesInvalidas);
        AyudaAuth.CookiesDe(conLaBuena).Should().BeEmpty();

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Usuario.SingleAsync(u => u.Id == usuario.Id, ct);
        fila.BloqueadaHasta.Should().NotBeNull();
        fila.BloqueadaHasta.Should()
            .BeCloseTo(api.Reloj.GetUtcNow().AddMinutes(30), TimeSpan.FromSeconds(5));

        // Cada intento fallido queda registrado con su IP: 5 mala contraseÃ±a y el intento con la
        // buena de una cuenta ya bloqueada.
        var intentos = await contexto.IntentoLogin.Where(i => i.UsuarioId == usuario.Id).ToListAsync(ct);
        intentos.Should().HaveCount(6);
        intentos.Should().OnlyContain(i => !i.Exito && i.Ip == api.IpCliente);
    }

    [Fact]
    public async Task PasadosTreintaMinutosLaCuentaSeVuelveAUsar()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        for (var intento = 1; intento <= 5; intento++)
        {
            await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);
        }
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        api.Reloj.Avanzar(TimeSpan.FromMinutes(31));

        var trasElBloqueo = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct);

        trasElBloqueo.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var intentos = await contexto.IntentoLogin
            .Where(i => i.UsuarioId == usuario.Id)
            .ToListAsync(ct);
        intentos.Should().Contain(i => i.Exito && i.Ip == api.IpCliente);
    }

    [Fact]
    public async Task UnLoginCorrectoEntreIntentosNoCuentaComoFallido()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        for (var intento = 1; intento <= 4; intento++)
        {
            await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);
        }

        // Un acierto en medio: a partir de aquÃ­ el contador vuelve a cero.
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);
        await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Usuario.SingleAsync(u => u.Id == usuario.Id, ct);
        fila.BloqueadaHasta.Should().BeNull();

        // Y la cuenta sigue entrando: seis fallos en total, pero nunca cinco seguidos.
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
