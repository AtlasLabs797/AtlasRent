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
    /// Cadena del almacenamiento de jobs. Sale de <c>ConnectionStrings__Jobs</c> (el rol
    /// <c>rent_jobs</c>, dueño de esa base y sin acceso a <c>atlas_rent</c>) con la base fijada a
    /// <see cref="BaseDeDatos"/>, para que una cadena mal copiada no apunte a la base de la aplicación.
    /// </summary>
    public static string ConexionDeJobs(string cadenaJobs) =>
        new NpgsqlConnectionStringBuilder(cadenaJobs) { Database = BaseDeDatos }.ConnectionString;

    public const string RutaCopiaSeguridad = "copias-seguridad";
    public const string RutaIntegridadAuditoria = "integridad-auditoria";
    public const string RutaVigilanciaCopia = "vigilancia-copia";
    public const string RutaAlertaReserva = "alerta-reserva-7-dias";
    public const string RutaRenovacionContratos = "renovacion-contratos";
    public const string RutaFinContratos = "fin-contratos";
    public const string RutaAvisosOperacion = "avisos-operacion";
    public const string RutaTipoCambio = "tipo-cambio";
    public const string RutaComprobantes = "comprobantes-fiscales";
    public const string RutaRetenciones = "comprobantes-retencion";
    public const string RutaPeriodosSinFacturar = "periodos-sin-facturar";
    public const string RutaFianzas = "fianzas-sin-devolver";
    public const string RutaRecordatorios = "recordatorios-pago";

    /// <summary>BUG-037: barrido de envíos masivos huérfanos. No está en <see cref="Todos"/> (son diarios y con hora); este va cada 10 minutos.</summary>
    public const string RutaBarridoEnvios = "barrido-envios";
    public const string CronBarridoEnvios = "*/10 * * * *";

    /// <summary>
    /// Todo lo que hay programado ahora mismo. Los trabajos de §8 que dependen de dominio que aún no
    /// existe (recordatorios, envíos, outbox y limpieza de sesiones) NO se inventan
    /// aquí: se añaden cuando exista lo que tienen que hacer. El tipo de cambio ya entra (F3-01):
    /// era el primero de esos, y el único cuya hora quedó por delante de un trabajo anterior.
    /// <para>
    /// La vigilancia de la copia sí entra, aunque su nombre sea "alertas", porque no depende de un
    /// dominio pendiente: usa el servicio de copias y la tabla <c>alerta</c>, que ya existen. Lo que
    /// queda para F2-10 es avisar a alguien por correo y enseñarle una bandeja, no detectar el fallo.
    /// </para>
    /// <para>
    /// F2-10 entra aquí solo con los avisos de §3.16 que se pueden construir con las tablas que ya
    /// existen. Los demás no se inventan: sin la tabla de la que salen, una alerta de "factura
    /// vencida" no se puede escribir sin inventar también el modelo de la facturación entera, y
    /// <c>ServicioAvisosOperacion</c> deja un <c>TODO(A#)</c> en el sitio donde iría cada uno
    /// nombrando la tabla que falta.
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
        // F2-09: el fin por fecha efectiva y la alerta persistente de lo que no está liquidado. A las
        // 07:00, detrás de todos los demás, y por el motivo que puso la renovación en su sitio: este
        // trabajo escribe en `contrato`, en `ocupacion_rango` y en `alerta`, así que si el servidor se
        // apagó, la copia de las 02:00 falta y no tiene sentido liberar unidades sobre un estado que
        // nadie sabe cuál era. Además, liberar una unidad antes de tiempo deja entrar a un inquilino
        // que luego resulta que ya tenía contrato, y eso no es un aviso: es un problema.
        new(
            RutaFinContratos,
            CronDiarioEnUtc(7),
            typeof(TrabajoFinContratos),
            nameof(TrabajoFinContratos.EjecutarAsync),
            7),
        // F2-10: los avisos de §3.16 que no dependen de las tablas de F3 (baja programada próxima,
        // unidad que se libera en 30 días, reserva en conflicto e IP bloqueada). A las 08:00, la
        // primera hora libre detrás de los seis trabajos que ya había, y por el mismo motivo que el
        // fin: escribe en `alerta` y solo tiene sentido sobre un estado que la copia de la madrugada y
        // el trabajo de fin ya han pasado revisando. No depende funcionalmente de ninguno de ellos.
        new(
            RutaAvisosOperacion,
            CronDiarioEnUtc(8),
            typeof(TrabajoAvisosOperacion),
            nameof(TrabajoAvisosOperacion.EjecutarAsync),
            8),
        // F3-01: el tipo de cambio del día. Va a las 05:00, y es el único trabajo que no se pone
        // detrás de todo lo demás: los de F2 escriben en el estado del negocio y quieren la copia de
        // las 02:00 por delante, pero aquí lo que se guarda es un valor de referencia que la factura
        // y el cobro congelan al emitirse, así que lo que importa es que esté antes de que empiece
        // el día y no después. Se solapa con el aviso de reservas de F2-03 a la misma hora: los dos
        // son cortos, ninguno espera al otro y ninguno escribe en la misma fila.
        new(
            RutaTipoCambio,
            CronDiarioEnUtc(5),
            typeof(TrabajoTipoCambio),
            nameof(TrabajoTipoCambio.EjecutarAsync),
            5),
        // F3-03: los avisos del numerador de comprobante fiscal (§3.7): por números restantes, por
        // vencimiento y por agotado. A las 06:00, la primera hora libre que cumple las dos
        // condiciones que la §8 exige a un trabajo que escribe en `alerta`: después de la copia de
        // las 02:00, de la integridad y de la vigilancia —si el servidor se apagó, la copia falta y
        // avisar sobre un estado que nadie ha verificado sería avisar de algo que puede que ya no sea
        // cierto—, y antes del fin y de los avisos de operación, que siguen siendo los últimos por
        // lo mismo que él. Comparte hora con la renovación de contratos: los dos son cortos e
        // independientes, y 06:00 deja más margen del día por delante que las 08:00 para lo que
        // este aviso pide, que es pedir un rango nuevo al organismo que autoriza los comprobantes.
        new(
            RutaComprobantes,
            CronDiarioEnUtc(6),
            typeof(TrabajoComprobantes),
            nameof(TrabajoComprobantes.EjecutarAsync),
            6),
        // F3-04: el aviso de las facturas que retienen y que no han recibido su comprobante (§3.9).
        // A las 06:00, la misma hora que los otros dos avisos de comprobante y por el mismo motivo
        // (§8): escribe en `alerta`, así que quiere la copia de la madrugada por delante, y antes del
        // fin y de los avisos de operación, que siguen siendo los últimos. Comparte hora con la
        // renovación y con el numerador fiscal porque los tres son cortos e independientes y ninguno
        // espera al otro. Podría ir a las 07:00 igual que el fin de contratos, pero antes es mejor:
        // el papel que hay que pedir al cliente depende de él, y a las 06:00 ya está pedido cuando
        // empieza el día.
        new(
            RutaRetenciones,
            CronDiarioEnUtc(6),
            typeof(TrabajoRetenciones),
            nameof(TrabajoRetenciones.EjecutarAsync),
            6),
        // F3-05: el aviso de los contratos cuyo mes natural ya pasó sin factura (§3.16). A las 06:00,
        // la misma hora que los otros avisos de comprobante y por el mismo motivo (§8): escribe en
        // `alerta`, así que quiere la copia de la madrugada por delante, y antes del fin y de los
        // avisos de operación, que siguen siendo los últimos. Comparte hora con los tres anteriores
        // porque los cuatro son cortos e independientes: solo leen contratos y facturas y escriben en
        // la tabla de alertas con claves distintas. No factura nada —facturar es una acción de
        // gerente o administrador (D15)—, así que puede ir antes de que empiece el día.
        new(
            RutaPeriodosSinFacturar,
            CronDiarioEnUtc(6),
            typeof(TrabajoPeriodosSinFacturar),
            nameof(TrabajoPeriodosSinFacturar.EjecutarAsync),
            6),
        // F3-09: el aviso de las fianzas cuyo contrato terminó y que siguen sin devolver ni liquidar
        // (§3.11 y §3.16). A las 06:00, la misma hora que los otros tres avisos y por el mismo motivo
        // (§8): escribe en `alerta`, así que quiere la copia de la madrugada por delante, y antes del
        // fin y de los avisos de operación, que siguen siendo los últimos. Comparte hora porque los
        // cinco son cortos e independientes: solo leen fianzas, contratos y asignaciones y escriben en
        // la tabla de alertas con claves distintas.
        new(
            RutaFianzas,
            CronDiarioEnUtc(6),
            typeof(TrabajoFianzas),
            nameof(TrabajoFianzas.EjecutarAsync),
            6),
        // F4-05: los avisos de pago de §3.14 (D12), el primero a los 5 días del vencimiento y luego
        // cada 15 mientras haya saldo. A las 09:00, la hora que fija docs/DOC_TECNICA.md §8, que es la
        // última del día a propósito: un aviso que sale cuando la casa ya cerró se lee por la mañana
        // con la cabeza del día entero, que es cuando se paga. Además es el único trabajo que compone
        // un PDF por cliente, así que quiere ir después de los avisos que no tocan la red.
        new(
            RutaRecordatorios,
            CronDiarioEnUtc(9),
            typeof(TrabajoRecordatorios),
            nameof(TrabajoRecordatorios.EjecutarAsync),
            9),
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

        recurrentes.AddOrUpdate(
            RutaBarridoEnvios,
            new Job(
                typeof(TrabajoBarridoEnvios),
                typeof(TrabajoBarridoEnvios).GetMethod(nameof(TrabajoBarridoEnvios.EjecutarAsync))!,
                [CancellationToken.None]),
            CronBarridoEnvios,
            null!);
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