using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Renovaciones;

/// <summary>
/// F2-06: la renovación automática por bloques y los incrementos de renta.
/// <para>
/// El trabajo se ejecuta directamente en vez de esperar al planificador, que está apagado en las
/// pruebas; lo que se prueba es el trabajo, no el cron, que se comprueba en
/// <c>ProgramacionJobsTests</c>. Cada prueba crea su unidad, así que no se pisan entre sí aunque
/// compartan la base de datos de la colección.
/// </para>
/// <para>
/// Las cuentas que devuelve <see cref="ResumenRenovacion"/> cuentan lo que hizo el trabajo sobre
/// <b>todos</b> los contratos de la base compartida, así que las pruebas comprueban el estado de su
/// propio contrato y no esos contadores.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RenovacionContratosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Criterio de aceptación de F2-06: ejecutar el trabajo dos veces no duplica nada. Un bloque
    /// nuevo, la renta incrementada una sola vez y un solo aviso por incremento.
    /// <para>
    /// La segunda ejecución no renueva porque su propio resultado se lo impide: al terminar, el fin
    /// del bloque en curso ya no es anterior a hoy en Santo Domingo, así que el contrato ni entra en
    /// la lista de candidatos. Y el aviso va dentro de la transacción del incremento, con la clave de
    /// deduplicación del propio incremento, que es la misma que usa el resto de alertas del sistema.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElTrabajoDosVecesNoDuplicaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);
        var finInicial = await FinVigenteAsync(contratoId, ct);
        var incrementoId = await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.Porcentaje,
            porcentaje: 10m,
            importe: null,
            finInicial,
            ct);
        var versionesAntes = (await VersionesAsync(contratoId, ct)).Count;

        await EjecutarAsync(ct);
        await EjecutarAsync(ct);

        // Un solo bloque nuevo.
        var bloques = await BloquesAsync(contratoId, ct);
        bloques.Should().ContainSingle("la segunda ejecución no abre un segundo bloque");
        bloques[0].Numero.Should().Be(2, "el bloque 1 es el que se firmó y no tiene fila propia");

        // La renta, incrementada una sola vez.
        (await RentaAsync(contratoId, ct)).Should().Be(55_000m);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var incremento = await contexto.ContratoIncremento.AsNoTracking()
            .SingleAsync(i => i.Id == incrementoId, ct);
        incremento.Aplicado.Should().BeTrue();
        incremento.AplicadoAt.Should().NotBeNull();
        incremento.RentaAntes.Should().Be(50_000m);
        incremento.RentaDespues.Should().Be(55_000m);

        // Y un solo aviso.
        var alertas = await contexto.Alerta.AsNoTracking()
            .Where(a => a.Clave == ServicioRenovaciones.ClaveIncremento(incrementoId))
            .ToListAsync(ct);
        alertas.Should().ContainSingle("un aviso por incremento, no uno por ejecución");
        alertas[0].Tipo.Should().Be("contrato");
        alertas[0].Repeticiones.Should().Be(
            1, "el aviso se escribe una vez, dentro de la transacción del incremento");

        (await VersionesAsync(contratoId, ct)).Should().HaveCount(
            versionesAntes + 1, "el incremento abre una versión y la segunda ejecución ninguna más");
    }

    /// <summary>
    /// Un contrato cuyo bloque no ha vencido no se toca: la condición es el fin del bloque anterior a
    /// hoy, no "está activo y el trabajo puede con él".
    /// </summary>
    [Fact]
    public async Task UnContratoConElBloqueSinVencerNoSeRenueva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, AyudaContrato.Dia(30), 50_000m, ct);
        var finAntes = await FinVigenteAsync(contratoId, ct);
        var versionesAntes = await VersionesAsync(contratoId, ct);

        await EjecutarAsync(ct);

        (await BloquesAsync(contratoId, ct)).Should().BeEmpty();
        (await FinVigenteAsync(contratoId, ct)).Should().Be(finAntes, "el bloque en curso no se adelanta");
        (await VersionesAsync(contratoId, ct))
            .Should().HaveCount(versionesAntes.Count, "sin cambio de renta, el trabajo no abre ninguna versión");
    }

    /// <summary>
    /// El bloque avanza exactamente <c>RenovacionMeses</c> y la duración que se firmó no se toca:
    /// §3.5 dice que la renovación "queda en el historial", no que rehaga el contrato.
    /// </summary>
    [Fact]
    public async Task LaRenovacionSumaLosMesesYNoTocaLaFechaInicial()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        // Fechas literales y reloj fijo: el esperado no se calcula con la fórmula del código.
        var contratoId = await ContratoActivoAsync(
            empleado,
            gerente,
            new DateOnly(2025, 1, 15),
            50_000m,
            ct,
            renovacionMeses: 6,
            regla: "Personalizada");

        var inicio = await InicioAsync(contratoId, ct);
        var finInicial = await FinInicialAsync(contratoId, ct);
        inicio.Should().Be(new DateOnly(2025, 1, 15));
        finInicial.Should().Be(new DateOnly(2026, 1, 15), "15/01/2025 más 12 meses");
        var esperado = new DateOnly(2026, 7, 15);

        await EjecutarAsync(ct, en: RelojFijo);

        (await InicioAsync(contratoId, ct)).Should().Be(inicio, "el inicio es lo que se firmó");
        (await FinInicialAsync(contratoId, ct)).Should().Be(
            finInicial, "la duración inicial no la mueve nadie nunca");

        (await FinVigenteAsync(contratoId, ct)).Should().Be(esperado, "el bloque avanza los meses pactados");

        var bloques = await BloquesAsync(contratoId, ct);
        bloques.Should().ContainSingle();
        bloques[0].FechaInicio.Should().Be(finInicial, "el bloque nuevo empieza donde acaba el anterior");
        bloques[0].FechaFin.Should().Be(esperado);
        bloques[0].Renta.Should().Be(50_000m, "sin incrementos, la renta del bloque es la que había");
    }

    /// <summary>
    /// El incremento de porcentaje multiplica la renta vigente, redondea medio hacia arriba a dos
    /// decimales y entra por el historial de versiones con su alcance, porque un cambio de renta es
    /// un cambio de renta. El aviso interno sale en la misma transacción.
    /// <para>
    /// El importe está elegido para que el redondeo se vea: 1 000,10 × 1,05 = 1 050,105. Medio hacia
    /// arriba da 1 050,11 y el medio hacia el par (el que usa <c>decimal.Round</c> por defecto)
    /// daría 1 050,10, que es un céntimo menos de lo pactado.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElIncrementoDePorcentajeSeAplicaYSeAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 1_000.10m, ct);
        var incrementoId = await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.Porcentaje,
            porcentaje: 5m,
            importe: null,
            await FinVigenteAsync(contratoId, ct),
            ct);

        await EjecutarAsync(ct);

        (await RentaAsync(contratoId, ct)).Should().Be(
            1_050.11m, "1 000,10 × 1,05 = 1 050,105, redondeado medio hacia arriba");

        var versiones = await VersionesAsync(contratoId, ct);
        var delIncremento = versiones[^1];
        delIncremento.Should().NotBeNull("el incremento abre su propia versión en el historial");
        delIncremento.RentaAnterior.Should().Be(1_000.10m);
        delIncremento.RentaNueva.Should().Be(1_050.11m);
        delIncremento.Alcance.Should().Contain(
            "facturas futuras",
            "§3.5: los cambios de renta afectan solo a facturas futuras, y el incremento también");
        delIncremento.Cambio.Should().Contain("5 %", "la versión dice qué incremento se aplicó");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking()
            .AnyAsync(a => a.Clave == ServicioRenovaciones.ClaveIncremento(incrementoId), ct))
            .Should().BeTrue("el incremento se aplica con aviso interno, no a oscuras");
    }

    /// <summary>
    /// El importe fijo se suma tal cual y en la moneda del contrato: el contrato está en dólares y el
    /// incremento también, sin convertir a la moneda de nada.
    /// </summary>
    [Fact]
    public async Task ElIncrementoDeImporteFijoSeAplicaYSeAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(
            empleado, gerente, Vencido(13), 500m, ct, moneda: "USD");
        var incrementoId = await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.ImporteFijo,
            porcentaje: null,
            importe: 150.75m,
            await FinVigenteAsync(contratoId, ct),
            ct);

        await EjecutarAsync(ct);

        (await RentaAsync(contratoId, ct)).Should().Be(
            650.75m, "un importe fijo es una suma absoluta, no un porcentaje mal leído");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var contrato = await contexto.Contrato.AsNoTracking()
            .Where(c => c.Id == contratoId)
            .Select(c => new { c.Moneda })
            .SingleAsync(ct);
        contrato.Moneda.Should().Be(Moneda.USD, "la renta sigue en la moneda del contrato");

        var alerta = await contexto.Alerta.AsNoTracking()
            .SingleAsync(a => a.Clave == ServicioRenovaciones.ClaveIncremento(incrementoId), ct);
        alerta.Mensaje.Should().Contain("importe fijo", "el aviso dice de qué incremento se trata");
        alerta.Tipo.Should().Be("contrato");
    }

    /// <summary>
    /// Un incremento con fecha posterior al inicio del bloque nuevo no entra todavía: espera a su
    /// bloque. Cuando ese bloque llega, se aplica sin que nadie lo pida otra vez.
    /// </summary>
    [Fact]
    public async Task UnIncrementoFuturoNoSeAplica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(14), 50_000m, ct);

        // Fecha dentro del bloque que va a abrirse ahora, pero después de su inicio.
        var enTresMeses = (await FinVigenteAsync(contratoId, ct)).AddMonths(3);
        var incrementoId = await CrearIncrementoAsync(
            bd, contratoId, TipoIncremento.Porcentaje, porcentaje: 20m, importe: null, enTresMeses, ct);

        // El primer bloque se abre y el incremento no se toca.
        await EjecutarAsync(ct);
        (await RentaAsync(contratoId, ct)).Should().Be(50_000m, "una subida con fecha futura no adelanta la renta");
        (await AplicadoAsync(incrementoId, ct)).Should().BeFalse();
        (await BloquesAsync(contratoId, ct)).Should().HaveCount(1);

        // Once meses después, ese bloque también ha vencido y el incremento le toca.
        await EjecutarAsync(ct, avanzar: TimeSpan.FromDays(340));

        (await RentaAsync(contratoId, ct)).Should().Be(60_000m);
        (await AplicadoAsync(incrementoId, ct)).Should().BeTrue("el incremento se aplicó una vez, en su bloque");
        (await BloquesAsync(contratoId, ct)).Should().HaveCount(2);
    }

    /// <summary>
    /// Un contrato en baja programada no se renueva: tiene fecha de fin acordada y renovar por detrás
    /// sería alargar un contrato que alguien ya dijo que termina.
    /// </summary>
    [Fact]
    public async Task UnContratoEnBajaProgramadaNoSeRenueva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(14), 50_000m, ct);
        await AyudaContrato.AceptarAsync(
            gerente,
            contratoId,
            "baja",
            new { fechaFin = TiempoNegocio.Hoy().AddMonths(6).ToString("yyyy-MM-dd") },
            ct);

        await EjecutarAsync(ct);

        (await BloquesAsync(contratoId, ct)).Should().BeEmpty();
        (await RentaAsync(contratoId, ct)).Should().Be(50_000m);
    }

    /// <summary>
    /// Renovar no suelta la unidad ni toca su candado. Al activarse, el contrato ocupa la unidad con
    /// un rango abierto que sigue bloqueando hasta que se finaliza, así que renovar no tiene nada que
    /// hacer ahí: si una reserva corta en las fechas del bloque nuevo entra, es un agujero.
    /// </summary>
    [Fact]
    public async Task RenovarNoLiberaLaUnidadNiTocaElCandado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var contratoId = await ContratoActivoAsync(
            empleado, gerente, Vencido(14), 50_000m, ct, clienteId: clienteId, unidadId: unidadId);

        var antes = await CandadoAsync(contratoId, ct);
        antes.Bloquea.Should().BeTrue();
        antes.FechaFin.Should().BeNull("el rango del contrato es abierto");

        await EjecutarAsync(ct);

        var despues = await CandadoAsync(contratoId, ct);
        despues.Bloquea.Should().BeTrue("renovar no suelta la unidad");
        despues.FechaInicio.Should().Be(antes.FechaInicio);
        despues.FechaFin.Should().BeNull("el rango sigue abierto y siendo el mismo");
        (await CuentaCandadosAsync(contratoId, ct)).Should().Be(1, "renovar no crea ni borra candados");

        // Y una reserva corta dentro del bloque nuevo sigue sin caber.
        var dentro = (await FinVigenteAsync(contratoId, ct)).AddDays(10);
        var reserva = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, dentro), ct);
        reserva.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "la unidad la tiene el contrato y renovar no la ha soltado");
    }

    /// <summary>
    /// Dos ejecuciones simultáneas del trabajo sobre el mismo contrato dejan un solo bloque y una
    /// sola subida de renta.
    /// <para>
    /// Las dos son de verdad concurrentes: cada una con su contexto y su conexión, y las dos esperan
    /// en una barrera antes de empezar, de modo que leen el contrato antes de que ninguna escriba.
    /// Con eso las dos calculan el mismo número de bloque y las dos intentan escribirlo, y el índice
    /// único de <c>(contrato_id, numero)</c> deja pasar a una mientras a la otra la para. Lo que
    /// importa no es cuál gana, sino que el contrato termine con un bloque y no con dos y con la renta
    /// subida una vez: si las dos llegaran a confirmar, el fin de bloque avanzaría 24 meses.
    /// </para>
    /// <para>
    /// También cabe que la segunda ejecución lea el contrato ya renovado y no tenga nada que hacer,
    /// que es la otra mitad de la idempotencia. Las dos salidas son correctas y cumplen las mismas
    /// aserciones.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DosEjecucionesSimultaneasNoCreanDosBloques()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, new DateOnly(2025, 1, 15), 50_000m, ct);
        var incrementoId = await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.Porcentaje,
            porcentaje: 10m,
            importe: null,
            await FinVigenteAsync(contratoId, ct),
            ct);

        var reloj = new RelojPrueba(RelojFijo);
        await using var primero = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await using var segundo = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());

        using var barrera = new Barrier(2);

        var una = Task.Run(async () =>
        {
            barrera.SignalAndWait();
            await new ServicioRenovaciones(
                    primero, new AuditoriaService(primero), new ServicioAlertas(primero, reloj), reloj)
                .RenovarVencidosAsync(ct);
        }, ct);

        var otra = Task.Run(async () =>
        {
            barrera.SignalAndWait();
            await new ServicioRenovaciones(
                    segundo, new AuditoriaService(segundo), new ServicioAlertas(segundo, reloj), reloj)
                .RenovarVencidosAsync(ct);
        }, ct);

        await Task.WhenAll(una, otra);

        (await BloquesAsync(contratoId, ct))
            .Should().ContainSingle("el índice único de (contrato_id, numero) es la red de la carrera");
        (await FinVigenteAsync(contratoId, ct))
            .Should().Be(new DateOnly(2027, 1, 15), "15/01/2026 más 12 meses: el bloque avanza una vez, no dos");
        (await RentaAsync(contratoId, ct)).Should().Be(55_000m, "el incremento se aplica una vez");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking()
            .CountAsync(a => a.Clave == ServicioRenovaciones.ClaveIncremento(incrementoId), ct))
            .Should().Be(1, "la ejecución perdedora no dejó su aviso: iba en la transacción que se deshizo");

        // Y la red está en la base, no solo en el modelo.
        var indice = await contexto.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'contrato_bloque'
                  AND indexname = 'ix_contrato_bloque_contrato_id_numero'
                """)
            .SingleAsync(ct);
        indice.Should().Contain("UNIQUE", "el índice único de (contrato_id, numero) es la red de la carrera");
    }

    /// <summary>
    /// La carrera de la renovación no tumba el trabajo: la ejecución que pierde se cuenta como
    /// omitida y ninguna de las dos lanza.
    /// <para>
    /// Aquí la carrera no se deja al azar. El trabajo registra el aviso del incremento con
    /// <c>IRegistroAlertas</c> en un punto exacto: después de leer el contrato, de calcular el
    /// número de bloque y de decidir qué incrementos aplicar, y <b>antes</b> de escribir nada. La
    /// barrera se pone ahí, dentro de la transacción, así que las dos ejecuciones quedan con el
    /// mismo estado viejo en la memoria y sueltan a la vez. Lo que viene después es lo que hay que
    /// aguantar: las dos escriben, una gana y la otra recibe una excepción de la base.
    /// </para>
    /// <para>
    /// La que pierde puede recibirla de dos maneras y las dos son la misma carrera. Si su lote de
    /// comandos choca antes con el índice único de <c>(contrato_id, numero)</c> es un
    /// <c>DbUpdateException</c> con violación de índice único; si el <c>UPDATE</c> de
    /// <c>contrato</c> va primero, su <c>WHERE xmin = &lt;el leído&gt;</c> ya no casa con ninguna fila
    /// y es un <c>DbUpdateConcurrencyException</c>, que hereda de <c>DbUpdateException</c> pero no
    /// lleva un <c>PostgresException</c> dentro. Antes de arreglarlo, la segunda se escapaba y el
    /// trabajo de Hangfire quedaba fallido. Lo que importa es que ahora ninguna de las dos sale por
    /// encima: el trabajo de la noche tiene que poder terminar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaCarreraDeLaRenovacionNingunaEjecucionLanza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, new DateOnly(2025, 1, 15), 50_000m, ct);
        var incrementoId = await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.Porcentaje,
            porcentaje: 10m,
            importe: null,
            await FinVigenteAsync(contratoId, ct),
            ct);

        var reloj = new RelojPrueba(RelojFijo);
        await using var primero = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await using var segundo = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());

        using var barrera = new Barrier(2);

        ResumenRenovacion[] resumenes = [new ResumenRenovacion(), new ResumenRenovacion()];
        Exception?[] fallos = new Exception?[2];

        var ejecuciones = new[] { (primero, 0), (segundo, 1) }
            .Select(par => Task.Run(async () =>
            {
                try
                {
                    resumenes[par.Item2] = await new ServicioRenovaciones(
                            par.Item1,
                            new AuditoriaService(par.Item1),
                            new AlertaConBarrera(new ServicioAlertas(par.Item1, reloj), barrera),
                            reloj)
                        .RenovarVencidosAsync(ct);
                }
                catch (Exception excepcion)
                {
                    fallos[par.Item2] = excepcion;
                }
            }, ct))
            .ToArray();

        await Task.WhenAll(ejecuciones);

        fallos[0].Should().BeNull("la ejecución perdedora tiene que quedarse en omitido, no romper el trabajo");
        fallos[1].Should().BeNull("ninguna de las dos ejecuciones puede dejar el trabajo fallido");

        resumenes.Sum(r => r.ContratosRenovados).Should().Be(1, "el contrato se renovó una vez y solo una");
        resumenes.Sum(r => r.OmitidosPorCarrera).Should().Be(1, "la perdedora se cuenta como omitida");
        resumenes.Sum(r => r.Bloques).Should().Be(1, "un bloque y no dos");
        resumenes.Sum(r => r.IncrementosAplicados).Should().Be(1, "el incremento se aplicó una vez");

        (await BloquesAsync(contratoId, ct))
            .Should().ContainSingle("una sola ejecución llegó a escribir el bloque");
        (await FinVigenteAsync(contratoId, ct))
            .Should().Be(new DateOnly(2027, 1, 15), "15/01/2026 más 12 meses: el bloque avanza una vez, no dos");
        (await RentaAsync(contratoId, ct)).Should().Be(55_000m, "el incremento se aplica una vez");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking()
            .CountAsync(a => a.Clave == ServicioRenovaciones.ClaveIncremento(incrementoId), ct))
            .Should().Be(1, "la ejecución perdedora no dejó su aviso: iba en la transacción que se deshizo");
    }

    /// <summary>
    /// Un registro de alertas de verdad con una barrera delante. Sirve solo para este test: el aviso
    /// del incremento es el punto exacto en el que el trabajo ya ha leído y calculado pero todavía no
    /// ha escrito, así que parar ahí deja las dos ejecuciones con el mismo estado viejo y las suelta a
    /// la vez, sin depender del reloj del planificador. Y como escribe de verdad, el aviso de la
    /// ejecución perdedora se ve desaparecer con su transacción, que es justo lo que hay que
    /// comprobar.
    /// </summary>
    private sealed class AlertaConBarrera(ServicioAlertas dentro, Barrier barrera) : IRegistroAlertas
    {
        public async Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct)
        {
            barrera.SignalAndWait(ct);
            await dentro.RegistrarAsync(tipo, clave, mensaje, ct);
        }

        public Task ResolverAsync(string clave, CancellationToken ct) => dentro.ResolverAsync(clave, ct);
    }

    /// <summary>
    /// Un contrato cuyo bloque venció hace meses entra por <b>todos</b> los bloques que debería haber
    /// tenido, no solo por el siguiente, y en una sola transacción.
    /// <para>
    /// El porqué está escrito en el servicio, y es este: la renovación automática no depende de que
    /// nadie decidiera nada, así que los bloques perdidos no son bloques que alguien rechaza, son
    /// bloques que esta máquina no llegó a registrar. Saltárselos dejaría sin rastro un tramo que
    /// alguien vivió y se facturó, y un incremento con fecha dentro de uno de ellos no se aplicaría
    /// nunca, que es justo lo que §3.5 quiere cubrir. Además el bucle para en cuanto el bloque en
    /// curso cubre hoy, así que un contrato parado años no genera cientos de filas de golpe.
    /// </para>
    /// <para>
    /// Con un año de duración y el bloque vencido 26 meses atrás son tres bloques: −14, −2 y +10.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElContratoVencidoNoRenueva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        // Fechas fijas y reloj fijo, con los resultados escritos a mano: contrato del 31/03/2022 de
        // un año (fin inicial 31/03/2023) visto el 01/06/2025.
        var contratoId = await ContratoActivoAsync(empleado, gerente, new DateOnly(2022, 3, 31), 50_000m, ct);
        var hoy = new DateTimeOffset(2025, 6, 1, 13, 0, 0, TimeSpan.Zero);

        await EjecutarAsync(ct, en: hoy);

        var bloques = await BloquesAsync(contratoId, ct);
        bloques.Should().HaveCount(3, "el trabajo recupera los bloques que se dejó pasar");
        bloques.Select(b => b.Numero).Should().Equal([2, 3, 4], "numerados y sin huecos");
        bloques[0].FechaInicio.Should().Be(new DateOnly(2023, 3, 31));
        bloques[1].FechaInicio.Should().Be(new DateOnly(2024, 3, 31));
        bloques[2].FechaInicio.Should().Be(new DateOnly(2025, 3, 31));
        bloques[2].FechaFin.Should().Be(new DateOnly(2026, 3, 31));
        (await FinVigenteAsync(contratoId, ct)).Should().Be(new DateOnly(2026, 3, 31));

        // Y la siguiente ejecución, con el bloque en curso ya vigente, no añade ni uno más.
        await EjecutarAsync(ct, en: hoy);
        (await BloquesAsync(contratoId, ct)).Should().HaveCount(3);
    }

    /// <summary>
    /// BUG-043: encadenar <c>AddMonths</c> desde el fin vigente derivaba a fin de mes. Un contrato
    /// del 31/01/2024 de un año con renovación mensual tiene que ir 28/02, 31/03, 30/04, 31/05 (cada
    /// fin contado desde el inicio del contrato) y no 28/02, 28/03, 28/04.
    /// </summary>
    [Fact]
    public async Task LaRenovacionMensualNoDerivaAFinDeMes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        // Inicio 31/01/2024, 12 meses: fin inicial 31/01/2025.
        var contratoId = await ContratoActivoAsync(
            empleado, gerente, new DateOnly(2024, 1, 31), 50_000m, ct, renovacionMeses: 1, regla: "Mensual");

        await EjecutarAsync(ct, en: new DateTimeOffset(2025, 5, 15, 13, 0, 0, TimeSpan.Zero));

        var bloques = await BloquesAsync(contratoId, ct);
        bloques.Select(b => b.FechaFin).Should().Equal(
            [new DateOnly(2025, 2, 28), new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 30), new DateOnly(2025, 5, 31)]);
        bloques.Select(b => b.FechaInicio).Should().Equal(
            [new DateOnly(2025, 1, 31), new DateOnly(2025, 2, 28), new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 30)]);
        (await FinVigenteAsync(contratoId, ct)).Should().Be(new DateOnly(2025, 5, 31));
    }

    /// <summary>
    /// Lo que ve quien mira el contrato: los incrementos con su fecha y si se aplicaron, y los
    /// bloques del historial. Es la forma de responder "¿por qué esta renta?" sin leer la base.
    /// </summary>
    [Fact]
    public async Task LosIncrementosYLosBloquesSePuedenLeer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);
        await CrearIncrementoAsync(
            bd,
            contratoId,
            TipoIncremento.Porcentaje,
            porcentaje: 10m,
            importe: null,
            await FinVigenteAsync(contratoId, ct),
            ct);
        await EjecutarAsync(ct);

        var incremento = await AyudaReserva.Json(
            await empleado.GetAsync($"/api/v1/contratos/{contratoId:D}/incrementos", ct), ct);
        incremento.GetArrayLength().Should().Be(1);
        incremento[0].GetProperty("tipo").GetString().Should().Be("Porcentaje");
        incremento[0].GetProperty("porcentaje").GetDecimal().Should().Be(10m);
        incremento[0].GetProperty("importe").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        incremento[0].GetProperty("aplicado").GetBoolean().Should().BeTrue();

        var bloques = await AyudaReserva.Json(
            await gerente.GetAsync($"/api/v1/contratos/{contratoId:D}/bloques", ct), ct);
        bloques.GetArrayLength().Should().Be(1);
        bloques[0].GetProperty("renta").GetDecimal().Should().Be(55_000m);

        // Los tres roles leen: `contrato.borrador` está en el techo de los tres (matriz de §2).
        using var admin = await SesionAsync(Roles.Admin, ct);
        (await admin.GetAsync($"/api/v1/contratos/{contratoId:D}/incrementos", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/v1/contratos/{contratoId:D}/bloques", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------------------------
    // Atajos
    // -------------------------------------------------------------------------------------------

    /// <summary>Instante fijo del trabajo en las pruebas que afirman fechas literales: 01/03/2026.</summary>
    private static readonly DateTimeOffset RelojFijo = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);

    /// <summary>Hoy en Santo Domingo, <paramref name="meses"/> meses hacia atrás.</summary>
    private static DateOnly Vencido(int meses) => TiempoNegocio.Hoy().AddMonths(-meses);

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct) =>
        await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct), Contrasena, ct);

    /// <summary>
    /// Contrato ya activo con el bloque en curso vencido, que es la situación en la que el trabajo
    /// tiene algo que hacer.
    /// </summary>
    private async Task<Guid> ContratoActivoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        DateOnly inicio,
        decimal renta,
        CancellationToken ct,
        Guid? clienteId = null,
        Guid? unidadId = null,
        int renovacionMeses = 12,
        string regla = "Anual",
        string moneda = "DOP")
    {
        clienteId ??= await AyudaReserva.ClienteAsync(empleado, ct);
        unidadId ??= await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var id = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                clienteId,
                unidadId,
                facturadoraId,
                inicio,
                renta: renta,
                moneda: moneda,
                regla: regla,
                renovacionMeses: renovacionMeses),
            ct);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "activacion", null, ct);

        return id;
    }

    /// <summary>
    /// Incremento escrito directamente en la base, como la subempresa: todavía no hay endpoint que lo
    /// cree, así que la prueba lo deja como lo dejaría una carga de datos. Devuelve su identificador
    /// para poder mirar después la misma fila y no otra.
    /// </summary>
    private static async Task<Guid> CrearIncrementoAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        TipoIncremento tipo,
        decimal? porcentaje,
        decimal? importe,
        DateOnly fechaVigencia,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var incremento = new ContratoIncremento
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            ContratoId = contratoId,
            Tipo = tipo,
            Porcentaje = porcentaje,
            Importe = importe,
            FechaVigencia = fechaVigencia,
            Aplicado = false,
        };

        contexto.ContratoIncremento.Add(incremento);
        await contexto.SaveChangesAsync(ct);

        return incremento.Id;
    }

    /// <summary>
    /// Una pasada del trabajo con su propio contexto. <paramref name="avanzar"/> mueve el reloj para
    /// poder ver qué pasa meses después sin esperar meses.
    /// </summary>
    private async Task EjecutarAsync(CancellationToken ct, TimeSpan? avanzar = null, DateTimeOffset? en = null)
    {
        var reloj = new RelojPrueba(en);
        if (avanzar is { } delta)
        {
            reloj.Avanzar(delta);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await new ServicioRenovaciones(
                contexto, new AuditoriaService(contexto), new ServicioAlertas(contexto, reloj), reloj)
            .RenovarVencidosAsync(ct);
    }

    private Task<decimal> RentaAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.Renta).SingleAsync(ct));

    private Task<DateOnly> InicioAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.FechaInicio).SingleAsync(ct));

    private Task<DateOnly> FinInicialAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.FechaFinInicial).SingleAsync(ct));

    private Task<DateOnly> FinVigenteAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.FechaFinVigente).SingleAsync(ct));

    private Task<bool> AplicadoAsync(Guid incrementoId, CancellationToken ct) =>
        EnContextoAsync(c => c.ContratoIncremento.Where(i => i.Id == incrementoId).Select(i => i.Aplicado).SingleAsync(ct));

    private Task<List<ContratoBloque>> BloquesAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.ContratoBloque.AsNoTracking()
            .Where(b => b.ContratoId == contratoId).OrderBy(b => b.Numero).ToListAsync(ct));

    private Task<List<ContratoVersion>> VersionesAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.ContratoVersion.AsNoTracking()
            .Where(v => v.ContratoId == contratoId).OrderBy(v => v.Numero).ToListAsync(ct));

    private Task<OcupacionRango> CandadoAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.OcupacionRango.AsNoTracking()
            .SingleAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId, ct));

    private Task<int> CuentaCandadosAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.OcupacionRango.AsNoTracking()
            .CountAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId, ct));

    private async Task<T> EnContextoAsync<T>(Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}