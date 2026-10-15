using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// F1-09 cierra <c>docs/SEGURIDAD.md SEC-005</c>: el secreto TOTP del usuario se guardaba en claro
/// y a partir de aquí está cifrado en reposo con AES-256-GCM.
/// <para>
/// El camino se prueba entero: activar el MFA, leer la fila cruda y entrar después con el código.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MfaSecretoCifradoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElSecretoMfaQuedaCifradoEnLaBaseDeDatosYElSegundoFactorSigueFuncionando()
    {
        var ct = TestContext.Current.CancellationToken;

        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        (await AyudaMfa.LoginConDesafioAsync(cliente, usuario, Contrasena, ct))
            .EnsureSuccessStatusCode();

        var uri = await AyudaMfa.ActivarAsync(cliente, ct);
        var secreto = AyudaMfa.SecretoDe(uri);

        // Lo que hay en la tabla no es el secreto.
        var enLaTabla = await SecretoEnLaFilaAsync(usuario.Id, ct);
        enLaTabla.Should().NotBeNull("F1-06 lo guardaba pendiente de confirmar");
        enLaTabla.Should().StartWith("v1:");
        enLaTabla.Should().NotContain(secreto, "SEC-005: el secreto TOTP no puede estar en claro");

        await AyudaMfa.ConfirmarAsync(cliente, secreto, api.Reloj, ct);

        var activo = await SecretoEnLaFilaAsync(usuario.Id, ct);
        activo.Should().StartWith("v1:");
        activo.Should().NotContain(secreto);

        // Y el login con el TOTP sigue entrando: descifrar el secreto no rompió la verificación.
        var entrada = await AyudaMfa.EntrarConTotpAsync(cliente, secreto, api.Reloj, ct);
        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        entrada.Headers.Contains("Set-Cookie").Should().BeTrue();

        // Con un código malo sigue rechazando, que es lo que un cifrado mal hecho rompería.
        var rechazo = await AyudaMfa.EntrarConTotpAsync(
            cliente, "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", api.Reloj, ct);
        rechazo.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// El secreto de la columna descifra con el servicio de la aplicación: prueba de que lo que se
    /// guarda es un valor cifrado válido y no un texto cualquiera.
    /// </summary>
    [Fact]
    public async Task ElSecretoGuardadoDescifraConElServicioDeLaAplicacion()
    {
        var ct = TestContext.Current.CancellationToken;

        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();

        (await AyudaMfa.LoginConDesafioAsync(cliente, usuario, Contrasena, ct))
            .EnsureSuccessStatusCode();

        var uri = await AyudaMfa.ActivarAsync(cliente, ct);
        var secreto = AyudaMfa.SecretoDe(uri);
        await AyudaMfa.ConfirmarAsync(cliente, secreto, api.Reloj, ct);

        var cifrado = await SecretoEnLaFilaAsync(usuario.Id, ct);

        // La clave es la misma que usa la aplicación en esta instancia: lo que hay en la columna es
        // un cifrado válido, no un texto cualquiera.
        new ServicioCifrado(new OpcionesSeguridad
        {
            ClaveJwt = new byte[64],
            ClaveCifrado = api.ClaveCifrado,
            ClaveHmacDocumento = api.ClaveHmacDocumento,
        }).Descifrar(cifrado!).Should().Be(secreto);
    }

    private async Task<string?> SecretoEnLaFilaAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT mfa_secreto FROM usuario WHERE id = @id", conexion);
        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = usuarioId });

        return (string?)await comando.ExecuteScalarAsync(ct);
    }
}