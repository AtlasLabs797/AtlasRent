using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Bandeja;

/// <summary>
/// F2-10: lectura y cierre de alertas, con acceso por tipo (cada tipo exige una acción) y lectura de
/// la auditoría. La base es compartida con otras pruebas: cada prueba busca sus propias filas por id.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class BandejaAlertasApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    private static readonly string[] Tipos =
    [
        "backup", "auditoria", "comprobante", "contrato", "factura", "fianza", "recordatorio",
        "reserva", "seguridad", "tipo_cambio", "envio",
    ];

    [Fact]
    public void EnmascararRecorreArraysYObjetosAnidadosSinRomper()
    {
        // En la suite completa salió un 500: reasignar un nodo a su propio padre lanza
        // "The node already has a parent" en cuanto `datos` trae un array.
        var nodo = System.Text.Json.Nodes.JsonNode.Parse(
            """{"lineas":[{"importe":"1.00","token":"x"},[1,2]],"cliente":{"cedula":"001","nombre":"A"}}""");

        var resultado = ServicioLecturaAuditoria.Enmascarar(nodo)!.ToJsonString();

        resultado.Should().Be(
            """{"lineas":[{"importe":"1.00","token":"***"},[1,2]],"cliente":{"cedula":"***","nombre":"A"}}""");
    }

    private async Task<Dictionary<string, Guid>> SembrarAsync(CancellationToken ct, DateTimeOffset? resuelta = null)
    {
        var ids = new Dictionary<string, Guid>();
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        foreach (var tipo in Tipos)
        {
            var alerta = new Alerta
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Clave = $"test:{Guid.NewGuid():N}",
                Mensaje = $"Alerta de prueba {tipo}",
                OcurridoAt = DateTimeOffset.UtcNow.AddYears(1),
                Repeticiones = 1,
                ResueltaAt = resuelta,
            };
            contexto.Alerta.Add(alerta);
            ids[tipo] = alerta.Id;
        }

        await contexto.SaveChangesAsync(ct);
        return ids;
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct, string? recortar = null)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        if (recortar is not null)
        {
            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            contexto.UsuarioPermiso.Add(new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                Accion = recortar,
                Permitido = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await contexto.SaveChangesAsync(ct);
        }

        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    private static async Task<List<JsonElement>> FilasAsync(HttpResponseMessage r, CancellationToken ct)
    {
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        return [.. cuerpo.GetProperty("data").EnumerateArray()];
    }

    private static HashSet<Guid> Ids(IEnumerable<JsonElement> filas) =>
        [.. filas.Select(f => f.GetProperty("id").GetGuid())];

    private static HashSet<string> TiposVistos(IEnumerable<JsonElement> filas, Dictionary<string, Guid> ids)
    {
        var vistos = Ids(filas);
        return [.. ids.Where(p => vistos.Contains(p.Value)).Select(p => p.Key)];
    }

    [Theory]
    [InlineData(Roles.Admin, "backup,auditoria,comprobante,contrato,factura,fianza,recordatorio,reserva,seguridad,tipo_cambio,envio")]
    [InlineData(Roles.Gerente, "comprobante,contrato,factura,fianza,recordatorio,reserva,tipo_cambio,envio")]
    [InlineData(Roles.Empleado, "contrato,reserva")]
    public async Task CadaRolVeSoloLosTiposQueLeCorresponden(string rol, string esperados)
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SembrarAsync(ct);
        using var sesion = await SesionAsync(rol, ct);

        var filas = await FilasAsync(await sesion.GetAsync("/api/v1/alertas?pageSize=200", ct), ct);

        TiposVistos(filas, ids).Should().BeEquivalentTo(esperados.Split(','));
    }

    [Fact]
    public async Task UnPermisoRecortadoQuitaLasAlertasDeEseTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SembrarAsync(ct);
        using var sesion = await SesionAsync(Roles.Gerente, ct, recortar: Acciones.FianzaMover);

        var filas = await FilasAsync(await sesion.GetAsync("/api/v1/alertas?pageSize=200", ct), ct);

        TiposVistos(filas, ids).Should().NotContain("fianza").And.Contain("factura");
    }

    [Fact]
    public async Task PedirUnTipoNoPermitidoDaListaVaciaYNoUn403()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarAsync(ct);
        using var sesion = await SesionAsync(Roles.Empleado, ct);

        var filas = await FilasAsync(await sesion.GetAsync("/api/v1/alertas?tipo=backup", ct), ct);

        filas.Should().BeEmpty();
    }

    [Fact]
    public async Task ElFiltroDeEstadoSeparaAbiertasYCerradas()
    {
        var ct = TestContext.Current.CancellationToken;
        var abiertas = await SembrarAsync(ct);
        var cerradas = await SembrarAsync(ct, resuelta: DateTimeOffset.UtcNow);
        using var sesion = await SesionAsync(Roles.Admin, ct);

        var porDefecto = Ids(await FilasAsync(await sesion.GetAsync("/api/v1/alertas?pageSize=200", ct), ct));
        var soloCerradas = Ids(await FilasAsync(await sesion.GetAsync("/api/v1/alertas?estado=cerradas&pageSize=200", ct), ct));
        var todas = Ids(await FilasAsync(await sesion.GetAsync("/api/v1/alertas?estado=todas&pageSize=200", ct), ct));

        porDefecto.Should().Contain(abiertas.Values).And.NotContain(cerradas.Values);
        soloCerradas.Should().Contain(cerradas.Values).And.NotContain(abiertas.Values);
        todas.Should().Contain(abiertas.Values).And.Contain(cerradas.Values);

        (await sesion.GetAsync("/api/v1/alertas?estado=raro", ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CerrarPoneLaFechaLaConservaAlRepetirYAudita()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SembrarAsync(ct);
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var id = ids["contrato"];

        var primera = await sesion.PostAsync($"/api/v1/alertas/{id:D}/cerrar", new { }, ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        var fecha1 = (await primera.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resueltaAt").GetDateTimeOffset();

        await Task.Delay(50, ct);
        var segunda = await sesion.PostAsync($"/api/v1/alertas/{id:D}/cerrar", new { }, ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        var fecha2 = (await segunda.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("resueltaAt").GetDateTimeOffset();

        fecha2.Should().Be(fecha1);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        var auditadas = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion == "alerta.cerrada" && a.EntidadId == id.ToString("D"))
            .ToListAsync(ct);
        auditadas.Should().ContainSingle().Which.Ip.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CerrarUnTipoNoPermitidoEsUn403AuditadoYNoCierra()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SembrarAsync(ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        var respuesta = await sesion.PostAsync($"/api/v1/alertas/{ids["backup"]:D}/cerrar", new { }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        (await contexto.Alerta.AsNoTracking().SingleAsync(a => a.Id == ids["backup"], ct)).ResueltaAt.Should().BeNull();
        (await contexto.Auditoria.AsNoTracking().AnyAsync(
            a => a.Accion == $"permiso.denegado.{Acciones.ConfiguracionEditar}" && a.UsuarioId == usuario.Id, ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CerrarUnIdInexistenteEsUn404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Admin, ct);

        (await sesion.PostAsync($"/api/v1/alertas/{Guid.NewGuid():D}/cerrar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- auditoría

    private async Task<(string Accion, Guid Usuario)> SembrarAuditoriaAsync(CancellationToken ct)
    {
        var accion = $"test.lectura.{Guid.NewGuid():N}";
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        await using var tx = await contexto.Database.BeginTransactionAsync(ct);
        for (var i = 0; i < 3; i++)
        {
            await new AuditoriaService(contexto).RegistrarAsync(new RegistroAuditoria
            {
                Accion = accion,
                Ip = "10.0.0.1",
                UsuarioId = usuario.Id,
                Entidad = "prueba",
                EntidadId = $"E{i}",
                DatosJson = """{"nombre":"visible","password_hash":"abc","cedula":"00112345678","anidado":{"token":"t","ok":1}}""",
                Resultado = "permitido",
                Origen = "api",
            }, ct);
        }

        await tx.CommitAsync(ct);
        return (accion, usuario.Id);
    }

    [Theory]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.Gerente, HttpStatusCode.OK)]
    [InlineData(Roles.Empleado, HttpStatusCode.Forbidden)]
    public async Task LaAuditoriaSoloLaLeenAdminYGerente(string rol, HttpStatusCode esperado)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);

        (await sesion.GetAsync("/api/v1/auditoria", ct)).StatusCode.Should().Be(esperado);
    }

    [Fact]
    public async Task LaAuditoriaFiltraPaginaYNoDevuelveCamposSensibles()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accion, usuario) = await SembrarAuditoriaAsync(ct);
        using var sesion = await SesionAsync(Roles.Admin, ct);

        foreach (var q in new[] { $"accion={accion}", $"usuarioId={usuario:D}&accion={accion}", $"entidad=prueba&accion={accion}" })
        {
            var t = (await (await sesion.GetAsync($"/api/v1/auditoria?{q}", ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("total").GetInt32();
            t.Should().Be(3, q);
        }

        var respuesta = await sesion.GetAsync(
            $"/api/v1/auditoria?accion={accion}&usuarioId={usuario:D}&entidad=prueba&pageSize=2", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("total").GetInt32().Should().Be(3);
        cuerpo.GetProperty("totalPages").GetInt32().Should().Be(2);
        var filas = cuerpo.GetProperty("data").EnumerateArray().ToList();
        filas.Should().HaveCount(2);

        var fila = filas[0];
        fila.GetProperty("usuarioNombre").GetString().Should().Be("Usuario de prueba");
        fila.GetProperty("ip").GetString().Should().Be("10.0.0.1");
        fila.TryGetProperty("firma", out _).Should().BeFalse();
        fila.TryGetProperty("firmaAnterior", out _).Should().BeFalse();
        var datos = fila.GetProperty("datos");
        datos.GetProperty("nombre").GetString().Should().Be("visible");
        datos.GetProperty("password_hash").GetString().Should().Be("***");
        datos.GetProperty("cedula").GetString().Should().Be("***");
        datos.GetProperty("anidado").GetProperty("token").GetString().Should().Be("***");
        datos.GetProperty("anidado").GetProperty("ok").GetInt32().Should().Be(1);
        cuerpo.GetRawText().Should().NotContain("00112345678");

        var porEntidadId = await FilasAsync(
            await sesion.GetAsync($"/api/v1/auditoria?accion={accion}&entidadId=E1", ct), ct);
        porEntidadId.Should().ContainSingle();

        var futuro = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd");
        (await FilasAsync(await sesion.GetAsync($"/api/v1/auditoria?accion={accion}&desde={futuro}", ct), ct))
            .Should().BeEmpty();
        var hoy = DateTime.UtcNow.ToString("yyyy-MM-dd");
        (await FilasAsync(await sesion.GetAsync($"/api/v1/auditoria?accion={accion}&desde={hoy}&hasta={futuro}", ct), ct))
            .Should().HaveCount(3);

        (await sesion.GetAsync($"/api/v1/auditoria?desde={futuro}&hasta={hoy}", ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
