using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// SEC-024: el interruptor de configuración que hace el MFA obligatorio también para gerentes
/// (docs/ESPECIFICACION.md D4). Apagado por defecto; solo lo cambia el administrador y queda
/// auditado.
/// <para>
/// El ajuste es una sola fila compartida por toda la base de pruebas, así que cada test lo deja
/// apagado al terminar.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MfaGerenteTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ConElInterruptorApagadoElGerenteEntraSinMfa()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        await FijarInterruptorAsync(false, ct);
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        using var cliente = api.CrearCliente();

        var login = await AyudaAuth.LoginAsync(cliente, gerente.NombreAcceso, Contrasena, ct);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<JsonElement>(ct)).TryGetProperty("requiereMfa", out _)
            .Should().BeFalse();
        AyudaAuth.CookiesDe(login).Should().Contain(c => c.Nombre == NombresCookies.Acceso);
    }

    [Fact]
    public async Task ConElInterruptorEncendidoElGerenteSoloPuedeConfigurarElMfa()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        await FijarInterruptorAsync(true, ct);

        try
        {
            using var cliente = api.CrearCliente();
            var login = await AyudaAuth.LoginAsync(cliente, gerente.NombreAcceso, Contrasena, ct);

            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var cuerpo = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
            cuerpo.GetProperty("requiereMfa").GetBoolean().Should().BeTrue();
            cuerpo.GetProperty("requiereConfiguracion").GetBoolean().Should().BeTrue();
            AyudaAuth.CookiesDe(login).Should().NotContain(c => c.Nombre == NombresCookies.Acceso);

            // Sin sesión: solo el token provisional, que habilita el alta del segundo factor.
            (await cliente.GetAsync("/api/v1/config/ajustes", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var secreto = AyudaMfa.SecretoDe(await AyudaMfa.ActivarAsync(cliente, ct));
            await AyudaMfa.ConfirmarAsync(cliente, secreto, api.Reloj, ct);
            (await AyudaMfa.EntrarConTotpAsync(cliente, secreto, api.Reloj, ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await FijarInterruptorAsync(false, ct);
        }
    }

    [Fact]
    public async Task SoloElAdminCambiaElInterruptorYQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        await FijarInterruptorAsync(false, ct);
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesionGerente = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);
        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        try
        {
            (await sesionGerente.PutAsync("/api/v1/config/ajustes", Cuerpo(true), ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await LeerAsync(sesionAdmin, ct)).Should().BeFalse("por defecto está apagado");

            var cambio = await sesionAdmin.PutAsync("/api/v1/config/ajustes", Cuerpo(true), ct);
            cambio.StatusCode.Should().Be(HttpStatusCode.OK);
            (await cambio.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("mfaGerente").GetBoolean()
                .Should().BeTrue();
            (await LeerAsync(sesionAdmin, ct)).Should().BeTrue();

            await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
            await conexion.OpenAsync(ct);
            await using var comando = new NpgsqlCommand(
                "SELECT count(*) FROM auditoria WHERE accion = 'ajuste_general.actualizado' "
                + "AND usuario_id = @u AND datos::text LIKE '%\"MfaGerente\": true%'", conexion);
            comando.Parameters.AddWithValue("u", admin.Id);
            ((long)(await comando.ExecuteScalarAsync(ct))!).Should().Be(1);
        }
        finally
        {
            await FijarInterruptorAsync(false, ct);
        }
    }

    private static object Cuerpo(bool mfaGerente) =>
        new { modoRecordatorios = "Manual", mfaGerente };

    private static async Task<bool> LeerAsync(SesionCliente sesion, CancellationToken ct) =>
        (await (await sesion.GetAsync("/api/v1/config/ajustes", ct)).Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("mfaGerente").GetBoolean();

    private async Task FijarInterruptorAsync(bool valor, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("UPDATE ajuste_general SET mfa_gerente = @v", conexion);
        comando.Parameters.AddWithValue("v", valor);
        await comando.ExecuteNonQueryAsync(ct);
    }
}
