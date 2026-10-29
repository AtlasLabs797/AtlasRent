using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Controllers;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Agentes;

/// <summary>
/// F6-01: el token de agente externo. Corre con los permisos de su usuario propietario y nunca por
/// encima: una acción vale si está en la lista del token, si el rol la trae y si el usuario no se la
/// ha recortado. Cada llamada queda auditada con origen <c>agente</c>.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TokenAgenteTests(ContenedorBdFixture bd, ApiTestFactory api) : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task UnTokenValidoDeUnUsuarioConPermisoDevuelve200()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.IaConsultar, Acciones.ClienteVer], ct);

        using var cliente = ClienteConToken(token);

        var respuesta = await cliente.GetAsync("/api/v1/agente/clientes", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Criterio 2 (doble techo): la acción está en la lista del token, pero el rol del propietario no
    /// la tiene. La lista del token nunca amplía el techo del rol.
    /// </summary>
    [Fact]
    public async Task UnTokenNoAlcanzaAccionesQueSuRolNoTiene()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        // `agente.token` está en la lista del token, pero es solo de admin.
        var token = await TokenEnBaseAsync(empleado.Id, [Acciones.AgenteToken], ct);
        using var cliente = ClienteConToken(token);

        var respuesta = await cliente.PostAsync(
            "/api/v1/agente/tokens",
            JsonContent.Create(new { nombre = "Intruso", usuarioId = empleado.Id, acciones = new[] { Acciones.ClienteVer } }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);
        (await LlamadasDeAgenteAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.AgenteToken}"
            && fila.Resultado == "denegado"
            && fila.UsuarioId == empleado.Id
            && fila.Ip == api.IpCliente);
    }

    /// <summary>
    /// Criterio 6: el empleado nunca recibe totales agregados. Aunque su token los pida, ninguna ruta
    /// de agente los expone y su rol no le da acceso a ellos.
    /// </summary>
    [Fact]
    public async Task UnTokenDeEmpleadoNoRecibeTotalesPorNingunEndpointDeAgente()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var token = await TokenEnBaseAsync(
            empleado.Id,
            [Acciones.InformeVer, Acciones.IaConsultar, Acciones.ClienteVer],
            ct);
        using var cliente = ClienteConToken(token);

        (await cliente.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Y no es que hoy no haya esa ruta: es que ninguna ruta de agente declara una acción de totales.
        foreach (var declaracion in DeclaracionesDeAccion())
        {
            declaracion.Should().NotContain(Acciones.InformeVer);
        }
    }

    [Fact]
    public async Task UnTokenCaducadoDevuelve401()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(
            admin.Id,
            [Acciones.IaConsultar, Acciones.ClienteVer],
            ct,
            caducaEn: api.Reloj.GetUtcNow().AddMinutes(-1));

        using var cliente = ClienteConToken(token);

        (await cliente.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnTokenRevocadoDevuelve401()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(
            admin.Id,
            [Acciones.IaConsultar, Acciones.ClienteVer],
            ct,
            revocadoEn: DateTimeOffset.UtcNow);

        using var cliente = ClienteConToken(token);

        (await cliente.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Criterio 3: un token que no existe es un 401, nunca un 500.</summary>
    [Fact]
    public async Task UnTokenQueNoExisteDevuelve401YNoUn500()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = ClienteConToken("no-es-un-token-valido");

        var respuesta = await cliente.GetAsync("/api/v1/agente/clientes", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Criterio 4: la acción no está en la lista del token, aunque el rol sí la tenga.</summary>
    [Fact]
    public async Task UnTokenSinLaAccionDevuelve403Auditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        // La ruta exige `ia.consultar` y `cliente.ver`; el token solo trae una de las dos.
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.ClienteVer], ct);
        using var cliente = ClienteConToken(token);

        var respuesta = await cliente.GetAsync("/api/v1/agente/clientes", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LlamadasDeAgenteAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.IaConsultar}"
            && fila.Resultado == "denegado"
            && fila.UsuarioId == admin.Id);
    }

    /// <summary>Criterio 5: cada llamada con token deja su fila con origen <c>agente</c> y su IP.</summary>
    [Fact]
    public async Task CadaLlamadaConTokenQuedaAuditadaConOrigenAgente()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.IaConsultar, Acciones.ClienteVer], ct);
        using var cliente = ClienteConToken(token);

        (await cliente.GetAsync("/api/v1/agente/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.GetAsync("/api/v1/agente/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // La base es compartida por toda la colección, así que se filtra por el usuario de esta
        // prueba y no por el total de filas.
        var llamadas = (await LlamadasDeAgenteAsync(ct))
            .Where(fila => fila.Accion == LlamadaDeAgenteAccion && fila.UsuarioId == admin.Id)
            .ToList();
        llamadas.Should().HaveCount(2);
        llamadas.Should().OnlyContain(fila => fila.Ip == api.IpCliente && fila.Origen == "agente");
    }

    /// <summary>Criterio 7a: sin cookie de sesión y sin token no hay quién sea.</summary>
    [Fact]
    public async Task SinCookieNiTokenDevuelve401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.CrearClienteSinCookies();

        var respuesta = await cliente.GetAsync("/api/v1/agente/clientes", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Criterio 7b: la ruta de agente con una sesión normal es una petición normal, sin puerta de
    /// atrás: el gerente entra porque su rol trae las acciones, no porque sea agente.
    /// </summary>
    [Fact]
    public async Task ConCookieDeSesionLaRutaDeAgenteSeComportaComoUnaPeticionNormal()
    {
        var ct = TestContext.Current.CancellationToken;
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);

        (await sesion.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Y lo que no le cabe en el rol, ni con sesión ni con token.
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesionEmpleado = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        (await sesionEmpleado.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Criterio 8: del token solo queda el hash SHA-256. El en claro sale una vez, en la respuesta
    /// del alta, y no aparece en ninguna columna de la tabla.
    /// </summary>
    [Fact]
    public async Task ElTokenEnClaroNoSeGuardaEnLaBaseDeDatos()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var emitido = await sesion.PostAsync(
            "/api/v1/agente/tokens",
            new { nombre = "Asistente de pruebas", usuarioId = admin.Id, acciones = new[] { Acciones.ClienteVer } },
            ct);
        emitido.StatusCode.Should().Be(HttpStatusCode.OK);

        var secreto = (await emitido.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString()!;
        secreto.Should().NotBeNullOrWhiteSpace();

        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        string hash;
        await using (var comando = new NpgsqlCommand(
            "SELECT hash_token FROM agente_token WHERE nombre = 'Asistente de pruebas'", conexion))
        await using (var lector = await comando.ExecuteReaderAsync(ct))
        {
            (await lector.ReadAsync(ct)).Should().BeTrue();
            hash = lector.GetString(0);
        }

        hash.Should().NotBe(secreto);
        hash.Should().Be(EmisorTokens.HashToken(secreto));

        // Ningún texto de la tabla contiene el secreto.
        await using var busqueda = new NpgsqlCommand(
            """
            SELECT count(*) FROM agente_token
            WHERE coalesce(nombre, '') || coalesce(hash_token, '') || coalesce(acciones::text, '')
                LIKE '%' || @secreto || '%'
            """,
            conexion);
        busqueda.Parameters.AddWithValue("secreto", secreto);
        (await busqueda.ExecuteScalarAsync(ct)).Should().Be(0L);
    }

    [Fact]
    public async Task SoloElAdminAdministraLosTokensYElListadoNoTraeSecretos()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);

        using var sesionGerente = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);
        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var cuerpo = new
        {
            nombre = "Asistente del gerente",
            usuarioId = gerente.Id,
            acciones = new[] { Acciones.IaConsultar, Acciones.ClienteVer },
            diasValidez = 30,
            limiteConsultasDia = 250,
        };

        (await sesionGerente.PostAsync("/api/v1/agente/tokens", cuerpo, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var emitido = await sesionAdmin.PostAsync("/api/v1/agente/tokens", cuerpo, ct);
        emitido.StatusCode.Should().Be(HttpStatusCode.OK);

        var creado = await emitido.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = creado.GetProperty("id").GetGuid();
        creado.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        creado.GetProperty("caduca").GetDateTimeOffset().Should().BeAfter(api.Reloj.GetUtcNow());
        creado.GetProperty("limiteConsultasDia").GetInt32().Should().Be(250);

        var listado = await sesionAdmin.GetAsync("/api/v1/agente/tokens", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.OK);
        var fila = (await listado.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("data").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == id);
        fila.TryGetProperty("token", out _).Should().BeFalse();
        fila.GetProperty("acciones").EnumerateArray().Select(a => a.GetString())
            .Should().BeEquivalentTo(Acciones.IaConsultar, Acciones.ClienteVer);
    }

    [Fact]
    public async Task RevocarElTokenLoDejaSinPoder()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.IaConsultar, Acciones.ClienteVer], ct);
        var id = await IdDeTokenAsync(token, ct);

        using var cliente = ClienteConToken(token);
        (await cliente.GetAsync("/api/v1/agente/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        (await sesion.PostAsync($"/api/v1/agente/tokens/{id:D}/revocar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetAsync("/api/v1/agente/clientes", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnaAccionDesconocidaEnElAltaEsUn400()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/agente/tokens",
            new { nombre = "Inventado", usuarioId = admin.Id, acciones = new[] { "inventado.accion" } },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>SEC-010: el alta rechaza con 400 cualquier acción que mute datos o acuñe tokens.</summary>
    [Theory]
    [InlineData(Acciones.CobroRegistrar)]
    [InlineData(Acciones.FacturaAnular)]
    [InlineData(Acciones.MensajeriaMasivo)]
    [InlineData(Acciones.ContratoEstado)]
    [InlineData(Acciones.AgenteToken)]
    public async Task ElAltaRechazaLasAccionesFueraDeLaListaBlanca(string accion)
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/agente/tokens",
            new { nombre = "Escritor", usuarioId = admin.Id, acciones = new[] { Acciones.IaConsultar, accion } },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(accion);
    }

    [Fact]
    public async Task ElAltaAceptaTodaLaListaBlanca()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PostAsync(
            "/api/v1/agente/tokens",
            new { nombre = "Lector", usuarioId = admin.Id, acciones = Acciones.PermitidasAAgente.ToArray() },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// SEC-010: un token ya emitido (aquí, escrito en la base) con una acción de escritura no la
    /// puede usar: 403 y fila de denegación con origen agente.
    /// </summary>
    [Fact]
    public async Task UnTokenExistenteConCobroRegistrarNoPuedeCobrar()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.CobroRegistrar, Acciones.IaConsultar], ct);
        using var cliente = ClienteConToken(token);

        var respuesta = await cliente.PostAsync("/api/v1/cobros", JsonContent.Create(new { }), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);
        (await LlamadasDeAgenteAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.CobroRegistrar}"
            && fila.Resultado == "denegado"
            && fila.UsuarioId == admin.Id);
    }

    [Fact]
    public async Task UnTokenExistenteConAgenteTokenNoAcunaOtrosTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.AgenteToken], ct);
        using var cliente = ClienteConToken(token);

        var respuesta = await cliente.PostAsync(
            "/api/v1/agente/tokens",
            JsonContent.Create(new { nombre = "Clon", usuarioId = admin.Id, acciones = new[] { Acciones.ClienteVer } }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LlamadasDeAgenteAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.AgenteToken}" && fila.UsuarioId == admin.Id);
    }

    // ------------------------------------------------------------------ ayuda

    /// <summary>
    /// AUD-10 (H-01): las alertas se declaran con <c>cliente.ver</c>, que un token de agente puede
    /// llevar. El token de un administrador con solo esa acción no puede ver alertas de tipos cuya
    /// acción no lleva (backup) ni cerrar ninguna: un agente no muta.
    /// </summary>
    [Fact]
    public async Task UnTokenConClienteVerNoVeAlertasAjenasNiCierraNinguna()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var token = await TokenEnBaseAsync(admin.Id, [Acciones.ClienteVer], ct);
        var backup = Guid.NewGuid();
        var contrato = Guid.NewGuid();
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            foreach (var (id, tipo) in new[] { (backup, "backup"), (contrato, "contrato") })
            {
                contexto.Alerta.Add(new Alerta
                {
                    Id = id,
                    Tipo = tipo,
                    Clave = $"{tipo}:{id:N}",
                    Mensaje = "Alerta de prueba",
                    OcurridoAt = DateTimeOffset.UtcNow,
                    Repeticiones = 1,
                });
            }

            await contexto.SaveChangesAsync(ct);
        }

        using var cliente = ClienteConToken(token);

        var lista = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/alertas?estado=todas&pageSize=200", ct);
        var ids = lista.GetProperty("data").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
        ids.Should().NotContain(backup, "el token no lleva configuracion.editar");
        ids.Should().Contain(contrato, "el token sí lleva cliente.ver");

        (await cliente.PostAsync($"/api/v1/alertas/{contrato:D}/cerrar", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PostAsync($"/api/v1/alertas/{backup:D}/cerrar", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Acción con la que se audita toda llamada hecha con token de agente.</summary>
    private const string LlamadaDeAgenteAccion = "agente.peticion";

    private HttpClient ClienteConToken(string token)
    {
        var cliente = api.CrearClienteSinCookies();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>
    /// Token escrito directamente en la base de datos, para poder probar caducado, revocado y
    /// acciones que el alta no dejaría pasar. Devuelve el secreto en claro, que es lo que viaja en
    /// la cabecera.
    /// </summary>
    private async Task<string> TokenEnBaseAsync(
        Guid usuarioId,
        string[] acciones,
        CancellationToken ct,
        DateTimeOffset? caducaEn = null,
        DateTimeOffset? revocadoEn = null)
    {
        var secreto = EmisorTokens.TokenAleatorio();
        var ahora = DateTimeOffset.UtcNow;

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.AgenteToken.Add(new AgenteToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Nombre = "Token de prueba",
            HashToken = EmisorTokens.HashToken(secreto),
            Acciones = acciones,
            Creado = ahora,
            Caduca = caducaEn ?? ahora.AddDays(90),
            Revocado = revocadoEn,
            UltimoUso = null,
            LimiteConsultasDia = 500,
        });
        await contexto.SaveChangesAsync(ct);

        return secreto;
    }

    private async Task<Guid> IdDeTokenAsync(string secreto, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.AgenteToken.AsNoTracking()
            .Where(t => t.HashToken == EmisorTokens.HashToken(secreto))
            .Select(t => t.Id)
            .SingleAsync(ct);
    }

    /// <summary>
    /// Todas las acciones declaradas por los endpoints del controlador de agente. Es la lista
    /// cerrada de lo que un agente puede pedir: nada más entra en la API con token.
    /// </summary>
    private static string[][] DeclaracionesDeAccion() =>
    [
        .. typeof(AgenteController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(metodo => metodo.GetCustomAttribute<AccionAttribute>())
            .Where(atributo => atributo is not null)
            .Select(atributo => atributo!.Acciones),
    ];

    /// <summary>
    /// Filas de auditoría escritas por el middleware de agente. Se lee con el superusuario porque a
    /// <c>rent_app</c> la RLS no le deja leer esa tabla más que a administrador y gerente.
    /// </summary>
    private async Task<List<LlamadaAuditada>> LlamadasDeAgenteAsync(CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT accion, resultado, usuario_id, ip, origen FROM auditoria WHERE origen = 'agente'",
            conexion);
        await using var lector = await comando.ExecuteReaderAsync(ct);

        var filas = new List<LlamadaAuditada>();
        while (await lector.ReadAsync(ct))
        {
            filas.Add(new LlamadaAuditada(
                lector.GetString(0),
                lector.GetString(1),
                lector.IsDBNull(2) ? null : lector.GetGuid(2),
                lector.GetString(3),
                lector.GetString(4)));
        }

        return filas;
    }

    private sealed record LlamadaAuditada(string Accion, string Resultado, Guid? UsuarioId, string Ip, string Origen);
}