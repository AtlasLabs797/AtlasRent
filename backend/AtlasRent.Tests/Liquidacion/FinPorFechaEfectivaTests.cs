using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// F2-09, criterio de aceptación: <b>la unidad queda libre desde la fecha efectiva</b>.
/// <para>
/// No desde que alguien se acuerde de apretar un botón: la baja tiene fecha, y el día que llega el
/// cliente se va. Estas pruebas mueven el reloj del trabajo en vez de esperar días.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FinPorFechaEfectivaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El criterio, dicho entero: una reserva corta en la fecha efectiva choca con el candado mientras
    /// el contrato siga vivo, y entra el mismo día en que la fecha pasa —sin que nadie apriete nada— y
    /// el candado queda apagado, no borrado.
    /// </summary>
    [Fact]
    public async Task LaUnidadQuedaLibreDesdeLaFechaEfectiva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        // El contrato tiene que llevar un bloque ya vencido para que la baja pueda tener fecha en el
        // pasado: §3.5 impide dar de baja antes del fin del bloque en curso.
        var inicio = AyudaContrato.Dia(-400);
        var (unidadId, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(empleado, gerente, bd, inicio, ct);

        var efectiva = AyudaContrato.Dia(1);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = efectiva.ToString("yyyy-MM-dd") }, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);

        // Antes del fin: la unidad no está libre y la reserva no entra.
        var antes = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, efectiva), ct);
        antes.StatusCode.Should().Be(HttpStatusCode.Conflict, "el contrato todavía tiene la unidad");
        (await AyudaLiquidacion.CandadoAsync(bd, contratoId, ct)).Bloquea.Should().BeTrue();

        // El trabajo corre tres días después: para entonces la fecha efectiva ya pasó.
        var resumen = await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(3));

        resumen.ContratosFinalizados.Should().Be(1, "la fecha efectiva ya pasó y el contrato está en baja");
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);

        (await AyudaLiquidacion.FechaFinRealAsync(bd, contratoId, ct))
            .Should().Be(efectiva, "la fecha efectiva es la pactada, no el día que el trabajo corrió");
        (await AyudaLiquidacion.CandadoAsync(bd, contratoId, ct))
            .Bloquea.Should().BeFalse("el candado se apaga, no se borra");

        // Y la reserva corta de ese día ya entra.
        var despues = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, efectiva), ct);
        despues.StatusCode.Should().Be(HttpStatusCode.Created, await despues.Cuerpo());
    }

    /// <summary>
    /// Correr el trabajo dos veces no finaliza dos veces. La idempotencia tiene dos capas y aquí se
    /// comprueban las dos cosas que tienen que ser verdad: un solo fin escrito y una sola fila de
    /// auditoría, porque un segundo "finalizado" del mismo contrato no sería idempotencia sino ruido
    /// en el historial.
    /// </summary>
    [Fact]
    public async Task ElTrabajoDeFinEsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        var efectiva = AyudaContrato.Dia(1);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = efectiva.ToString("yyyy-MM-dd") }, ct);

        var primera = await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(3));
        var segunda = await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(4));

        primera.ContratosFinalizados.Should().Be(1);
        segunda.ContratosFinalizados.Should().Be(0, "el contrato ya no está en baja programada");
        segunda.Omitidos.Should().Be(0, "ni siquiera llega a mirar uno que ya está finalizado");

        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);
        (await AyudaLiquidacion.FechaFinRealAsync(bd, contratoId, ct))
            .Should().Be(efectiva, "la segunda pasada no vuelve a escribir la fecha");

        (await AyudaLiquidacion.AuditoriasDeAccionAsync(bd, contratoId, "contrato.finalizado_por_fecha", ct))
            .Should().ContainSingle("el fin se auditó una vez y solo una");

        // Y el trabajo se identifica como trabajo: sin usuario y con su propio origen, porque la fila
        // de auditoría tiene que poder distinguir "lo hizo el sistema" de "lo hizo un gerente".
        var auditoria = (await AyudaLiquidacion.AuditoriasDeAccionAsync(
            bd, contratoId, "contrato.finalizado_por_fecha", ct)).Single();
        auditoria.UsuarioId.Should().BeNull("el trabajo no tiene sesión y no puede inventarse una");
        auditoria.Origen.Should().Be("job");
        auditoria.Ip.Should().Be("127.0.0.1");
    }

    /// <summary>
    /// La fecha efectiva es una fecha, no una sugerencia. Con la fecha en el futuro el contrato sigue
    /// en baja: finalizar antes sería soltar la unidad con el inquilino dentro, que es el peor error
    /// posible en este flujo.
    /// </summary>
    [Fact]
    public async Task NoSeFinalizaAntesDeLaFechaEfectiva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        var efectiva = AyudaContrato.Dia(30);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = efectiva.ToString("yyyy-MM-dd") }, ct);

        var resumen = await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(3));

        resumen.ContratosFinalizados.Should().Be(0, "la fecha efectiva todavía no ha llegado");
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.BajaProgramada);
        (await AyudaLiquidacion.FechaFinRealAsync(bd, contratoId, ct)).Should().BeNull();
        (await AyudaLiquidacion.CandadoAsync(bd, contratoId, ct))
            .Bloquea.Should().BeTrue("la unidad sigue siendo del cliente que todavía no se ha ido");
        (await AyudaLiquidacion.AuditoriasDeAccionAsync(bd, contratoId, "contrato.finalizado_por_fecha", ct))
            .Should().BeEmpty();
    }

    /// <summary>
    /// La segunda capa de idempotencia es la carrera: dos instancias del trabajo a la vez sobre el
    /// mismo contrato. Una gana y la otra no finaliza dos veces, no falla el proceso y no deja una
    /// segunda fila de auditoría.
    /// </summary>
    [Fact]
    public async Task DosPasadasSimultaneasSoloFinalizanUnaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        var efectiva = AyudaContrato.Dia(1);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = efectiva.ToString("yyyy-MM-dd") }, ct);

        var reloj = new RelojPrueba();
        reloj.Avanzar(TimeSpan.FromDays(3));

        await using var primero = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await using var segundo = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());

        using var barrera = new Barrier(2);

        var una = Task.Run(async () =>
        {
            barrera.SignalAndWait();
            return await AyudaLiquidacion.ServicioDe(primero, reloj).FinalizarVencidosAsync(ct);
        }, ct);

        var otra = Task.Run(async () =>
        {
            barrera.SignalAndWait();
            return await AyudaLiquidacion.ServicioDe(segundo, reloj).FinalizarVencidosAsync(ct);
        }, ct);

        var resúmenes = await Task.WhenAll(una, otra);

        resúmenes.Sum(r => r.ContratosFinalizados)
            .Should().Be(1, "el contrato se finaliza una vez entre las dos pasadas");
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);
        (await AyudaLiquidacion.AuditoriasDeAccionAsync(bd, contratoId, "contrato.finalizado_por_fecha", ct))
            .Should().ContainSingle("la pasada perdedora no dejó rastro");
        (await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct))
            .Should().NotBeNull("la alerta persistente tampoco se duplica en la carrera");
        (await AyudaLiquidacion.CuantasAlertasAsync(bd, contratoId, ct))
            .Should().Be(1, "la clave única de alerta es la red de la carrera");
    }
}