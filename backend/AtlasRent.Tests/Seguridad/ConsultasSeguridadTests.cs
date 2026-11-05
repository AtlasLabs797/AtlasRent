using System.Net;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>Fábrica con la lista blanca puesta y el límite de login holgado (varias sesiones por clase).</summary>
public sealed class ApiTestFactoryConsultasSeguridad(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    public const string IpOficina = "198.51.100.250";

    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes)
    {
        ajustes[$"{OpcionesLimites.Seccion}:LoginPorMinuto"] = "500";
        ajustes["Seguridad:IpListaBlanca"] = IpOficina;
    }
}

/// <summary>
/// Consultas de solo lectura de seguridad: IP y cuentas bloqueadas, lista blanca, solicitudes de
/// acceso de emergencia y techo del rol en los permisos de un usuario. Solo el administrador, y
/// ninguna respuesta lleva hashes, códigos ni datos de credenciales.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ConsultasSeguridadTests(ContenedorBdFixture bd, ApiTestFactoryConsultasSeguridad api)
    : IClassFixture<ApiTestFactoryConsultasSeguridad>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string RutaIps = "/api/v1/seguridad/bloqueos/ips";
    private const string RutaCuentas = "/api/v1/seguridad/bloqueos/cuentas";
    private const string RutaListaBlanca = "/api/v1/seguridad/bloqueos/lista-blanca";
    private const string RutaSolicitudes = "/api/v1/auth/emergencia/solicitudes";

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct) =>
        await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct), Contrasena, ct);

    [Theory]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Empleado)]
    public async Task GerenteYEmpleadoNoLeenNingunaConsultaDeSeguridad(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        foreach (var ruta in new[] { RutaIps, RutaCuentas, RutaListaBlanca, RutaSolicitudes, $"/api/v1/usuarios/{admin.Id:D}/permisos" })
        {
            (await sesion.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);
        }
    }

    [Fact]
    public async Task ElAdminVeLasIpYCuentasBloqueadasSinDatosDeCredenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var ahora = DateTimeOffset.UtcNow;
        var ipVigente = "203.0.113.77";
        var ipIndefinida = "203.0.113.78";
        var ipCaducada = "203.0.113.79";
        var bloqueada = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var libre = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            contexto.BloqueoIp.AddRange(
                new BloqueoIp { Id = Guid.NewGuid(), Ip = ipVigente, Nivel = 1, Fallos = 10, BloqueadaHasta = ahora.AddHours(1), CreatedAt = ahora, UpdatedAt = ahora },
                new BloqueoIp { Id = Guid.NewGuid(), Ip = ipIndefinida, Nivel = 3, Fallos = 10, BloqueadaHasta = null, CreatedAt = ahora, UpdatedAt = ahora },
                new BloqueoIp { Id = Guid.NewGuid(), Ip = ipCaducada, Nivel = 1, Fallos = 10, BloqueadaHasta = ahora.AddHours(-1), CreatedAt = ahora, UpdatedAt = ahora });
            var fila = await contexto.Usuario.SingleAsync(u => u.Id == bloqueada.Id, ct);
            fila.BloqueadaHasta = ahora.AddMinutes(30);
            await contexto.SaveChangesAsync(ct);
        }

        var respuestaIps = await admin.GetAsync(RutaIps, ct);
        respuestaIps.StatusCode.Should().Be(HttpStatusCode.OK);
        var textoIps = await respuestaIps.Content.ReadAsStringAsync(ct);
        var filasIps = JsonDocument.Parse(textoIps).RootElement.GetProperty("data").EnumerateArray().ToList();
        var ips = filasIps.Select(e => e.GetProperty("ip").GetString()).ToList();
        ips.Should().Contain([ipVigente, ipIndefinida]);
        ips.Should().NotContain(ipCaducada, "un bloqueo vencido ya no bloquea");

        var indefinida = filasIps.Single(e => e.GetProperty("ip").GetString() == ipIndefinida);
        indefinida.GetProperty("hasta").ValueKind.Should().Be(JsonValueKind.Null);
        indefinida.GetProperty("indefinido").GetBoolean().Should().BeTrue();

        var respuestaCuentas = await admin.GetAsync(RutaCuentas, ct);
        respuestaCuentas.StatusCode.Should().Be(HttpStatusCode.OK);
        var textoCuentas = await respuestaCuentas.Content.ReadAsStringAsync(ct);
        var cuentas = JsonDocument.Parse(textoCuentas).RootElement.GetProperty("data")
            .EnumerateArray().Select(e => e.GetProperty("usuarioId").GetGuid()).ToList();
        cuentas.Should().Contain(bloqueada.Id).And.NotContain(libre.Id);
        SinCamposSensibles(textoCuentas);
        SinCamposSensibles(textoIps);
    }

    [Fact]
    public async Task LaListaBlancaSaleDeLaConfiguracion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);

        var respuesta = await admin.GetAsync(RutaListaBlanca, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement;
        cuerpo.GetProperty("data").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(ApiTestFactoryConsultasSeguridad.IpOficina);
    }

    [Fact]
    public async Task SinIpDesbloquearNoSeVenIpNiListaBlancaPeroSiLasCuentas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var recortado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        (await admin.PutAsync(
            $"/api/v1/usuarios/{recortado.Id:D}/permisos",
            new { accion = Acciones.IpDesbloquear, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var sesion = await AyudaPermisos.SesionAsync(api, recortado, Contrasena, ct);

        (await sesion.GetAsync(RutaIps, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync(RutaListaBlanca, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync(RutaCuentas, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LasSolicitudesDeEmergenciaSeFiltranPorEstadoYNuncaTraenElCodigo()
    {
        var ct = TestContext.Current.CancellationToken;
        var solicitante = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var segundo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var objetivoPendiente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var objetivoAprobado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesionA = await AyudaPermisos.SesionAsync(api, solicitante, Contrasena, ct);
        using var sesionB = await AyudaPermisos.SesionAsync(api, segundo, Contrasena, ct);

        var pendiente = await Solicitar(sesionA, objetivoPendiente.Id, ct);
        var aprobada = await Solicitar(sesionA, objetivoAprobado.Id, ct);
        var aprobacion = await sesionB.PostAsync("/api/v1/auth/emergencia/aprobar", new { solicitudId = aprobada }, ct);
        aprobacion.StatusCode.Should().Be(HttpStatusCode.OK);
        var codigo = JsonDocument.Parse(await aprobacion.Content.ReadAsStringAsync(ct))
            .RootElement.GetProperty("codigoAcceso").GetString()!;

        string hash;
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            hash = (await contexto.AccesoEmergencia.SingleAsync(a => a.Id == aprobada, ct)).CodigoHash!;
        }

        // Por defecto, solo pendientes.
        var porDefecto = await sesionA.GetAsync(RutaSolicitudes, ct);
        porDefecto.StatusCode.Should().Be(HttpStatusCode.OK);
        var textoDefecto = await porDefecto.Content.ReadAsStringAsync(ct);
        Ids(textoDefecto).Should().Contain(pendiente).And.NotContain(aprobada);
        var fila = JsonDocument.Parse(textoDefecto).RootElement.GetProperty("data")
            .EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == pendiente);
        fila.GetProperty("estado").GetString().Should().Be("pendiente");
        fila.GetProperty("solicitanteId").GetGuid().Should().Be(solicitante.Id);
        fila.GetProperty("motivo").GetString().Should().NotBeNullOrWhiteSpace();
        fila.GetProperty("aprobadoPorId").ValueKind.Should().Be(JsonValueKind.Null);

        var soloAprobadas = await sesionA.GetAsync($"{RutaSolicitudes}?estado=aprobada", ct);
        var textoAprobadas = await soloAprobadas.Content.ReadAsStringAsync(ct);
        Ids(textoAprobadas).Should().Contain(aprobada).And.NotContain(pendiente);
        var filaAprobada = JsonDocument.Parse(textoAprobadas).RootElement.GetProperty("data")
            .EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == aprobada);
        filaAprobada.GetProperty("aprobadoPorId").GetGuid().Should().Be(segundo.Id);
        filaAprobada.GetProperty("caducaAt").ValueKind.Should().Be(JsonValueKind.String);

        // Ni el código en claro ni su hash salen nunca, en ningún estado.
        var todas = await (await sesionA.GetAsync($"{RutaSolicitudes}?estado=todas", ct)).Content.ReadAsStringAsync(ct);
        foreach (var texto in new[] { textoDefecto, textoAprobadas, todas })
        {
            texto.Should().NotContain(codigo).And.NotContain(hash);
            SinCamposSensibles(texto);
        }

        (await sesionA.GetAsync($"{RutaSolicitudes}?estado=inventado", ct)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LosPermisosDeUnUsuarioTraenElTechoDeSuRol()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var recortado = Acciones.ClienteVer;

        (await admin.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = recortado, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var respuesta = await admin.GetAsync($"/api/v1/usuarios/{empleado.Id:D}/permisos", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement;
        var techo = cuerpo.GetProperty("techo").EnumerateArray().Select(e => e.GetString()).ToList();
        var permisos = cuerpo.GetProperty("permisos").EnumerateArray().Select(e => e.GetString()).ToList();
        techo.Should().BeEquivalentTo(MatrizPermisos.PorRol[Roles.Empleado]);
        techo.Should().Contain(recortado, "el techo es el del rol, no recortado");
        permisos.Should().NotContain(recortado);
        techo.Should().NotContain(Acciones.ConfiguracionEditar);
    }

    private static async Task<Guid> Solicitar(SesionCliente sesion, Guid objetivo, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync(
            "/api/v1/auth/emergencia/solicitar",
            new { usuarioObjetivoId = objetivo, motivo = "Perdió el teléfono y los códigos de recuperación" },
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetGuid();
    }

    private static List<Guid> Ids(string json) =>
        [.. JsonDocument.Parse(json).RootElement.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];

    private static void SinCamposSensibles(string json)
    {
        foreach (var prohibido in new[] { "hash", "codigo", "password", "contrasena", "secreto", "token", "mfa" })
        {
            json.Should().NotContainEquivalentOf(prohibido);
        }
    }
}
