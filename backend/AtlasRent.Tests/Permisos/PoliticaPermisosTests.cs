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

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// F1-07: control de acceso por acción. Comprueba el criterio de aceptación (un intento no
/// permitido devuelve 403 y queda auditado), el techo del rol con los permisos por usuario dentro,
/// y el desbloqueo de IP y de cuenta, que solo puede hacer el administrador.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PoliticaPermisosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Criterio de aceptación de F1-07: el intento no permitido se rechaza con 403 y queda en la
    /// auditoría firmada con resultado "denegado".
    /// </summary>
    [Fact]
    public async Task UnIntentoNoPermitidoDevuelve403YQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/seguridad/bloqueos/ip/desbloquear", new { ip = "198.51.100.77" }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);

        var denegaciones = await DenegacionesAsync(bd, ct);
        denegaciones.Should().NotBeEmpty();
        denegaciones.Should().Contain(fila => fila.Accion == $"permiso.denegado.{Acciones.IpDesbloquear}"
            && fila.Resultado == "denegado"
            && fila.UsuarioId == empleado.Id
            && fila.Ip == api.IpCliente);
    }

    [Fact]
    public async Task ElGerenteYElEmpleadoNoDesbloqueanUnaIp()
    {
        var ct = TestContext.Current.CancellationToken;
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        using var sesionGerente = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);
        using var sesionEmpleado = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        var ruta = "/api/v1/seguridad/bloqueos/ip/desbloquear";

        (await sesionGerente.PostAsync(ruta, new { ip = "198.51.100.78" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesionEmpleado.PostAsync(ruta, new { ip = "198.51.100.78" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SoloElAdminDesbloqueaUnaIp()
    {
        var ct = TestContext.Current.CancellationToken;
        var ip = $"198.51.100.{Guid.NewGuid().ToByteArray()[0] % 200 + 1}";
        await CrearBloqueoIpAsync(ip, ct);

        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/seguridad/bloqueos/ip/desbloquear", new { ip }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.BloqueoIp.AsNoTracking().AnyAsync(b => b.Ip == ip, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task SoloElAdminDesbloqueaUnaCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var bloqueado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        var ruta = "/api/v1/seguridad/bloqueos/cuenta/desbloquear";
        using var sesionGerente = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);

        (await sesionGerente.PostAsync(ruta, new { usuarioId = bloqueado.Id }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        var respuesta = await sesionAdmin.PostAsync(ruta, new { usuarioId = bloqueado.Id }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == bloqueado.Id, ct))
            .BloqueadaHasta.Should().BeNull();
    }

    [Fact]
    public async Task UnPermisoDentroDelTechoSeActiva()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = Acciones.CotizacionEnviar, permitido = true },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PermisosEfectivosAsync(sesion, empleado.Id, ct))
            .Should().Contain(Acciones.CotizacionEnviar);
    }

    /// <summary>D17: un permiso por usuario nunca supera el techo del rol; se rechaza al guardarlo.</summary>
    [Fact]
    public async Task UnPermisoPorEncimaDelTechoNoSePuedeGuardar()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = Acciones.FacturaEmitir, permitido = true },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoFueraDeTecho);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.UsuarioPermiso.AsNoTracking()
            .AnyAsync(p => p.UsuarioId == empleado.Id && p.Accion == Acciones.FacturaEmitir, ct))
            .Should().BeFalse();
    }

    /// <summary>
    /// Un permiso por debajo del techo sí se puede quitar: "recortables desde el rol" (D17).
    /// </summary>
    [Fact]
    public async Task UnPermisoDelTechoSePuedeQuitar()
    {
        var ct = TestContext.Current.CancellationToken;
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/usuarios/{gerente.Id:D}/permisos",
            new { accion = Acciones.FacturaEmitir, permitido = false },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PermisosEfectivosAsync(sesion, gerente.Id, ct))
            .Should().NotContain(Acciones.FacturaEmitir);
    }

    /// <summary>
    /// "Enviar cotizaciones" está en el techo del empleado y viene desactivado al crear el usuario;
    /// el administrador lo activa y a partir de ahí el empleado lo tiene.
    /// </summary>
    [Fact]
    public async Task EnviarCotizacionesVieneDesactivadoAlCrearElUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var nombreAcceso = $"nuevo{Guid.NewGuid():N}"[..20];
        var creado = await sesion.PostAsync("/api/v1/usuarios", new
        {
            nombreAcceso,
            nombre = "Empleado nuevo",
            contrasena = Contrasena,
            rol = Roles.Empleado,
        }, ct);

        creado.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        (await PermisosEfectivosAsync(sesion, id, ct)).Should().NotContain(Acciones.CotizacionEnviar);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var porUsuario = await contexto.UsuarioPermiso.AsNoTracking()
            .SingleAsync(p => p.UsuarioId == id && p.Accion == Acciones.CotizacionEnviar, ct);
        porUsuario.Permitido.Should().BeFalse();

        (await sesion.PutAsync(
            $"/api/v1/usuarios/{id:D}/permisos",
            new { accion = Acciones.CotizacionEnviar, permitido = true },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PermisosEfectivosAsync(sesion, id, ct)).Should().Contain(Acciones.CotizacionEnviar);
    }

    /// <summary>El empleado nunca ve totales agregados: esa acción no está en su techo.</summary>
    [Fact]
    public async Task ElEmpleadoNoTieneTotalesNiInformes()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var permisos = await PermisosEfectivosAsync(sesion, empleado.Id, ct);

        permisos.Should().NotContain(Acciones.InformeVer);
        permisos.Should().NotContain(Acciones.AuditoriaVer);

        // Ni siquiera se puede activar por encima del techo.
        (await sesion.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = Acciones.InformeVer, permitido = true },
            ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Empleado)]
    public async Task LaSesionDevuelveElRolRealDelUsuario(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        var yo = await sesion.GetAsync("/api/v1/auth/yo", ct);

        yo.StatusCode.Should().Be(HttpStatusCode.OK);
        (await yo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rol").GetString()
            .Should().Be(rol);
    }

    [Fact]
    public async Task UnGerenteNoAdministraUsuarios()
    {
        var ct = TestContext.Current.CancellationToken;
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);

        var respuesta = await sesion.GetAsync("/api/v1/usuarios", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DenegacionesAsync(bd, ct))
            .Should().Contain(fila => fila.Accion == $"permiso.denegado.{Acciones.ConfiguracionEditar}");
    }

    [Fact]
    public async Task ElAdminListaLosUsuariosConSuRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.GetAsync("/api/v1/usuarios", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        var fila = cuerpo.GetProperty("data").EnumerateArray()
            .Single(u => u.GetProperty("id").GetGuid() == empleado.Id);
        fila.GetProperty("rol").GetString().Should().Be(Roles.Empleado);
    }

    private static async Task<string[]> PermisosEfectivosAsync(
        SesionCliente sesion,
        Guid usuarioId,
        CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync($"/api/v1/usuarios/{usuarioId:D}/permisos", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        return [.. (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("permisos").EnumerateArray().Select(p => p.GetString()!)];
    }

    private async Task CrearBloqueoIpAsync(string ip, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.BloqueoIp.Add(new BloqueoIp
        {
            Id = Guid.NewGuid(),
            Ip = ip,
            Nivel = 1,
            Fallos = 30,
            BloqueadaHasta = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Denegaciones de auditoría. Se lee con el superusuario porque a <c>rent_app</c> RLS no le deja
    /// leer esa tabla más que a administrador y gerente.
    /// </summary>
    private static async Task<List<Denegacion>> DenegacionesAsync(ContenedorBdFixture bd, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT accion, resultado, usuario_id, ip FROM auditoria WHERE resultado = 'denegado'", conexion);
        await using var lector = await comando.ExecuteReaderAsync(ct);

        var filas = new List<Denegacion>();
        while (await lector.ReadAsync(ct))
        {
            filas.Add(new Denegacion(
                lector.GetString(0),
                lector.GetString(1),
                lector.IsDBNull(2) ? null : lector.GetGuid(2),
                lector.GetString(3)));
        }

        return filas;
    }

    private sealed record Denegacion(string Accion, string Resultado, Guid? UsuarioId, string Ip);
}