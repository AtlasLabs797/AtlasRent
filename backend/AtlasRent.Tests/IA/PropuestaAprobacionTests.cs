using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Recordatorios;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.IA;

/// <summary>
/// F6-03: las propuestas del asistente. <b>Proponer no es hacer</b>, y eso es lo que comprueban
/// estas pruebas: entre proponer y aprobar no se manda ningún correo, aprobar sí, y si el saldo
/// cambia mientras espera la propuesta caduca y tampoco se manda.
/// <para>
/// El escenario es real: cliente, contrato, factura emitida que venció hace días y el trabajo de
/// recordatorios en modo manual, que es quien deja el aviso <c>Pendiente</c> esperando a alguien.
/// Lo único que se monta por debajo es el recorte de permisos y el token de agente, como en las
/// pruebas de F6-01.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PropuestaAprobacionTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ProponerNoMandaNadaYApruebaSi()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, _) = await EscenarioAsync(ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        propuesta.Resultado.Should().Be(nameof(ResultadoSolicitud.Pendiente));
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct))
            .Should().BeEmpty("proponer no es mandar: el correo solo sale al aprobar");
        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Pendiente, "el aviso sigue esperando a que alguien lo mande");

        var respuesta = await admin.PostAsync($"/api/v1/ia/propuestas/{propuesta.Id:D}/aprobacion", new { }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("resultado").GetString().Should().Be(nameof(ResultadoSolicitud.Aprobada));

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Enviado);
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct))
            .Should().ContainSingle("el aviso sale por el mismo servicio que el botón de la pantalla");
    }

    [Fact]
    public async Task LaPropuestaCaducaSiElSaldoCambiaEntreProponerYAprobar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, facturaId) = await EscenarioAsync(ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        // Alguien cobra entre que se propone y que se aprueba: lo que iba a aprobar ya no existe.
        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 300m, escenario.Hoy, ct);

        var respuesta = await admin.PostAsync($"/api/v1/ia/propuestas/{propuesta.Id:D}/aprobacion", new { }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("resultado").GetString().Should().Be(nameof(ResultadoSolicitud.Caducada));
        cuerpo.GetProperty("motivoCaducidad").GetString().Should().NotBeNullOrWhiteSpace("caducar dice por qué");

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct))
            .Should().BeEmpty("un cliente al que acaban de cobrarle no recibe un recordatorio");
        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Pendiente, "el aviso se queda como estaba");
    }

    /// <summary>
    /// Criterio de F6-03: aprobar no da más poder del que ya se tiene. El gerente de aquí trae
    /// <c>ia.proponer</c> y <c>contrato.estado</c> —o sea, aprueba contratos todos los días— pero se
    /// le ha recortado la acción del envío (D17), así que esta propuesta no es suya.
    /// </summary>
    [Fact]
    public async Task QuienNoPuedeMandarElAvisoNoApruebaLaPropuesta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, _) = await EscenarioAsync(ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        await RecortarAsync(gerente.Id, Acciones.MensajeriaMasivo, ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);

        (await sesion.GetAsync("/api/v1/ia/propuestas", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "proponer le deja ver la bandeja");

        var respuesta = await sesion.PostAsync($"/api/v1/ia/propuestas/{propuesta.Id:D}/aprobacion", new { }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);

        (await DenegadasAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.MensajeriaMasivo}"
            && fila.Resultado == "denegado"
            && fila.UsuarioId == gerente.Id);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct))
            .Should().BeEmpty("sin permiso de envío no sale nada");
        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Pendiente);
    }

    [Fact]
    public async Task UnTokenDeAgenteSinIaProponerNoPropone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, escenario, _) = await EscenarioAsync(ct);
        var antes = (await PendientesAsync(ct)).Count;

        using var cliente = ClienteConToken(await TokenEnBaseAsync([Acciones.IaConsultar], ct));

        var respuesta = await cliente.PostAsync(
            "/api/v1/agente/propuesta",
            JsonContent.Create(new
            {
                clienteId = escenario.ClienteId,
                tipo = ServicioPropuestasAgente.TipoRecordatorioSaldo,
            }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);

        (await DenegadasAsync(ct)).Should().Contain(fila =>
            fila.Accion == $"permiso.denegado.{Acciones.IaProponer}"
            && fila.Resultado == "denegado"
            && fila.Origen == "agente");

        (await PendientesAsync(ct))
            .Should().HaveCount(antes, "una petición denegada no deja ninguna propuesta viva");
    }

    [Fact]
    public async Task UnTokenDeAgenteConIaProponerProponeYNoEjecuta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, escenario, _) = await EscenarioAsync(ct);

        using var cliente = ClienteConToken(await TokenEnBaseAsync([Acciones.IaProponer], ct));

        var respuesta = await cliente.PostAsync(
            "/api/v1/agente/propuesta",
            JsonContent.Create(new
            {
                clienteId = escenario.ClienteId,
                tipo = ServicioPropuestasAgente.TipoRecordatorioSaldo,
            }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("resultado").GetString().Should().Be(nameof(ResultadoSolicitud.Pendiente));

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct))
            .Should().BeEmpty("ni con la acción de proponer sale un correo");
    }

    [Fact]
    public async Task ElEmpleadoNiProponeNiAprueba()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, _) = await EscenarioAsync(ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        (await sesion.PostAsync(
            "/api/v1/ia/propuesta",
            new { clienteId = escenario.ClienteId, tipo = ServicioPropuestasAgente.TipoRecordatorioSaldo },
            ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await sesion.PostAsync($"/api/v1/ia/propuestas/{propuesta.Id:D}/aprobacion", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task RechazarNoEjecutaYDejaElComentario()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, _) = await EscenarioAsync(ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/ia/propuestas/{propuesta.Id:D}/rechazo",
            new { comentario = "El cliente ya lo pagó por otra vía." },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("resultado").GetString().Should().Be(nameof(ResultadoSolicitud.Rechazada));
        cuerpo.GetProperty("comentario").GetString().Should().Be("El cliente ya lo pagó por otra vía.");

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty();
        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Pendiente);
    }

    /// <summary>
    /// El nombre real del cliente no sale del proceso: en el payload y en la auditoría va el
    /// seudónimo, como en las consultas del asistente. La bandeja sí lo enseña, porque es una
    /// pantalla de personas y sin nombre no se puede decidir.
    /// </summary>
    [Fact]
    public async Task ElPayloadYLaAuditoriaLlevanElSeudonimoYNoElNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, escenario, _) = await EscenarioAsync(ct);
        var nombre = await NombreDelClienteAsync(escenario.ClienteId, ct);

        var propuesta = await ProponerAsync(admin, escenario.ClienteId, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.SolicitudAprobacion.AsNoTracking()
                .SingleAsync(s => s.Id == propuesta.Id, ct);

            fila.PayloadJson.Should().Contain("[[cliente_1]]");
            fila.PayloadJson.Should().NotContain(nombre);
        }

        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var auditorias = await contexto.Auditoria.AsNoTracking()
                .Where(a => a.EntidadId == propuesta.Id.ToString("D"))
                .ToListAsync(ct);

            auditorias.Should().NotBeEmpty();
            auditorias.Should().OnlyContain(a => (a.Datos == null || !a.Datos.RootElement.GetRawText().Contains(nombre)));
        }

        var bandeja = await admin.GetAsync("/api/v1/ia/propuestas", ct);
        bandeja.StatusCode.Should().Be(HttpStatusCode.OK, await bandeja.Cuerpo());
        (await bandeja.Content.ReadAsStringAsync(ct)).Should().Contain(nombre);
    }

    // ---------------------------------------------------------------- ayuda

    /// <summary>
    /// Cliente, contrato y una factura vencida, con el trabajo de recordatorios en modo manual: el
    /// aviso queda <c>Pendiente</c> y sin mandar, que es lo único que se puede proponer.
    /// </summary>
    private async Task<(SesionCliente Admin, AyudaCuentas.Escenario Escenario, Guid FacturaId)> EscenarioAsync(
        CancellationToken ct)
    {
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Should().ContainSingle("el trabajo deja un aviso por cliente y unidad");

        return (admin, escenario, facturaId);
    }

    private async Task<Propuesta> ProponerAsync(SesionCliente sesion, Guid clienteId, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync(
            "/api/v1/ia/propuesta",
            new
            {
                clienteId,
                tipo = ServicioPropuestasAgente.TipoRecordatorioSaldo,
                justificacion = "Tiene saldo vencido.",
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

        return new Propuesta(cuerpo.GetProperty("id").GetGuid(), cuerpo.GetProperty("resultado").GetString()!);
    }

    private async Task<string> NombreDelClienteAsync(Guid clienteId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Cliente.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => c.Nombre)
            .SingleAsync(ct);
    }

    /// <summary>Recorta una acción que el rol sí trae (D17), que es como se quita poder de verdad.</summary>
    private async Task RecortarAsync(Guid usuarioId, string accion, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        contexto.UsuarioPermiso.Add(new UsuarioPermiso
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Accion = accion,
            Permitido = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Token escrito en la base con la lista de acciones que interesa probar, como en las pruebas de
    /// F6-01. Devuelve el secreto en claro, que es lo que viaja en la cabecera.
    /// </summary>
    private async Task<string> TokenEnBaseAsync(string[] acciones, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var secreto = EmisorTokens.TokenAleatorio();
        var ahora = DateTimeOffset.UtcNow;

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.AgenteToken.Add(new AgenteToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            Nombre = "Token de prueba de propuestas",
            HashToken = EmisorTokens.HashToken(secreto),
            Acciones = acciones,
            Creado = ahora,
            Caduca = ahora.AddDays(90),
            Revocado = null,
            UltimoUso = null,
            LimiteConsultasDia = 500,
        });
        await contexto.SaveChangesAsync(ct);

        return secreto;
    }

    private HttpClient ClienteConToken(string token)
    {
        var cliente = api.CrearClienteSinCookies();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>Las propuestas vivas que hay en la base, leídas por debajo de la API.</summary>
    private async Task<List<SolicitudAprobacion>> PendientesAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.Tipo == TipoSolicitud.AccionAgente && s.Resultado == ResultadoSolicitud.Pendiente)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Filas de denegación de la auditoría. Se leen con el superusuario porque a <c>rent_app</c> la RLS
    /// no le deja leer esa tabla más que a administrador y gerente.
    /// </summary>
    private async Task<List<FilaAuditada>> DenegadasAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);

        var filas = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion != null && a.Accion.StartsWith("permiso.denegado."))
            .ToListAsync(ct);

        return [.. filas.Select(a => new FilaAuditada(a.Accion, a.Resultado ?? string.Empty, a.UsuarioId, a.Origen ?? string.Empty))];
    }

    private sealed record FilaAuditada(string Accion, string Resultado, Guid? UsuarioId, string Origen);

    /// <summary>Lo que devuelve <c>POST /api/v1/ia/propuesta</c>, en camelCase.</summary>
    private sealed record Propuesta(Guid Id, string Resultado);
}