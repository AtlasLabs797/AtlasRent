using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: la solicitud de aprobación <b>caduca si el contrato cambia</b>, el otro criterio de
/// aceptación del plan.
/// <para>
/// El agujero que tapa: el empleado manda el contrato a aprobación y, mientras el gerente lo mira,
/// lo cambia por debajo. Sin caducidad, el gerente aprueba a ciegas un contrato que ya no es el que
/// le enseñaron. Con caducidad, aprobar un contrato editado falla con 409 y hay que volver a
/// enviarlo.
/// </para>
/// <para>
/// Y el revés, que es el detalle que hay que acertar: el propio paso a "pendiente" no puede
/// invalidar su propia solicitud, o el contrato se caducaría solo en el instante de enviarlo.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoSolicitudTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Enviar a aprobación, editar el contrato y aprobar: 409 y hay que volver a enviar. Después de
    /// reenviarlo, aprobar sí funciona.
    /// </summary>
    [Fact]
    public async Task LaSolicitudCaducaSiElContratoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var dado = await PendienteAsync(empleado, gerente, 120, ct);

        // El empleado cambia la renta mientras espera. La edición es legal; lo que caduca es la
        // solicitud.
        var editado = await empleado.PutAsync(
            $"/api/v1/contratos/{dado.ContratoId:D}",
            AyudaContrato.Peticion(dado.ClienteId, dado.UnidadId, dado.FacturadoraId, dado.Inicio, renta: 75_000m),
            ct);
        editado.StatusCode.Should().Be(HttpStatusCode.OK, await editado.Cuerpo());

        var caducada = await AyudaContrato.Boton(gerente, dado.ContratoId, "aprobacion", ct: ct);
        caducada.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "lo que hay ahora no es lo que se pidió aprobar, así que no se puede aprobar a ciegas");

        (await AyudaContrato.EstadoAsync(bd, dado.ContratoId, ct)).ToString().Should().Be("PendienteAprobacion");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var solicitudes = await contexto.SolicitudAprobacion.AsNoTracking()
                .Where(s => s.ContratoId == dado.ContratoId)
                .OrderBy(s => s.SolicitadoAt)
                .Select(s => new { s.Resultado, s.MotivoCaducidad })
                .ToListAsync(ct);

            solicitudes.Should().HaveCount(1);
            solicitudes[0].Resultado.ToString().Should().Be("Caducada");
            solicitudes[0].MotivoCaducidad.Should()
                .NotBeNullOrWhiteSpace("la caducidad dice por qué, que es lo que necesita la bandeja de F2-10");
        }

        // Volver a enviar y entonces sí se aprueba.
        await AyudaContrato.AceptarAsync(empleado, dado.ContratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, dado.ContratoId, "aprobacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, dado.ContratoId, ct)).ToString().Should().Be("Aprobado");

        // Las dos solicitudes se quedan en la base: la caducada y la buena.
        await using var final = bd.CrearContexto(bd.CadenaApp);
        var total = await final.SolicitudAprobacion.AsNoTracking()
            .CountAsync(s => s.ContratoId == dado.ContratoId, ct);
        total.Should().Be(2, "el historial de solicitudes no se borra (AGENTS.md §6)");
    }

    /// <summary>
    /// El revés, que es el que se rompe solo: aprobar lo recién enviado, sin tocar nada, tiene que
    /// funcionar. Si la huella incluyera el estado, el propio envío cambiaría la huella que acababa
    /// de calcular y el contrato quedaría caducado sin que nadie lo tocara.
    /// </summary>
    [Fact]
    public async Task AprobarRecienEnviadoFunciona()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var dado = await PendienteAsync(empleado, gerente, 150, ct);

        await AyudaContrato.AceptarAsync(gerente, dado.ContratoId, "aprobacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, dado.ContratoId, ct)).ToString().Should().Be("Aprobado");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var solicitud = await contexto.SolicitudAprobacion.AsNoTracking()
            .SingleAsync(s => s.ContratoId == dado.ContratoId, ct);
        solicitud.Resultado.ToString().Should().Be("Aprobada");
        solicitud.MotivoCaducidad.Should().BeNull();
        solicitud.HuellaContenido.Should().HaveLength(64, "la huella es el SHA-256 en hexadecimal");
    }

    /// <summary>
    /// Editar un contrato pendiente <b>sin cambiar nada</b> no caduca la solicitud: si lo hiciera,
    /// un guardado sin cambios dejaría el contrato sin poder aprobarse.
    /// </summary>
    [Fact]
    public async Task EditarSinCambiarNoCaducaLaSolicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var dado = await PendienteAsync(empleado, gerente, 180, ct);

        var guardado = await empleado.PutAsync(
            $"/api/v1/contratos/{dado.ContratoId:D}",
            AyudaContrato.Peticion(dado.ClienteId, dado.UnidadId, dado.FacturadoraId, dado.Inicio),
            ct);
        guardado.StatusCode.Should().Be(HttpStatusCode.OK, await guardado.Cuerpo());

        await AyudaContrato.AceptarAsync(gerente, dado.ContratoId, "aprobacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, dado.ContratoId, ct)).ToString().Should().Be("Aprobado");
    }

    /// <summary>Datos del contrato que quedó esperando aprobación, para poder reenviarlo o editarlo.</summary>
    private sealed record Pendiente(Guid ContratoId, Guid ClienteId, Guid UnidadId, Guid FacturadoraId, DateOnly Inicio);

    private async Task<Pendiente> PendienteAsync(
        SesionCliente empleado,
        SesionCliente aprobador,
        int desplazamiento,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(desplazamiento);

        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);

        return new Pendiente(id, clienteId, unidadId, facturadoraId, inicio);
    }
}