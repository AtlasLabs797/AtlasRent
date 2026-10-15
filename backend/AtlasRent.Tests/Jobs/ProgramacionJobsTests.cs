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
    public void LosTrabajosDeLaSeccion8QueTodaviaNoExisteNoSePrograman()
    {
        // Los trabajos de §8 que dependen de dominio inexistente (tipos de cambio, renovaciones,
        // alertas, recordatorios, envíos, outbox, limpieza de sesiones) no se programan: sin lo que
        // tienen que hacer, programarlos solo produce errores en el registro todas las noches.
        ProgramacionJobs.Todos.Select(t => t.Tipo).Should().BeEquivalentTo(
            [typeof(TrabajoCopiaSeguridad), typeof(TrabajoIntegridadAuditoria)]);
    }

    [Fact]
    public void RegistrarDejaCadaTrabajoConSuCronYSuMetodo()
    {
        var recurrentes = new RecurrentesFalsos();

        ProgramacionJobs.Registrar(recurrentes);

        recurrentes.Registrados.Select(r => r.Ruta).Should().BeEquivalentTo(
            [ProgramacionJobs.RutaCopiaSeguridad, ProgramacionJobs.RutaIntegridadAuditoria]);

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