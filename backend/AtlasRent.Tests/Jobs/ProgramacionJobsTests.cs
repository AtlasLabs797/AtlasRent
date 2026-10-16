using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AwesomeAssertions;
using Hangfire;
using Hangfire.Common;
using Xunit;

namespace AtlasRent.Tests.Jobs;

/// <summary>
/// F1-12: la cadencia con la que queda programado el backup diario y el trabajo de integridad.
/// <para>
/// Son tests de configuración: comprueban el <c>RecurringJob</c> que se registra y no que el backup
/// corra. La zona horaria es la parte importante, porque registrar "las 02:00" sin decir en qué zona
/// es el error clásico: en Hangfire 1.8 (la edición gratuita) un cron es UTC, y las 02:00 UTC en
/// Santo Domingo son las 22:00 del día anterior.
/// </para>
/// <para>
/// Y el orden importa igual que la hora: cada trabajo de F2 entra detrás del anterior, y el test de
/// cada uno comprueba que su hora es mayor que la de todos los demás.
/// </para>
/// </summary>
public sealed class ProgramacionJobsTests
{
    private static readonly TimeZoneInfo Zona = TiempoNegocio.Zona;

    /// <summary>Un día cualquiera a medianoche UTC, para calcular el disparo siguiente.</summary>
    private static readonly DateTime Desde = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    private static TrabajoProgramado Trabajo(string ruta) =>
        ProgramacionJobs.Todos.Should().ContainSingle(t => t.Ruta == ruta).Subject;

    /// <summary>
    /// A qué hora y minuto dispara ese cron, en UTC. Los crons de F1-12 son diarios
    /// (<c>minuto hora * * *</c>), así que con el primer par de campos basta; y se comprueba que
    /// sean efectivamente diarios, que es lo que no puede fallar sin querer.
    /// </summary>
    private static DateTime PrimerDisparo(string cron)
    {
        var campos = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        campos.Should().HaveCount(5, $"un cron de Hangfire tiene cinco campos y este es '{cron}'");
        campos[2..].Should().Equal(["*", "*", "*"], "el trabajo es diario");
        int.TryParse(campos[0], out var minuto).Should().BeTrue();
        int.TryParse(campos[1], out var hora).Should().BeTrue();

        return new DateTime(Desde.Year, Desde.Month, Desde.Day, hora, minuto, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void ElBackupDiarioEstaProgramadoALas02DeSantoDomingo()
    {
        var backup = Trabajo(ProgramacionJobs.RutaCopiaSeguridad);

        backup.HoraLocal.Should().Be(2, "son las 02:00 de Santo Domingo, no de UTC");
        backup.Tipo.Should().Be(typeof(TrabajoCopiaSeguridad));
        backup.Metodo.Should().Be(nameof(TrabajoCopiaSeguridad.EjecutarAsync));

        // Lo que decide es a qué hora local cae el disparo, no el número del cron: el cron va en
        // UTC porque Hangfire 1.8 no entiende el prefijo CRON_TZ= de los crons con zona.
        TimeZoneInfo.ConvertTime(PrimerDisparo(backup.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 2, 0, 0),
                "el trabajo tiene que caer a las 02:00 de Santo Domingo");
    }

    /// <summary>
    /// Regresión explícita de la trampa de la zona horaria. Si alguien "simplifica" el cron a las
    /// 02:00 en UTC, la copia sale a las 22:00 de la noche anterior y este test lo dice. El fallo es
    /// silencioso: la copia se hace, el trabajo sale verde, y lo que se rompe es la copia de la
    /// madrugada anterior si esa máquina se apaga por la tarde.
    /// </summary>
    [Fact]
    public void ElBackupDiarioNoEstaProgramadoALas02EnUtc()
    {
        var backup = Trabajo(ProgramacionJobs.RutaCopiaSeguridad);

        backup.Cron.Should().NotBe("0 2 * * *", "las 02:00 en UTC son las 22:00 en Santo Domingo");
        backup.Cron.Should().NotContain("CRON_TZ",
            "Hangfire 1.8 no entiende el prefijo CRON_TZ=; el cron se usaría tal cual");

        var disparo = PrimerDisparo(backup.Cron);
        disparo.Hour.Should().Be(6, "las 02:00 de Santo Domingo (UTC−4) son las 06:00 UTC");
    }

    [Fact]
    public void LaIntegridadDeAuditoriaVaCadaDiaDespuesDelBackup()
    {
        var integridad = Trabajo(ProgramacionJobs.RutaIntegridadAuditoria);

        integridad.HoraLocal.Should().Be(3, "después de la copia, para verificar el estado en que quedó");
        integridad.Tipo.Should().Be(typeof(TrabajoIntegridadAuditoria));

        TimeZoneInfo.ConvertTime(PrimerDisparo(integridad.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 3, 0, 0));
    }

    [Fact]
    public void LaVigilanciaDeLaCopiaVaDespuesDeHacerla()
    {
        var vigilancia = Trabajo(ProgramacionJobs.RutaVigilanciaCopia);

        vigilancia.HoraLocal.Should().Be(4, "después de la copia (02:00) y de la integridad (03:00)");
        vigilancia.Tipo.Should().Be(typeof(TrabajoVigilanciaCopia));

        TimeZoneInfo.ConvertTime(PrimerDisparo(vigilancia.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 4, 0, 0));

        // Si se adelantara a antes de la copia, vigilaría una copia que todavía no ha tenido tiempo
        // de hacerse y avisaría de un fallo que no existe.
        vigilancia.HoraLocal.Should().BeGreaterThan(Trabajo(ProgramacionJobs.RutaCopiaSeguridad).HoraLocal);
    }

    /// <summary>
    /// F1-12: la lista de <c>ProgramacionJobs.Todos</c> es exactamente lo que hay escrito.
    /// <para>
    /// Los trabajos de §8 que dependen de dominio inexistente (recordatorios, envíos, outbox,
    /// limpieza de sesiones) no se programan: sin lo que tienen que hacer, programarlos solo produce
    /// errores en el registro cada noche. El tipo de cambio ya no está en esa lista desde F3-01: su
    /// dominio existe y es lo que programa.
    /// </para>
    /// </summary>
    [Fact]
    public void LosTrabajosDeLaSeccion8QueTodaviaNoExisteNoSePrograman()
    {
        ProgramacionJobs.Todos.Select(t => t.Tipo).Should().BeEquivalentTo(
            [
                typeof(TrabajoCopiaSeguridad),
                typeof(TrabajoIntegridadAuditoria),
                typeof(TrabajoVigilanciaCopia),
                typeof(TrabajoAlertaReserva),
                typeof(TrabajoRenovacionContratos),
                typeof(TrabajoFinContratos),
                typeof(TrabajoAvisosOperacion),
                typeof(TrabajoTipoCambio),
                typeof(TrabajoComprobantes),
                typeof(TrabajoRetenciones),
                typeof(TrabajoPeriodosSinFacturar),
            ]);
    }

    /// <summary>
    /// F3-03: los avisos del numerador de comprobante van a las 06:00.
    /// <para>
    /// Escribe en <c>alerta</c>, así que cumple la misma regla que el resto de los trabajos que
    /// escriben ahí (fin por fecha efectiva, avisos de operación): ir <b>detrás</b> de la copia de las
    /// 02:00, la integridad y la vigilancia, porque si el servidor se apagó esa madrugada la copia
    /// falta y avisar sobre un estado que nadie ha verificado sería avisar de algo que puede que ya
    /// no sea cierto. Y por delante del fin y de los avisos de operación, que son los últimos.
    /// </para>
    /// <para>
    /// Comparte hora con la renovación de contratos a propósito, igual que hace el tipo de cambio
    /// con el aviso de reservas: son cortos e independientes, y a las 06:00 todavía queda la
    /// mañana por delante para lo que este aviso pide, que es pedir un rango nuevo al organismo que
    /// autoriza los comprobantes.
    /// </para>
    /// </summary>
    [Fact]
    public void LosAvisosDelNumeradorVanALas06DetrasDeLaVigilanciaYDelanteDelFin()
    {
        var numerador = Trabajo(ProgramacionJobs.RutaComprobantes);

        numerador.HoraLocal.Should().Be(6);
        numerador.Tipo.Should().Be(typeof(TrabajoComprobantes));
        numerador.Metodo.Should().Be(nameof(TrabajoComprobantes.EjecutarAsync));

        // Las 06:00 de Santo Domingo (UTC−4) son las 10:00 UTC.
        TimeZoneInfo.ConvertTime(PrimerDisparo(numerador.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 6, 0, 0));

        numerador.HoraLocal.Should().BeGreaterThan(
            Trabajo(ProgramacionJobs.RutaVigilanciaCopia).HoraLocal,
            "escribe en alerta, así que va detrás de la copia, la integridad y la vigilancia");
        numerador.HoraLocal.Should().BeLessThan(
            Trabajo(ProgramacionJobs.RutaFinContratos).HoraLocal,
            "el fin y los avisos de operación siguen siendo los últimos de la madrugada");
        numerador.HoraLocal.Should().Be(
            Trabajo(ProgramacionJobs.RutaRenovacionContratos).HoraLocal,
            "comparte hora con la renovación: los dos son cortos e independientes");
    }

    /// <summary>
    /// F3-04: los avisos de comprobante de retención van a las 06:00, por las mismas dos reglas que
    /// los del numerador fiscal (§8): escriben en <c>alerta</c>, así que van detrás de la copia, la
    /// integridad y la vigilancia, y por delante del fin y de los avisos de operación, que son los
    /// últimos. Compartir hora con el numerador y con la renovación es lo mismo: tres trabajos cortos
    /// e independientes.
    /// </summary>
    [Fact]
    public void LosAvisosDeRetencionVanALas06DetrasDeLaVigilanciaYDelanteDelFin()
    {
        var retenciones = Trabajo(ProgramacionJobs.RutaRetenciones);

        retenciones.HoraLocal.Should().Be(6);
        retenciones.Tipo.Should().Be(typeof(TrabajoRetenciones));
        retenciones.Metodo.Should().Be(nameof(TrabajoRetenciones.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(retenciones.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 6, 0, 0));

        retenciones.HoraLocal.Should().BeGreaterThan(
            Trabajo(ProgramacionJobs.RutaVigilanciaCopia).HoraLocal,
            "escribe en alerta, así que va detrás de la copia, la integridad y la vigilancia");
        retenciones.HoraLocal.Should().BeLessThan(
            Trabajo(ProgramacionJobs.RutaFinContratos).HoraLocal,
            "el fin y los avisos de operación siguen siendo los últimos de la madrugada");
    }

    /// <summary>
    /// F3-01: el tipo de cambio va a las 05:00 de Santo Domingo, y es el único trabajo que se pone
    /// <b>delante</b> de otro.
    /// <para>
    /// Los trabajos de F2 van todos detrás de todo lo demás por el mismo motivo: escriben en el
    /// estado del negocio, así que si el servidor se apagó la copia de las 02:00 falta y no hay nada
    /// verificado sobre lo que actuar. El tipo de cambio rompe esa regla a propósito: lo que guarda
    /// es un valor de referencia que la factura y el cobro congelan al emitirse, así que importa
    /// que esté antes de que empiece el día, no después. Por eso cae a las 05:00 y no a las 09:00,
    /// que es lo que le tocaría por el orden de las demás.
    /// </para>
    /// <para>
    /// Comparte hora con el aviso de reservas de F2-03, y eso se comprueba: si alguien mueve una de
    /// las dos horas sin querer, el solapamiento dejaría de ser un dato conocido del código para
    /// pasar a ser una sorpresa.
    /// </para>
    /// </summary>
    [Fact]
    public void ElTipoCambioVaALas05ParaTenerloAntesDeLaPrimeraFactura()
    {
        var tipoCambio = Trabajo(ProgramacionJobs.RutaTipoCambio);

        tipoCambio.HoraLocal.Should().Be(5, "a las 05:00 de Santo Domingo ya ha empezado el día");
        tipoCambio.Tipo.Should().Be(typeof(TrabajoTipoCambio));
        tipoCambio.Metodo.Should().Be(nameof(TrabajoTipoCambio.EjecutarAsync));

        // Las 05:00 de Santo Domingo (UTC−4) son las 09:00 UTC.
        TimeZoneInfo.ConvertTime(PrimerDisparo(tipoCambio.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 5, 0, 0));

        tipoCambio.HoraLocal.Should().Be(Trabajo(ProgramacionJobs.RutaAlertaReserva).HoraLocal,
            "comparte hora con el aviso de reservas a propósito: ambos son cortos e independientes");

        tipoCambio.HoraLocal.Should().BeLessThan(Trabajo(ProgramacionJobs.RutaRenovacionContratos).HoraLocal,
            "y va por delante de los trabajos de F2, que esperan a la copia de la madrugada");
    }

    /// <summary>
    /// F2-10: los avisos de operación van a las 08:00, la primera hora libre detrás de los seis
    /// trabajos que ya había, y no se solapan con ninguno.
    /// <para>
    /// Van al final por el mismo motivo que el fin por fecha efectiva: escriben en <c>alerta</c>
    /// después de que la copia de las 02:00 y el trabajo de fin ya han pasado. No dependen
    /// funcionalmente de ellos —si la copia falla, el aviso sigue haciendo falta igual—, pero avisar
    /// sobre un estado que nadie ha verificado es avisar de algo que puede que ya no sea cierto.
    /// </para>
    /// </summary>
    [Fact]
    public void LosAvisosDeOperacionVanLaUltimaDeLaMadrugada()
    {
        var avisos = Trabajo(ProgramacionJobs.RutaAvisosOperacion);

        avisos.HoraLocal.Should().Be(8, "detrás de los seis trabajos que ya había");
        avisos.Tipo.Should().Be(typeof(TrabajoAvisosOperacion));
        avisos.Metodo.Should().Be(nameof(TrabajoAvisosOperacion.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(avisos.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 8, 0, 0), "las 08:00 de Santo Domingo son las 12:00 UTC");

        foreach (var otra in ProgramacionJobs.Todos.Where(t => t.Ruta != ProgramacionJobs.RutaAvisosOperacion))
        {
            avisos.HoraLocal.Should().BeGreaterThan(otra.HoraLocal, $"el aviso va detrás de {otra.Ruta}");
        }
    }

    /// <summary>
    /// F2-03: el aviso de la reserva que entra dentro de 7 días va después de todo lo demás de la
    /// madrugada. Si el servidor se apagó, la copia falta y no tiene sentido avisar de reservas
    /// sobre un estado que nadie ha verificado.
    /// </summary>
    [Fact]
    public void LaAlertaDeReservaVaLaUltimaDeLaMadrugada()
    {
        var alerta = Trabajo(ProgramacionJobs.RutaAlertaReserva);

        alerta.HoraLocal.Should().Be(5, "después de la copia, la integridad y la vigilancia");
        alerta.Tipo.Should().Be(typeof(TrabajoAlertaReserva));
        alerta.Metodo.Should().Be(nameof(TrabajoAlertaReserva.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(alerta.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 5, 0, 0));

        alerta.HoraLocal.Should().BeGreaterThan(Trabajo(ProgramacionJobs.RutaVigilanciaCopia).HoraLocal);
    }

    /// <summary>
    /// <para>
    /// F2-06: la renovación va a las 06:00. Es la que mueve la renta que factura el día siguiente,
    /// así que su fallo se tiene que ver en la mañana siguiente y no en mitad de la operación; y no
    /// solapa con ningún trabajo anterior de la madrugada. Ya no es la última: desde F2-09 el fin por
    /// fecha efectiva va detrás, a las 07:00, y desde F2-10 los avisos de operación, a las 08:00.
    /// </para>
    /// </summary>
    [Fact]
    public void LaRenovacionVaDetrasDelTrabajoDeMadrugadaAnterior()
    {
        var renovacion = Trabajo(ProgramacionJobs.RutaRenovacionContratos);

        renovacion.HoraLocal.Should().Be(6);
        renovacion.Tipo.Should().Be(typeof(TrabajoRenovacionContratos));
        renovacion.Metodo.Should().Be(nameof(TrabajoRenovacionContratos.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(renovacion.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 6, 0, 0));

        renovacion.HoraLocal.Should().BeGreaterThan(Trabajo(ProgramacionJobs.RutaVigilanciaCopia).HoraLocal);
        renovacion.HoraLocal.Should().BeGreaterThan(Trabajo(ProgramacionJobs.RutaAlertaReserva).HoraLocal);
    }

    /// <summary>
    /// F2-09: el fin por fecha efectiva va a las 07:00, detrás de todo lo demás, y no solo por
    /// simetría.
    /// <para>
    /// Primero, escribe en <c>contrato</c>, en <c>ocupacion_rango</c> y en <c>alerta</c>: si el
    /// servidor se apagó, la copia de las 02:00 falta y no tiene sentido soltar unidades sobre un
    /// estado que nadie sabe cuál era. Y segundo, y más caro: liberar una unidad antes de tiempo deja
    /// entrar a un inquilino que resulta que ya tenía contrato. Eso no es un aviso, es un problema.
    /// </para>
    /// <para>
    /// Ya no es el último: desde F2-10 los avisos de operación van detrás, a las 08:00. El test
    /// compara al fin con los trabajos anteriores a él, que es lo que este orden significa, y el
    /// trabajo nuevo comprueba por su cuenta que va detrás del fin.
    /// </para>
    /// </summary>
    [Fact]
    public void ElFinVaDetrasDelTrabajoDeMadrugadaAnterior()
    {
        var fin = Trabajo(ProgramacionJobs.RutaFinContratos);

        fin.HoraLocal.Should().Be(7);
        fin.Tipo.Should().Be(typeof(TrabajoFinContratos));
        fin.Metodo.Should().Be(nameof(TrabajoFinContratos.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(fin.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 7, 0, 0), "las 07:00 de Santo Domingo son las 11:00 UTC");

        var anteriores = ProgramacionJobs.Todos
            .Where(t => t.Ruta != ProgramacionJobs.RutaFinContratos
                && t.Ruta != ProgramacionJobs.RutaAvisosOperacion)
            .ToList();

        anteriores.Should().NotBeEmpty("el fin va detrás de los trabajos de F1-12 y de F2-03/F2-06");
        foreach (var otra in anteriores)
        {
            fin.HoraLocal.Should().BeGreaterThan(otra.HoraLocal, $"el fin va detrás de {otra.Ruta}");
        }
    }

    /// <summary>
    /// F3-05: el aviso de «periodos sin facturar» va a las 06:00, la hora de los otros avisos que
    /// escriben en <c>alerta</c>.
    /// <para>
    /// Detrás de la copia de las 02:00, la integridad y la vigilancia —si el servidor se apagó, la
    /// copia falta y avisar sobre un estado que nadie ha verificado sería avisar de algo que puede
    /// que ya no sea cierto— y por delante del fin y de los avisos de operación, que son los últimos
    /// por el mismo motivo. Comparte hora con la renovación, el numerador fiscal y las retenciones:
    /// los cinco son cortos e independientes, y ninguno espera al otro.
    /// </para>
    /// <para>
    /// Que no se solape con el aviso de operaciones a las 08:00 es lo que además garantiza
    /// <see cref="LosAvisosDeOperacionVanLaUltimaDeLaMadrugada"/>, que exige que ese trabajo sea el
    /// último de la madrugada.
    /// </para>
    /// </summary>
    [Fact]
    public void ElAvisoDePeriodosSinFacturarVaDetrasDeLaCopiaYAntesDelFin()
    {
        var aviso = Trabajo(ProgramacionJobs.RutaPeriodosSinFacturar);

        aviso.HoraLocal.Should().Be(6);
        aviso.Tipo.Should().Be(typeof(TrabajoPeriodosSinFacturar));
        aviso.Metodo.Should().Be(nameof(TrabajoPeriodosSinFacturar.EjecutarAsync));

        TimeZoneInfo.ConvertTime(PrimerDisparo(aviso.Cron), Zona)
            .Should().Be(new DateTime(2026, 10, 20, 6, 0, 0));

        foreach (var otra in ProgramacionJobs.Todos.Where(t =>
            t.Ruta is ProgramacionJobs.RutaCopiaSeguridad
                or ProgramacionJobs.RutaIntegridadAuditoria
                or ProgramacionJobs.RutaVigilanciaCopia))
        {
            aviso.HoraLocal.Should().BeGreaterThan(otra.HoraLocal, $"el aviso va detrás de {otra.Ruta}");
        }

        foreach (var otra in ProgramacionJobs.Todos.Where(t =>
            t.Ruta is ProgramacionJobs.RutaFinContratos or ProgramacionJobs.RutaAvisosOperacion))
        {
            aviso.HoraLocal.Should().BeLessThan(otra.HoraLocal, $"el aviso va antes de {otra.Ruta}");
        }
    }

    [Fact]
    public void RegistrarDejaCadaTrabajoConSuCronYSuMetodo()
    {
        var recurrentes = new RecurrentesFalsos();

        ProgramacionJobs.Registrar(recurrentes);

        recurrentes.Registrados.Select(r => r.Ruta).Should().BeEquivalentTo(
            [
                ProgramacionJobs.RutaCopiaSeguridad,
                ProgramacionJobs.RutaIntegridadAuditoria,
                ProgramacionJobs.RutaVigilanciaCopia,
                ProgramacionJobs.RutaAlertaReserva,
                ProgramacionJobs.RutaRenovacionContratos,
                ProgramacionJobs.RutaFinContratos,
                ProgramacionJobs.RutaAvisosOperacion,
                ProgramacionJobs.RutaTipoCambio,
                ProgramacionJobs.RutaComprobantes,
                ProgramacionJobs.RutaRetenciones,
                ProgramacionJobs.RutaPeriodosSinFacturar,
            ]);

        var backup = recurrentes.Registrados.Single(r => r.Ruta == ProgramacionJobs.RutaCopiaSeguridad);
        backup.Cron.Should().Be(ProgramacionJobs.CronDiarioEnUtc(2));
        backup.Trabajo.Type.Should().Be(typeof(TrabajoCopiaSeguridad));
        backup.Trabajo.Method!.Name.Should().Be(nameof(TrabajoCopiaSeguridad.EjecutarAsync));
    }

    /// <summary>
    /// Programador de mentira: solo anota lo que se le pide registrar. Los trabajos de F1-12 no se
    /// ejecutan en esta clase, y menos contra una base de datos de verdad.
    /// </summary>
    private sealed class RecurrentesFalsos : IRecurringJobManager
    {
        public List<(string Ruta, Job Trabajo, string Cron)> Registrados { get; } = [];

        public void AddOrUpdate(
            string recurringJobId,
            Job job,
            string cronExpression,
            RecurringJobOptions options)
            => Registrados.Add((recurringJobId, job, cronExpression));

        public void Trigger(string recurringJobId) => throw new NotSupportedException();

        public void RemoveIfExists(string recurringJobId) => throw new NotSupportedException();
    }
}