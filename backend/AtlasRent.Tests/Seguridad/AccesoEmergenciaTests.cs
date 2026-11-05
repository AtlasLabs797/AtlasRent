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
/// F1-06: acceso de emergencia (break-glass). Lo aprueban dos administradores distintos, queda
/// registrado en la auditoría firmada y la sesión que sale de él queda marcada como de emergencia.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AccesoEmergenciaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Motivo = "El administrador perdió el teléfono y sus códigos de recuperación";

    /// <summary>Un solo administrador no vale: el que pide el acceso no lo aprueba.</summary>
    [Fact]
    public async Task UnSoloAdministradorNoPuedeAprobarElAccesoDeEmergencia()
    {
        var ct = TestContext.Current.CancellationToken;

        var solicitante = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var bloqueado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await SesionDeAdminAsync(solicitante, ct);

        var solicitud = await sesion.PostAsync(
            "/api/v1/auth/emergencia/solicitar",
            new { usuarioObjetivoId = bloqueado.Id, motivo = Motivo },
            ct);

        solicitud.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await solicitud.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = cuerpo.GetProperty("id").GetGuid();

        // Intenta aprobarlo él mismo: denegado, y sin código de acceso.
        var propia = await sesion.PostAsync(
            "/api/v1/auth/emergencia/aprobar", new { solicitudId = id }, ct);

        propia.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await propia.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.EmergenciaDenegada);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.AccesoEmergencia.SingleAsync(a => a.Id == id, ct);
        fila.AprobadoPorId.Should().BeNull();
        fila.CodigoHash.Should().BeNull();

        // La denegación también queda en la auditoría.
        (await AccionesDeAuditoriaAsync(bd, "seg.acceso_emergencia_denegado", ct))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConDosAdministradoresElAccesoDeEmergenciaSeApruebaYQuedaRegistrado()
    {
        var ct = TestContext.Current.CancellationToken;

        var solicitante = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var segundo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var bloqueado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesionA = await SesionDeAdminAsync(solicitante, ct);
        using var sesionB = await SesionDeAdminAsync(segundo, ct);
        using var clienteBloqueado = api.CrearCliente();

        var solicitud = await sesionA.PostAsync(
            "/api/v1/auth/emergencia/solicitar",
            new { usuarioObjetivoId = bloqueado.Id, motivo = Motivo },
            ct);

        solicitud.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpoSolicitud = await solicitud.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = cuerpoSolicitud.GetProperty("id").GetGuid();

        // Hasta que aprueba el segundo, no hay código: la primera respuesta no lo lleva.
        cuerpoSolicitud.GetProperty("codigoAcceso").ValueKind.Should().Be(JsonValueKind.Null);

        var aprobacion = await sesionB.PostAsync(
            "/api/v1/auth/emergencia/aprobar", new { solicitudId = id }, ct);

        aprobacion.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await aprobacion.Content.ReadFromJsonAsync<JsonElement>(ct);
        var codigo = cuerpo.GetProperty("codigoAcceso").GetString();
        codigo.Should().NotBeNullOrWhiteSpace();

        // El administrador bloqueado entra con su contraseña y el código aprobado: sin TOTP, porque
        // no tiene dispositivo.
        await AyudaMfa.LoginConDesafioAsync(clienteBloqueado, bloqueado, Contrasena, ct);
        var entrada = await clienteBloqueado.PostAsJsonAsync(
            "/api/v1/auth/emergencia/entrar", new { codigo }, ct);

        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        var identidad = await clienteBloqueado.GetFromJsonAsync<JsonElement>("/api/v1/auth/yo", ct);
        identidad.GetProperty("accesoEmergencia").GetBoolean().Should().BeTrue();

        // El rastro explícito: la sesión y la solicitud quedan marcadas, y el código, gastado.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.AccesoEmergencia.SingleAsync(a => a.Id == id, ct);
            fila.AprobadoPorId.Should().Be(segundo.Id);
            fila.UsadoAt.Should().NotBeNull();
            fila.CodigoHash.Should().Be(EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo)));

            var filaSesion = await contexto.Sesion
                .Where(s => s.UsuarioId == bloqueado.Id)
                .SingleAsync(ct);
            filaSesion.ModoEmergencia.Should().BeTrue();
        }

        // Y todo el proceso está en la auditoría firmada.
        (await AccionesDeAuditoriaAsync(bd, "seg.acceso_emergencia_solicitado", ct))
            .Should().BeGreaterThan(0);
        (await AccionesDeAuditoriaAsync(bd, "seg.acceso_emergencia_aprobado", ct))
            .Should().BeGreaterThan(0);
        (await AccionesDeAuditoriaAsync(bd, "auth.acceso_emergencia", ct))
            .Should().BeGreaterThan(0);

        // El código es de un solo uso: con el mismo, otra vez, no se entra.
        await AyudaMfa.LoginConDesafioAsync(clienteBloqueado, bloqueado, Contrasena, ct);
        var reutilizado = await clienteBloqueado.PostAsJsonAsync(
            "/api/v1/auth/emergencia/entrar", new { codigo }, ct);

        reutilizado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Un usuario que no es administrador no puede ni pedir ni aprobar el acceso. Lo corta el filtro
    /// global de permisos (F1-07), antes de que el servicio llegue a mirar quién lo pidió.
    /// </summary>
    [Fact]
    public async Task UnUsuarioQueNoEsAdministradorNoPuedePedirElAccesoDeEmergencia()
    {
        var ct = TestContext.Current.CancellationToken;

        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var bloqueado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var cliente = api.CrearCliente();
        var login = await AyudaAuth.LoginAsync(cliente, empleado.NombreAcceso, Contrasena, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesion = SesionDe(login, cliente);

        var respuesta = await sesion.PostAsync(
            "/api/v1/auth/emergencia/solicitar",
            new { usuarioObjetivoId = bloqueado.Id, motivo = Motivo },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);
    }

    /// <summary>
    /// Sesión completa de un administrador: login (sale desafío por ser administrador), alta del
    /// MFA y entrada con el primer código TOTP. Los endpoints con sesión exigen el doble envío de
    /// CSRF, así que se guardan las cookies para mandarlas como las mandaría el navegador.
    /// </summary>
    private async Task<Sesion> SesionDeAdminAsync(Usuario usuario, CancellationToken ct)
    {
        var cliente = api.CrearCliente();
        var mfa = await AyudaMfa.ActivarCompletoAsync(cliente, usuario, Contrasena, api.Reloj, ct);
        var entrada = await AyudaMfa.EntrarConTotpAsync(cliente, mfa.Secreto, api.Reloj, ct);
        entrada.EnsureSuccessStatusCode();

        return SesionDe(entrada, cliente);
    }

    private static Sesion SesionDe(HttpResponseMessage respuesta, HttpClient cliente) => new(
        cliente,
        [.. AyudaAuth.CookiesDe(respuesta)
            .Where(cookie => cookie.Nombre is NombresCookies.Acceso or NombresCookies.Csrf)
            .Select(cookie => (cookie.Nombre, cookie.Valor))]);

    /// <summary>
    /// Cuenta las filas de auditoría cuya acción empieza por ese texto (las denegaciones llevan
    /// el motivo detrás). Se lee con el superusuario porque a <c>rent_app</c> no le deja leer esa
    /// tabla (RLS, docs/DOC_TECNICA.md §4.3).
    /// </summary>
    private static async Task<int> AccionesDeAuditoriaAsync(
        ContenedorBdFixture bd,
        string accion,
        CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT count(*) FROM auditoria WHERE accion LIKE @accion", conexion);
        comando.Parameters.AddWithValue("accion", $"{accion}%");

        return (int)(long)(await comando.ExecuteScalarAsync(ct))!;
    }

    private sealed record Sesion(HttpClient Cliente, (string Nombre, string Valor)[] Cookies) : IDisposable
    {
        public Task<HttpResponseMessage> PostAsync(string ruta, object cuerpo, CancellationToken ct) =>
            Cliente.SendAsync(AyudaAuth.ConCookies(HttpMethod.Post, ruta, Cookies, cuerpo), ct);

        public void Dispose() => Cliente.Dispose();
    }
}