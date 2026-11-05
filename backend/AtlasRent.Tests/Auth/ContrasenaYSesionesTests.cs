using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// SEC-015 (mínimo de 12 caracteres, HaveIBeenPwned y cambio de contraseña propio) y SEC-019
/// (desactivar o restablecer corta las sesiones en la siguiente petición, con la caché caliente).
/// Todo por la API real; HaveIBeenPwned es un doble, nunca la red.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContrasenaYSesionesTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Nueva = "Otra-clave-segura-2027";

    private async Task<SesionCliente> AdminAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }

    private static object Alta(string nombreAcceso, string contrasena) => new
    {
        nombreAcceso,
        nombre = "Nuevo",
        contrasena,
        rol = "empleado",
    };

    private static string NombreNuevo() => $"nuevo{Guid.NewGuid():N}"[..16];

    private async Task<string> HashAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.Usuario.Where(u => u.Id == usuarioId).Select(u => u.PasswordHash).SingleAsync(ct);
    }

    // ---- SEC-015: alta de usuario ----

    [Theory]
    [InlineData("Corta-1234")]
    [InlineData("12345678901")]
    public async Task ElAltaRechazaUnaContrasenaDeMenosDeDoceCaracteres(string corta)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AdminAsync(ct);

        var respuesta = await admin.PostAsync("/api/v1/usuarios", Alta(NombreNuevo(), corta), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ElAltaAceptaExactamenteDoceCaracteres()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AdminAsync(ct);

        var respuesta = await admin.PostAsync("/api/v1/usuarios", Alta(NombreNuevo(), "Abcdefghij-1"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElAltaRechazaUnaContrasenaDeMasDeSetentaYDosCaracteres()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AdminAsync(ct);

        var respuesta = await admin.PostAsync("/api/v1/usuarios", Alta(NombreNuevo(), new string('a', 73)), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ElAltaRechazaUnaContrasenaFiltradaYNoCreaElUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AdminAsync(ct);
        const string filtrada = "Contrasena-filtrada-1";
        api.Hibp.Filtradas.Add(filtrada);
        var nombre = NombreNuevo();

        var respuesta = await admin.PostAsync("/api/v1/usuarios", Alta(nombre, filtrada), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain("filtraciones");
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Usuario.AnyAsync(u => u.NombreAcceso == nombre, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task ElAltaConHibpCaidoNoBloqueaPeroAvisaYAudita()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AdminAsync(ct);
        api.Hibp.Caido = true;
        try
        {
            var respuesta = await admin.PostAsync("/api/v1/usuarios", Alta(NombreNuevo(), "Clave-sin-hibp-2026"), ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            cuerpo.GetProperty("contrasenaComprobada").GetBoolean().Should().BeFalse();
            var id = cuerpo.GetProperty("id").GetGuid();

            await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
            var datos = (await contexto.Auditoria
                .Where(a => a.Accion == "usuario.creado" && a.EntidadId == id.ToString("D"))
                .ToListAsync(ct)).Single().Datos!.RootElement.GetRawText();
            datos.Should().Contain("hibp").And.Contain("no_disponible");
        }
        finally
        {
            api.Hibp.Caido = false;
        }
    }

    // ---- SEC-015: cambio de la contraseña propia ----

    [Fact]
    public async Task CambiarLaContrasenaPropiaFuncionaYRevocaLasDemasSesiones()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var actual = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        using var otra = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        (await otra.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var respuesta = await actual.PostAsync(
            "/api/v1/auth/password", new { contrasenaActual = Contrasena, contrasenaNueva = Nueva }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("contrasenaComprobada").GetBoolean().Should().BeTrue();

        // La sesión desde la que se cambia sigue viva; la otra cae en la siguiente petición.
        (await actual.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await otra.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // La contraseña antigua ya no entra, la nueva sí.
        using var cliente = api.CrearCliente();
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Nueva, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        (await contexto.Auditoria.AnyAsync(
            a => a.Accion == "usuario.password_cambiada" && a.UsuarioId == usuario.Id, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CambiarLaContrasenaExigeLaActualCorrecta()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        var antes = await HashAsync(usuario.Id, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/auth/password", new { contrasenaActual = "no-es-la-actual-1", contrasenaNueva = Nueva }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await HashAsync(usuario.Id, ct)).Should().Be(antes);
    }

    [Fact]
    public async Task CambiarLaContrasenaAplicaLasMismasReglasDeLongitudYFiltracion()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        var antes = await HashAsync(usuario.Id, ct);
        const string filtrada = "Otra-filtrada-2026!";
        api.Hibp.Filtradas.Add(filtrada);

        var corta = await sesion.PostAsync(
            "/api/v1/auth/password", new { contrasenaActual = Contrasena, contrasenaNueva = "Corta-123" }, ct);
        var pwned = await sesion.PostAsync(
            "/api/v1/auth/password", new { contrasenaActual = Contrasena, contrasenaNueva = filtrada }, ct);

        corta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        pwned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pwned.Content.ReadAsStringAsync(ct)).Should().Contain("filtraciones");
        (await HashAsync(usuario.Id, ct)).Should().Be(antes);
    }

    [Fact]
    public async Task CambiarLaContrasenaConHibpCaidoNoBloqueaPeroAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        api.Hibp.Caido = true;
        try
        {
            var respuesta = await sesion.PostAsync(
                "/api/v1/auth/password", new { contrasenaActual = Contrasena, contrasenaNueva = Nueva }, ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct))
                .GetProperty("contrasenaComprobada").GetBoolean().Should().BeFalse();
        }
        finally
        {
            api.Hibp.Caido = false;
        }
    }

    // ---- SEC-015: DebeCambiarPassword se impone ----

    [Fact]
    public async Task ConCambioDeContrasenaPendienteSoloVaLaIdentidadElCierreYElCambio()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == usuario.Id, ct);
            fila.DebeCambiarPassword = true;
            await contexto.SaveChangesAsync(ct);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        var clientes = await sesion.GetAsync("/api/v1/clientes", ct);
        clientes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clientes.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.CambioPasswordRequerido);
        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var cambio = await sesion.PostAsync(
            "/api/v1/auth/password", new { contrasenaActual = Contrasena, contrasenaNueva = Nueva }, ct);
        cambio.StatusCode.Should().Be(HttpStatusCode.OK);

        (await sesion.GetAsync("/api/v1/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- SEC-019 ----

    [Fact]
    public async Task DesactivarUnUsuarioCortaSuSesionAunConLaCacheCaliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        using var admin = await AdminAsync(ct);

        // Petición autenticada: calienta la caché de 30 s.
        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var desactivar = await admin.PutAsync($"/api/v1/usuarios/{usuario.Id}/activo", new { activo = false }, ct);
        desactivar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        (await contexto.Sesion.Where(s => s.UsuarioId == usuario.Id).ToListAsync(ct))
            .Should().OnlyContain(s => s.RevocadaAt != null);
        (await contexto.Auditoria.AnyAsync(
            a => a.Accion == "usuario.desactivado" && a.EntidadId == usuario.Id.ToString("D"), ct)).Should().BeTrue();
    }

    [Fact]
    public async Task UnUsuarioDesactivadoNoPuedeEntrarYReactivarloLoDevuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var admin = await AdminAsync(ct);
        using var cliente = api.CrearCliente();

        await admin.PutAsync($"/api/v1/usuarios/{usuario.Id}/activo", new { activo = false }, ct);
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await admin.PutAsync($"/api/v1/usuarios/{usuario.Id}/activo", new { activo = true }, ct);
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElAdministradorNoPuedeDesactivarseASiMismo()
    {
        var ct = TestContext.Current.CancellationToken;
        var yo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var admin = await AyudaPermisos.SesionAsync(api, yo, Contrasena, ct);

        var respuesta = await admin.PutAsync($"/api/v1/usuarios/{yo.Id}/activo", new { activo = false }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RestablecerLaContrasenaCortaLaSesionExigeCambioYValidaLaNueva()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        using var admin = await AdminAsync(ct);
        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var corta = await admin.PutAsync($"/api/v1/usuarios/{usuario.Id}/password", new { contrasena = "Corta-1" }, ct);
        corta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var reset = await admin.PutAsync($"/api/v1/usuarios/{usuario.Id}/password", new { contrasena = Nueva }, ct);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sesión antigua: 401 inmediato aunque la caché estuviera caliente.
        (await sesion.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Entra con la nueva, pero con el cambio pendiente (la conoce el administrador).
        using var nueva = await AyudaPermisos.SesionAsync(api, usuario, Nueva, ct);
        (await nueva.GetAsync("/api/v1/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await nueva.GetAsync("/api/v1/auth/yo", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SoloElAdministradorDesactivaORestablece()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);

        (await sesion.PutAsync($"/api/v1/usuarios/{objetivo.Id}/activo", new { activo = false }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.PutAsync($"/api/v1/usuarios/{objetivo.Id}/password", new { contrasena = Nueva }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
