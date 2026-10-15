using AtlasRent.Api.Dominio;
using Hangfire;
using Hangfire.Common;
using Npgsql;
using System.Reflection;

namespace AtlasRent.Api.Jobs;

/// <summary>Un trabajo programado: qué se ejecuta, con qué método y con qué cadencia.</summary>
/// <param name="Ruta">Identificador del trabajo recurrente. Estable y corto: es lo que se dispara a mano.</param>
/// <param name="Cron">Expresión de Hangfire. Siempre en UTC, por el motivo que explica <see cref="ProgramacionJobs"/>.</param>
/// <param name="Tipo">Clase que ejecuta el trabajo.</param>
/// <param name="Metodo">Método sin argumentos de esa clase.</param>
/// <param name="HoraLocal">Hora en <c>TZ_NEGOCIO</c> a la que se quiere que caiga, la que el cron de UTC traduce.</param>
public sealed record TrabajoProgramado(string Ruta, string Cron, Type Tipo, string Metodo, int HoraLocal);

/// <summary>
/// Los trabajos programados de F1-12 y sus cadencias (docs/DOC_TECNICA.md §8).
/// <para>
/// <strong>Por qué el cron está en UTC aunque la hora sea de Santo Domingo.</strong> La edición
/// gratuita de Hangfire (1.8.x, la última) planifica en UTC y no entiende el prefijo
/// <c>CRON_TZ=</c> de los crons con zona horaria: ese prefijo es de la edición de pago. Escribir
/// <c>0 2 * * *</c> sin más no significaría "las 02:00 de Santo Domingo" sino las 02:00 UTC, que
/// aquí son las 22:00 del día anterior: la copia saldría al final de la noche, no de madrugada, y
/// el fallo de esa copia sería el que de verdad duele.
/// <para>
/// La salida es convertir la hora de negocio al UTC que le corresponde
/// (<see cref="CronDiarioEnUtc"/>) y dejarlo escrito como cálculo, no como número mágico, para que
/// nadie tenga que saber que hay un desfase de cuatro horas escondido en un "6".
/// </para>
/// </summary>
public static class ProgramacionJobs
{
    /// <summary>
    /// Base de datos propia del planificador.
    /// <para>
    /// No es un capricho de aislamiento: las tablas de Hangfire <strong>no pueden vivir en
    /// <c>atlas_rent</c></strong>. El <c>pg_dump</c> del backup diario recorre la base entera y hace
    /// <c>LOCK TABLE</c> sobre todo lo que encuentra, así que un esquema que <c>rent_backup</c> no
    /// puede tocar hace que el volcado falle entero y el backup diario deje de funcionar. Por eso
    /// el planificador tiene su propia base, que ningún backup recorre.
    /// </para>
    /// </summary>
    public const string BaseDeDatos = "atlas_rent_jobs";

    /// <summary>
    /// Cadena del almacenamiento de jobs. Sale de <c>ConnectionStrings__Migraciones</c> (el rol
    /// <c>rent_migrador</c>, el único con permisos sobre esas tablas) con la base cambiada por
    /// <see cref="BaseDeDatos"/>: misma credencial, base distinta.
    /// </summary>
    public static string ConexionDeJobs(string cadenaMigrador) =>
        new NpgsqlConnectionStringBuilder(cadenaMigrador) { Database = BaseDeDatos }.ConnectionString;

    public const string RutaCopiaSeguridad = "copias-seguridad";
    public const string RutaIntegridadAuditoria = "integridad-auditoria";
    public const string RutaVigilanciaCopia = "vigilancia-copia";
    public const string RutaAlertaReserva = "alerta-reserva-7-dias";
    public const string RutaRenovacionContratos = "renovacion-contratos";

    /// <summary>
    /// Todo lo que hay programado ahora mismo. Los trabajos de §8 que dependen de dominio que aún no
    /// existe (tipos de cambio, recordatorios, envíos, outbox y limpieza de sesiones) NO se inventan
    /// aquí: se añaden cuando exista lo que tienen que hacer.
    /// <para>
    /// La vigilancia de la copia sí entra, aunque su nombre sea "alertas", porque no depende de un
    /// dominio pendiente: usa el servicio de copias y la tabla <c>alerta</c>, que ya existen. Lo que
    /// queda para F2-10 es avisar a alguien por correo y enseñarle una bandeja, no detectar el fallo.
    /// </para>
    /// <para>
    /// El aviso de las reservas (F2-03) entra ya por lo mismo: solo necesita la tabla <c>reserva</c>
    /// y el servicio de alertas, los dos existen. Y la renovación de contratos (F2-06) porque su
    /// dominio —<c>contrato</c>, <c>contrato_bloque</c>, <c>contrato_incremento</c>— ya está.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TrabajoProgramado> Todos { get; } =
    [
        new(
            RutaCopiaSeguridad,
            CronDiarioEnUtc(2),
            typeof(TrabajoCopiaSeguridad),
            nameof(TrabajoCopiaSeguridad.EjecutarAsync),
            2),
        new(
            RutaIntegridadAuditoria,
            CronDiarioEnUtc(3),
            typeof(TrabajoIntegridadAuditoria),
            nameof(TrabajoIntegridadAuditoria.EjecutarAsync),
            3),
        new(
            RutaVigilanciaCopia,
            CronDiarioEnUtc(4),
            typeof(TrabajoVigilanciaCopia),
            nameof(TrabajoVigilanciaCopia.EjecutarAsync),
            4),
        // F2-03: el aviso de reserva que entra dentro de 7 días. Va a las 05:00, después de la
        // copia (02:00), la integridad (03:00) y la vigilancia (04:00): si el servidor se apagó
        // esa madrugada, la copia falta y no tiene sentido avisar de reservas sobre un estado que
        // nadie ha verificado. Y es el último del día para que a las 05:00 no se solape con nada
        // que se añada después.
        new(
            RutaAlertaReserva,
            CronDiarioEnUtc(5),
            typeof(TrabajoAlertaReserva),
            nameof(TrabajoAlertaReserva.EjecutarAsync),
            5),
        // F2-06: la renovación automática por bloques y los incrementos de renta (§3.5). Va a las
        // 06:00, detrás de todos los demás, por dos motivos. Primero, porque escribe en
        // `contrato`, `contrato_bloque`, `contrato_incremento`, `contrato_version` y `alerta`: si
        // el servidor se apagó, la copia de las 02:00 falta y no tiene sentido mover la renta de
        // nadie sobre una base de la que nadie sabe cuál era su estado. Segundo, porque renovar
        // mueve datos que mañana factura F3-01: es el último trabajo del día para que cualquier
        // fallo suyo se vea en la mañana de F3-01 y no en medio de la operación del día.
        new(
            RutaRenovacionContratos,
            CronDiarioEnUtc(6),
            typeof(TrabajoRenovacionContratos),
            nameof(TrabajoRenovacionContratos.EjecutarAsync),
            6),
    ];

    /// <summary>
    /// Deja los trabajos registrados en el almacenamiento. Se llama una vez al arrancar, con el
    /// <see cref="IRecurringJobManager"/> del contenedor, y es idempotente: registrar dos veces lo
    /// mismo lo actualiza.
    /// </summary>
    public static void Registrar(IRecurringJobManager recurrentes)
    {
        foreach (var trabajo in Todos)
        {
            // El CancellationToken del final es un parámetro del método: Hangfire lo valida contra la firma
            // y lo sustituye por el suyo al ejecutar, que es lo que cancela el trabajo si se apaga
            // el proceso.
            recurrentes.AddOrUpdate(
                trabajo.Ruta,
                new Job(trabajo.Tipo, trabajo.Tipo.GetMethod(trabajo.Metodo)!, [CancellationToken.None]),
                trabajo.Cron,
                null!);
        }
    }

    /// <summary>
    /// Cron diario de Hangfire para una hora de la zona de negocio.
    /// <para>
    /// Rechaza una zona con horario de verano: un número fijo de hora UTC no puede querer decir
    /// "las 02:00 locales" los días de horario de verano, porque esas 02:00 son dos instantes distintos.
    /// Santo Domingo (UTC−4 todo el año) no tiene ese problema; fallar aquí es mejor que descubrir
    /// en un informe que la copia sale una hora de más seis meses al año.
    /// </para>
    /// </summary>
    public static string CronDiarioEnUtc(int horaLocal) => CronDiarioEnUtc(TiempoNegocio.Zona, horaLocal);

    private static string CronDiarioEnUtc(TimeZoneInfo zona, int horaLocal)
    {
        var enero = HoraUtcDe(zona, horaLocal, mes: 1);
        var julio = HoraUtcDe(zona, horaLocal, mes: 7);

        if (enero != julio)
        {
            throw new InvalidOperationException(
                $"La zona horaria de negocio ({zona.Id}) cambia de desfase con el horario de verano, y "
                + $"un cron fijo en UTC no puede expresar las {horaLocal:D2}:00 locales todos los días. "
                + "Registrar ese trabajo por otro mecanismo antes de cambiar la zona.");
        }

        // Solo la hora importa: el trabajo es diario y un cambio de día no lo mueve de día.
        return $"0 {enero} * * *";
    }

    /// <summary>Hora UTC a la que cae la hora local pedida, en un mes de referencia.</summary>
    private static int HoraUtcDe(TimeZoneInfo zona, int horaLocal, int mes)
    {
        var local = new DateTime(2026, mes, 15, horaLocal, 0, 0, DateTimeKind.Unspecified);
        var desfase = zona.GetUtcOffset(local);
        return new DateTimeOffset(local, desfase).UtcDateTime.Hour;
    }
}