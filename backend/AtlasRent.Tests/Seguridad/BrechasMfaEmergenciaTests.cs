using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// SEC-025: brechas del segundo factor y del acceso de emergencia. Un test por punto.
/// <para>
/// Cada test toma una IP nueva en vez de avanzar el reloj: los fallos de varios tests sumarían hasta
/// bloquear la IP compartida, y adelantar el reloj dejaría los JWT de las sesiones con <c>nbf</c>
/// en el futuro respecto al reloj real.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class BrechasMfaEmergenciaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Motivo = "El administrador perdió el teléfono y sus códigos de recuperación";

    [Fact]
    public async Task ElSegundoFactorRespetaLaCuentaBloqueada()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();
        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);

        await FijarBloqueoAsync(admin.Id, api.Reloj.GetUtcNow().AddHours(1), ct);

        // Código correcto, cuenta bloqueada: no entra y el intento queda como fallo.
        var bloqueado = await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct);
        bloqueado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await FallosAsync(admin.Id, ct)).Should().BeGreaterThan(0);

        // El token provisional no se gastó: al levantar el bloqueo el mismo código entra.
        await FijarBloqueoAsync(admin.Id, null, ct);
        (await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CincoCodigosTotpMalosBloqueanLaCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();
        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, admin, Contrasena, api.Reloj, ct);

        for (var i = 0; i < 5; i++)
        {
            (await cliente.PostAsJsonAsync(
                "/api/v1/auth/mfa/verificar", new { codigo = AyudaMfa.CodigoTotpEquivocado(api.Reloj) }, ct))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await BloqueadaHastaAsync(admin.Id, ct)).Should().NotBeNull();
        (await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmarElAltaConCodigosMalosCuentaComoFallosYBloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();
        (await AyudaAuth.LoginAsync(cliente, admin.NombreAcceso, Contrasena, ct)).EnsureSuccessStatusCode();
        var secreto = AyudaMfa.SecretoDe(await AyudaMfa.ActivarAsync(cliente, ct));

        for (var i = 0; i < 5; i++)
        {
            (await cliente.PostAsJsonAsync(
                "/api/v1/auth/mfa/confirmar", new { codigo = AyudaMfa.CodigoTotpEquivocado(api.Reloj) }, ct))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await BloqueadaHastaAsync(admin.Id, ct)).Should().NotBeNull();

        // Bloqueada, ni el código bueno confirma el alta.
        (await cliente.PostAsJsonAsync(
            "/api/v1/auth/mfa/confirmar", new { codigo = AyudaMfa.CodigoTotp(secreto, api.Reloj) }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == admin.Id, ct))
            .MfaActivado.Should().BeFalse();
    }

    [Fact]
    public async Task EntrarConEmergenciaRespetaLaCuentaBloqueadaYLosFallosCuentan()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var (bloqueado, codigo) = await AprobarEmergenciaAsync(ct);
        using var cliente = api.CrearCliente();

        // Cuenta bloqueada tras empezar el login: el código bueno no entra.
        (await AyudaAuth.LoginAsync(cliente, bloqueado.NombreAcceso, Contrasena, ct)).EnsureSuccessStatusCode();
        await FijarBloqueoAsync(bloqueado.Id, api.Reloj.GetUtcNow().AddHours(1), ct);
        (await cliente.PostAsJsonAsync("/api/v1/auth/emergencia/entrar", new { codigo }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Levantado el bloqueo, cinco códigos malos lo vuelven a poner.
        await FijarBloqueoAsync(bloqueado.Id, null, ct);
        // rent_app no puede borrar intentos (append-only a efectos prácticos): se limpia como superusuario.
        await using (var conexion = new NpgsqlConnection(bd.CadenaSuperusuario))
        {
            await conexion.OpenAsync(ct);
            await using var borrar = new NpgsqlCommand("DELETE FROM intento_login WHERE usuario_id = @id", conexion);
            borrar.Parameters.AddWithValue("id", bloqueado.Id);
            await borrar.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < 5; i++)
        {
            (await cliente.PostAsJsonAsync("/api/v1/auth/emergencia/entrar", new { codigo = "AAAAA-BBBBB" }, ct))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await BloqueadaHastaAsync(bloqueado.Id, ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task DosAprobacionesSimultaneasNoReescribenElCodigo()
    {
        var ct = TestContext.Current.CancellationToken;
        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        var a = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var b = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var c = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var objetivo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesionA = await AyudaPermisos.SesionAsync(api, a, Contrasena, ct);
        using var sesionB = await AyudaPermisos.SesionAsync(api, b, Contrasena, ct);
        using var sesionC = await AyudaPermisos.SesionAsync(api, c, Contrasena, ct);

        var solicitud = await sesionA.PostAsync(
            "/api/v1/auth/emergencia/solicitar", new { usuarioObjetivoId = objetivo.Id, motivo = Motivo }, ct);
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var respuestas = await Task.WhenAll(
            sesionB.PostAsync("/api/v1/auth/emergencia/aprobar", new { solicitudId = id }, ct),
            sesionC.PostAsync("/api/v1/auth/emergencia/aprobar", new { solicitudId = id }, ct));

        respuestas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var ganadora = respuestas.Single(r => r.StatusCode == HttpStatusCode.OK);
        var codigo = (await ganadora.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("codigoAcceso").GetString()!;

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.AccesoEmergencia.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        fila.CodigoHash.Should().Be(EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo)));
    }

    [Theory]
    [InlineData(Acciones.MfaObligatorio)]
    [InlineData(Acciones.SeguridadAccesoEmergencia)]
    public async Task UnAdminNoPuedeQuitarLasSalvaguardasAOtroAdmin(string accion)
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var otro = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, a, Contrasena, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/usuarios/{otro.Id}/permisos", new { accion, permitido = false }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.UsuarioPermiso.AnyAsync(p => p.UsuarioId == otro.Id && p.Accion == accion, ct))
            .Should().BeFalse();
    }

    [Fact]
    public async Task NoSeQuitaElRolAlUltimoAdminActivoNiAUnoMismo()
    {
        var ct = TestContext.Current.CancellationToken;
        var unico = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, unico, Contrasena, ct);
        var desactivados = await DesactivarOtrosAdminsAsync(unico.Id, ct);

        try
        {
            var respuesta = await sesion.PutAsync(
                $"/api/v1/usuarios/{unico.Id}/rol", new { rol = Roles.Gerente }, ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await RolAsync(unico.Id, ct)).Should().Be(Roles.Admin);
        }
        finally
        {
            await ReactivarAsync(desactivados, ct);
        }
    }

    [Fact]
    public async Task ConOtroAdminActivoSiSePuedeQuitarElRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var otro = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, a, Contrasena, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/usuarios/{otro.Id}/rol", new { rol = Roles.Gerente }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RolAsync(otro.Id, ct)).Should().Be(Roles.Gerente);
    }

    /// <summary>Dos administradores aprueban una solicitud para un tercero; devuelve el tercero y su código.</summary>
    private async Task<(Usuario Bloqueado, string Codigo)> AprobarEmergenciaAsync(CancellationToken ct)
    {
        var a = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var b = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var bloqueado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesionA = await AyudaPermisos.SesionAsync(api, a, Contrasena, ct);
        using var sesionB = await AyudaPermisos.SesionAsync(api, b, Contrasena, ct);

        var solicitud = await sesionA.PostAsync(
            "/api/v1/auth/emergencia/solicitar", new { usuarioObjetivoId = bloqueado.Id, motivo = Motivo }, ct);
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var aprobacion = await sesionB.PostAsync("/api/v1/auth/emergencia/aprobar", new { solicitudId = id }, ct);
        var codigo = (await aprobacion.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("codigoAcceso").GetString()!;

        return (bloqueado, codigo);
    }

    private async Task FijarBloqueoAsync(Guid usuarioId, DateTimeOffset? hasta, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        await contexto.Usuario.Where(u => u.Id == usuarioId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.BloqueadaHasta, hasta), ct);
    }

    private async Task<DateTimeOffset?> BloqueadaHastaAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return (await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == usuarioId, ct)).BloqueadaHasta;
    }

    private async Task<int> FallosAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.IntentoLogin.CountAsync(i => i.UsuarioId == usuarioId && !i.Exito, ct);
    }

    private async Task<string> RolAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return (await contexto.UsuarioRol.AsNoTracking().SingleAsync(r => r.UsuarioId == usuarioId, ct)).RolClave;
    }

    /// <summary>
    /// Desactiva a los demás administradores de la base compartida para que el probado sea el
    /// único activo. Devuelve sus ids para reactivarlos en el <c>finally</c>.
    /// </summary>
    private async Task<List<Guid>> DesactivarOtrosAdminsAsync(Guid conservar, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            UPDATE usuario SET activo = false
             WHERE activo AND deleted_at IS NULL AND id <> @id
               AND id IN (SELECT usuario_id FROM usuario_rol WHERE rol_clave = 'admin')
            RETURNING id
            """, conexion);
        comando.Parameters.AddWithValue("id", conservar);

        var ids = new List<Guid>();
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            ids.Add(lector.GetGuid(0));
        }

        return ids;
    }

    private async Task ReactivarAsync(List<Guid> ids, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("UPDATE usuario SET activo = true WHERE id = ANY(@ids)", conexion);
        comando.Parameters.AddWithValue("ids", ids.ToArray());
        await comando.ExecuteNonQueryAsync(ct);
    }
}
