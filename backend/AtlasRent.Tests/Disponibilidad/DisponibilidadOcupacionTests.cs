using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Disponibilidad;

/// <summary>
/// F2-01: la disponibilidad se calcula en la base de datos (AGENTS.md §6,
/// docs/ESPECIFICACION.md §3.4 y §5). Por eso estos tests van contra PostgreSQL de verdad y no
/// contra un doble en memoria: la restricción de exclusión y los triggers de capacidad no existen
/// fuera de la base, y un doble en memoria los dejaría sin probar justo en lo que importa.
/// <para>
/// Cada test crea sus propias unidades, así que no se pisan entre sí aunque compartan la base de
/// la colección.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class DisponibilidadOcupacionTests(ContenedorBdFixture bd)
{
    /// <summary>SQLSTATE de la restricción de exclusión de rangos.</summary>
    private const string SqlstateSolapamiento = "23P01";

    /// <summary>SQLSTATE de los triggers de ocupación (capacidad del día y choque con un rango).</summary>
    private const string SqlstateOcupacion = "23001";

    // ---------------------------------------------------------------- rangos

    /// <summary>
    /// Dos contratos no pueden solaparse en la misma unidad. Es la restricción de exclusión: la
    /// segunda fila nunca llega a existir.
    /// </summary>
    [Fact]
    public async Task SolapamientoRangoFalla()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparRangoAsync(contexto, unidad, new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), ct);

        // Del 15 de marzo al 15 de mayo: se cruza con el anterior.
        contexto.OcupacionRango.Add(Rango(unidad, new DateOnly(2026, 3, 15), new DateOnly(2026, 5, 15)));
        var accion = () => contexto.SaveChangesAsync(ct);

        var error = await ObtenerSqlstateAsync(accion);
        error.Should().Be(SqlstateSolapamiento, "es la restricción de exclusión la que lo rechaza");
        (await contexto.OcupacionRango.CountAsync(r => r.UnidadId == unidad, ct)).Should().Be(1);
    }

    /// <summary>
    /// Dos altas simultáneas del mismo rango: gana una y la otra recibe el error. Es el criterio de
    /// aceptación de la tarea y la razón de que la comprobación esté en la base y no antes del
    /// INSERT.
    /// </summary>
    [Fact]
    public async Task SolapamientoConcurrente()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);

        var directos = 0;
        var barrera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Dos contextos distintos = dos conexiones distintas, que es lo que hace que las dos
        // inserciones puedan ir de verdad a la vez.
        async Task<bool> IntentarAsync(DateOnly inicio, DateOnly fin)
        {
            await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
            contexto.OcupacionRango.Add(Rango(unidad, inicio, fin));

            if (Interlocked.Increment(ref directos) == 2)
            {
                barrera.SetResult();
            }

            await barrera.Task;

            try
            {
                await contexto.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException pg
                && pg.SqlState == SqlstateSolapamiento)
            {
                return false;
            }
        }

        var tareas = new[]
        {
            Task.Run(() => IntentarAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)), ct),
            Task.Run(() => IntentarAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 7, 10)), ct),
        };

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(30), ct);

        resultados.Count(ganada => ganada).Should().Be(1,
            "de dos contratos simultáneos sobre las mismas fechas solo puede quedar uno");

        await using var verificacion = bd.CrearContexto(bd.CadenaApp, sesion);
        (await verificacion.OcupacionRango.CountAsync(r => r.UnidadId == unidad && r.Bloquea, ct))
            .Should().Be(1);
    }

    /// <summary>
    /// Un rango que empieza el día que acaba otro no se solapa: el rango es [inicio, fin). Un
    /// contrato que termina el 31 deja libre el 1 siguiente.
    /// </summary>
    [Fact]
    public async Task RangoQueEmpiezaAlDiaSiguienteDelAnteriorNoChoca()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparRangoAsync(contexto, unidad, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), ct);

        await OcuparRangoAsync(contexto, unidad, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), ct);

        (await contexto.OcupacionRango.CountAsync(r => r.UnidadId == unidad && r.Bloquea, ct))
            .Should().Be(2, "el 1 de septiembre está libre: el anterior acaba ese día, sin incluirlo");
    }

    // ---------------------------------------------------------------- medios días

    /// <summary>
    /// Capacidad de la unidad: 2 medios días o 1 día completo (spec §3.4). Los dos primeros entran y
    /// el tercero se queda fuera.
    /// </summary>
    [Fact]
    public async Task DosMediosDiasOKYTerceroFalla()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 9, 15);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);

        await OcuparDiaAsync(contexto, unidad, dia, TipoOcupacionDia.MedioDia, ct);
        await OcuparDiaAsync(contexto, unidad, dia, TipoOcupacionDia.MedioDia, ct);

        var tercero = Dia(unidad, dia, TipoOcupacionDia.MedioDia);
        contexto.OcupacionDia.Add(tercero);
        var accion = () => contexto.SaveChangesAsync(ct);

        (await ObtenerSqlstateAsync(accion)).Should().Be(SqlstateOcupacion,
            "es el trigger de capacidad el que rechaza el tercer medio día");

        (await contexto.OcupacionDia.CountAsync(d => d.UnidadId == unidad && d.Bloquea, ct))
            .Should().Be(2);
    }

    /// <summary>
    /// Tres medios días a la vez: el bloqueo por unidad del trigger los serializa, así que sobreviven
    /// exactamente dos. Sin ese SELECT ... FOR UPDATE, los tres contarían cero días y entrarían los
    /// tres.
    /// </summary>
    [Fact]
    public async Task MediosDiasConcurrentes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 9, 16);

        var directos = 0;
        var barrera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> IntentarAsync()
        {
            await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
            contexto.OcupacionDia.Add(Dia(unidad, dia, TipoOcupacionDia.MedioDia));

            if (Interlocked.Increment(ref directos) == 3)
            {
                barrera.SetResult();
            }

            await barrera.Task;

            try
            {
                await contexto.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException pg
                && pg.SqlState == SqlstateOcupacion)
            {
                return false;
            }
        }

        var tareas = new[] { Task.Run(IntentarAsync, ct), Task.Run(IntentarAsync, ct), Task.Run(IntentarAsync, ct) };
        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(30), ct);

        resultados.Count(entra => entra).Should().Be(2, "la capacidad del día son dos medios días");

        await using var verificacion = bd.CrearContexto(bd.CadenaApp, sesion);
        (await verificacion.OcupacionDia.CountAsync(d => d.UnidadId == unidad && d.Bloquea, ct))
            .Should().Be(2);
    }

    /// <summary>Un día completo ya ocupado no admite ni un medio día más.</summary>
    [Fact]
    public async Task DiaCompletoExistenteBloqueaElMedioDia()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 9, 17);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparDiaAsync(contexto, unidad, dia, TipoOcupacionDia.DiaCompleto, ct);

        contexto.OcupacionDia.Add(Dia(unidad, dia, TipoOcupacionDia.MedioDia));
        var accion = () => contexto.SaveChangesAsync(ct);

        (await ObtenerSqlstateAsync(accion)).Should().Be(SqlstateOcupacion);
    }

    /// <summary>Un medio día no se convierte en día completo si el día ya está ocupado.</summary>
    [Fact]
    public async Task MedioDiaExistenteBloqueaElDiaCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 9, 18);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparDiaAsync(contexto, unidad, dia, TipoOcupacionDia.MedioDia, ct);

        contexto.OcupacionDia.Add(Dia(unidad, dia, TipoOcupacionDia.DiaCompleto));
        var accion = () => contexto.SaveChangesAsync(ct);

        (await ObtenerSqlstateAsync(accion)).Should().Be(SqlstateOcupacion);
    }

    // ---------------------------------------------------------------- rangos y días juntos

    /// <summary>
    /// Un contrato no puede tapar una reserva corta que ya existe. Sin este trigger, el contrato se
    /// solaparía en silencio con la reserva y los dos blastos saldrían.
    /// </summary>
    [Fact]
    public async Task RangoBloqueaDia()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparDiaAsync(contexto, unidad, new DateOnly(2026, 9, 20), TipoOcupacionDia.MedioDia, ct);

        // El rango empieza después del día reservado y lo cubre entero.
        contexto.OcupacionRango.Add(
            Rango(unidad, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 21)));
        var accion = () => contexto.SaveChangesAsync(ct);

        (await ObtenerSqlstateAsync(accion)).Should().Be(SqlstateOcupacion,
            "lo rechaza el trigger simétrico del rango");
    }

    /// <summary>
    /// Y al revés: un día de reserva corta no se puede meter dentro de un contrato ya activo.
    /// </summary>
    [Fact]
    public async Task DiaBloqueaRango()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparRangoAsync(contexto, unidad, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), ct);

        contexto.OcupacionDia.Add(
            Dia(unidad, new DateOnly(2026, 9, 10), TipoOcupacionDia.MedioDia));
        var accion = () => contexto.SaveChangesAsync(ct);

        (await ObtenerSqlstateAsync(accion)).Should().Be(SqlstateOcupacion);
    }

    // ---------------------------------------------------------------- liberar

    /// <summary>
    /// Liberar es poner <c>bloquea = false</c>, no borrar: la fila se queda (AGENTS.md §6, los
    /// registros con efecto económico no se borran) y el día vuelve a estar disponible.
    /// </summary>
    [Fact]
    public async Task LiberarPermiteReutilizarYConservaLaFila()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 9, 25);
        var origenId = Guid.NewGuid();

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(contexto, new RelojPrueba())
                .OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, origenId,
                    [(dia, TipoOcupacionDia.MedioDia)], ct);
        }

        // Un día completo solo entra después de liberar el medio día: es el caso que demuestra que
        // liberar de verdad devuelve la unidad, porque con el medio día ocupado el día completo no cabía.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(contexto, new RelojPrueba())
                .LiberarDiasAsync(OrigenOcupacion.ReservaCorta, origenId, ct);
        }

        await using (var reutiliza = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(reutiliza, new RelojPrueba())
                .OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, Guid.NewGuid(),
                    [(dia, TipoOcupacionDia.DiaCompleto)], ct);

            var filas = await reutiliza.OcupacionDia
                .Where(d => d.UnidadId == unidad)
                .ToListAsync(ct);

            filas.Should().HaveCount(2, "la fila liberada sigue ahí, con su historia");
            filas.Count(f => !f.Bloquea).Should().Be(1);
            filas.Single(f => f.Bloquea).Tipo.Should().Be(TipoOcupacionDia.DiaCompleto);
        }
    }

    /// <summary>
    /// El día completo que se acaba de liberar tampoco bloquea a un rango posterior.
    /// </summary>
    [Fact]
    public async Task LiberarUnRangoPermiteUnaReservaEnEsasFechas()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var origenId = Guid.NewGuid();

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(contexto, new RelojPrueba())
                .OcuparRangoAsync(unidad, OrigenOcupacion.Contrato, origenId,
                    new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), ct);
        }

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(contexto, new RelojPrueba())
                .LiberarRangoAsync(OrigenOcupacion.Contrato, origenId, ct);
        }

        await using (var reutiliza = bd.CrearContexto(bd.CadenaApp, sesion))
        {
            await new ServicioDisponibilidad(reutiliza, new RelojPrueba())
                .OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, Guid.NewGuid(),
                    [(new DateOnly(2026, 10, 15), TipoOcupacionDia.MedioDia)], ct);
        }
    }

    // ---------------------------------------------------------------- aislamiento

    /// <summary>El bloqueo es por unidad: la misma fecha en otra unidad no se ve afectada.</summary>
    [Fact]
    public async Task UnidadDistintaNoSeBloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var una = await CrearUnidadAsync(sesion, ct);
        var otra = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 11, 5);

        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await OcuparDiaAsync(contexto, una, dia, TipoOcupacionDia.DiaCompleto, ct);
        await OcuparDiaAsync(contexto, otra, dia, TipoOcupacionDia.DiaCompleto, ct);

        (await contexto.OcupacionDia.CountAsync(d => (d.UnidadId == una || d.UnidadId == otra) && d.Bloquea, ct))
            .Should().Be(2, "cada unidad lleva su propio bloqueo y ninguna estorba a la otra");
    }

    // ---------------------------------------------------------------- traducción

    /// <summary>
    /// El servicio convierte el fallo de la base en un error de dominio propio, no en un
    /// <c>DbUpdateException</c> con el SQLSTATE suelto: quien llama lo traduce a 409 con
    /// <see cref="Problemas.UnidadNoDisponible"/>. No hay endpoint todavía (los de reservas y
    /// contratos son de F2-03 y F2-05), así que aquí se comprueba la frontera del servicio.
    /// </summary>
    [Fact]
    public async Task ElServicioTraduceLaViolacionAErrorDeDominio()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        var unidad = await CrearUnidadAsync(sesion, ct);
        var dia = new DateOnly(2026, 11, 10);
        var servicio = new ServicioDisponibilidad(bd.CrearContexto(bd.CadenaApp, sesion), new RelojPrueba());

        await servicio.OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, Guid.NewGuid(),
            [(dia, TipoOcupacionDia.MedioDia)], ct);
        await servicio.OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, Guid.NewGuid(),
            [(dia, TipoOcupacionDia.MedioDia)], ct);

        var accion = () => servicio.OcuparDiasAsync(unidad, OrigenOcupacion.ReservaCorta, Guid.NewGuid(),
            [(dia, TipoOcupacionDia.MedioDia)], ct);

        var error = await accion.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("no está disponible");
        error.Which.InnerException.Should().BeOfType<PostgresException>(
            "el PostgreSQLException original se conserva para el registro");
    }

    // ---------------------------------------------------------------- utilidades

    private async Task<Guid> CrearUnidadAsync(ContextoSesion sesion, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaMigrador);

        var inmueble = new Inmueble
        {
            Id = Guid.NewGuid(),
            Nombre = "Inmueble de prueba de disponibilidad",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var unidad = new Unidad
        {
            Id = Guid.NewGuid(),
            InmuebleId = inmueble.Id,
            Nombre = $"Unidad {Guid.NewGuid()}",
            Tipo = TipoUnidad.Oficina,
            EstadoManual = EstadoUnidad.Disponible,
            Activo = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        contexto.Inmueble.Add(inmueble);
        contexto.Unidad.Add(unidad);
        await contexto.SaveChangesAsync(ct);

        return unidad.Id;
    }

    private static OcupacionRango Rango(Guid unidadId, DateOnly inicio, DateOnly? fin) => new()
    {
        Id = Guid.NewGuid(),
        UnidadId = unidadId,
        Origen = OrigenOcupacion.Contrato,
        OrigenId = Guid.NewGuid(),
        FechaInicio = inicio,
        FechaFin = fin,
        Bloquea = true,
        CreadoAt = DateTimeOffset.UtcNow,
    };

    private static OcupacionDia Dia(Guid unidadId, DateOnly fecha, TipoOcupacionDia tipo) => new()
    {
        Id = Guid.NewGuid(),
        UnidadId = unidadId,
        Origen = OrigenOcupacion.ReservaCorta,
        OrigenId = Guid.NewGuid(),
        Fecha = fecha,
        Tipo = tipo,
        Bloquea = true,
        CreadoAt = DateTimeOffset.UtcNow,
    };

    private static async Task OcuparRangoAsync(
        AtlasRentDbContext contexto, Guid unidadId, DateOnly inicio, DateOnly fin, CancellationToken ct)
    {
        contexto.OcupacionRango.Add(Rango(unidadId, inicio, fin));
        await contexto.SaveChangesAsync(ct);
    }

    private static async Task OcuparDiaAsync(
        AtlasRentDbContext contexto, Guid unidadId, DateOnly fecha, TipoOcupacionDia tipo, CancellationToken ct)
    {
        contexto.OcupacionDia.Add(Dia(unidadId, fecha, tipo));
        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>Devuelve el SQLSTATE con el que PostgreSQL rechazó la escritura.</summary>
    private static async Task<string> ObtenerSqlstateAsync(Func<Task> operacion)
    {
        var error = await operacion.Should().ThrowAsync<DbUpdateException>();
        return error.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState;
    }
}