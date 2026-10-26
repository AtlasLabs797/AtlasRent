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
/// F1-06: códigos de recuperación. Entran cuando se perdió el dispositivo, así que sustituyen al
/// TOTP, y son de un solo uso.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CodigosRecuperacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task UnCodigoDeRecuperacionValidoEntraYNoSePuedeReutilizar()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);
        var codigo = mfa.CodigosRecuperacion[0];

        await AyudaMfa.LoginConDesafioAsync(cliente, admin, Contrasena, ct);
        var entrada = await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/recuperacion", new { codigo = codigo.ToLowerInvariant() }, ct);

        // Entra: el código es válido y en minúsculas también, porque el usuario lo teclea como puede.
        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // En la base de datos el código queda gastado con su fecha.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var hash = ServicioMfa.HashCodigoRecuperacion(api.ClaveHmacDocumento, ServicioMfa.Normalizar(codigo));
            var fila = await contexto.CodigoRecuperacion
                .SingleAsync(c => c.UsuarioId == admin.Id && c.CodigoHash == hash, ct);
            fila.UsadoAt.Should().NotBeNull();
        }

        // Y con el mismo código ya no se entra: hay que empezar el login otra vez.
        await AyudaMfa.LoginConDesafioAsync(cliente, admin, Contrasena, ct);
        var reutilizado = await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/recuperacion", new { codigo }, ct);

        reutilizado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await reutilizado.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.MfaInvalido);
    }

    [Fact]
    public async Task LosCodigosDeRecuperacionSirvenUnoPorSesionYNoSirvenLosDeOtroUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var otro = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);
        var codigo = mfa.CodigosRecuperacion[1];

        await AyudaMfa.LoginConDesafioAsync(cliente, otro, Contrasena, ct);
        var deOtro = await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/recuperacion", new { codigo }, ct);

        deOtro.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Los diez del primer usuario siguen ahí: el intento ajeno no gastó ninguno.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var sinUsar = await contexto.CodigoRecuperacion
            .Where(c => c.UsuarioId == admin.Id && c.UsadoAt == null)
            .CountAsync(ct);

        sinUsar.Should().Be(ServicioMfa.CodigosPorUsuario);
    }

    /// <summary>
    /// SEC-030: lo guardado es un HMAC con la clave del servidor y marcador de version, no el SHA-256
    /// a secas que se podia atacar por fuerza bruta con la tabla robada.
    /// </summary>
    [Fact]
    public async Task LosCodigosNuevosSeGuardanConHmacYNoConSha256()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var hashes = await contexto.CodigoRecuperacion
            .Where(c => c.UsuarioId == admin.Id)
            .Select(c => c.CodigoHash)
            .ToListAsync(ct);

        hashes.Should().OnlyContain(h => h.StartsWith(ServicioMfa.PrefijoHashCodigo));
        foreach (var codigo in mfa.CodigosRecuperacion)
        {
            hashes.Should().NotContain(EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo)));
        }
    }

    /// <summary>SEC-030: los codigos emitidos antes (SHA-256 sin marcador) siguen valiendo hasta gastarse.</summary>
    [Fact]
    public async Task UnCodigoEmitidoConElHashAnteriorSigueEntrando()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);
        var codigo = mfa.CodigosRecuperacion[2];

        // Se deja ese codigo tal y como lo guardaba la version anterior.
        await using (var conexion = new Npgsql.NpgsqlConnection(bd.CadenaSuperusuario))
        {
            await conexion.OpenAsync(ct);
            await using var orden = new Npgsql.NpgsqlCommand(
                "UPDATE codigo_recuperacion SET codigo_hash = @antiguo WHERE usuario_id = @u AND codigo_hash = @nuevo",
                conexion);
            orden.Parameters.AddWithValue("antiguo", EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo)));
            orden.Parameters.AddWithValue("u", admin.Id);
            orden.Parameters.AddWithValue(
                "nuevo", ServicioMfa.HashCodigoRecuperacion(api.ClaveHmacDocumento, ServicioMfa.Normalizar(codigo)));
            (await orden.ExecuteNonQueryAsync(ct)).Should().Be(1);
        }

        await AyudaMfa.LoginConDesafioAsync(cliente, admin, Contrasena, ct);
        var entrada = await cliente.PostAsJsonAsync("/api/v1/auth/mfa/recuperacion", new { codigo }, ct);

        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
