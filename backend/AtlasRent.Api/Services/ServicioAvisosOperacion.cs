using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Los avisos de §3.16 que se pueden construir con lo que ya existe en la base de datos
/// (F2-10).
/// <para>
/// Es un servicio y no solo un trabajo por la misma razón que la renovación (F2-06) y el fin
/// (F2-09): el trabajo solo lo dispara, para que las pruebas puedan ejercitar los avisos sin pasar por
/// el planificador.
/// </para>
/// <para>
/// <b>Alertas sin duplicar, que es el criterio de aceptación de F2-10.</b> Las cuatro fuentes que
/// ya había (copia, reserva a 7 días, incremento aplicado, firmeza pendiente) deduplican por
/// <c>tipo-entidad:id</c> contra el índice único de <c>alerta.clave</c>, así que correr el trabajo
/// dos veces el mismo día no puede dejar dos filas. Estas cuatro hacen lo mismo con las suyas:
/// <list type="bullet">
/// <item><c>contrato-baja-programada:{id}</c></item>
/// <item><c>contrato-liberacion-30:{id}</c></item>
/// <item><c>reserva-conflicto:{id}</c></item>
/// <item><c>ip-bloqueada:{id}</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Ventanas, no igualdades (BUG-028).</b> Cada barrido busca lo que cae en [hoy, hoy + N]: un aviso
/// nace el primer día que la fecha entra en la ventana (aunque sea el mismo día de darla) y sigue vivo
/// hasta que la fecha pasa o la causa desaparece; un día sin que el trabajo corra no pierde ninguno.
/// </para>
/// <para>
/// <b>Ninguna lleva fecha en la clave, y esa es la decisión.</b> El aviso es una vez y para siempre
/// por entidad: una baja programada es un hecho, no un estado que se reavise cada día, y treinta filas
/// de "este contrato se libera pronto" son ruido que nadie mira. Además, con la fecha en la clave el
/// criterio de aceptación se cumpliría solo la primera vez —a partir del segundo día saldría otra
/// alerta distinta— y "repetir el trabajo dos veces el mismo día no deja dos alertas" dejaría de ser
/// cierto. Sin fecha, la condición de fecha del barrido <i>y</i> la clave dan la misma garantía:
/// correrlo dos veces el mismo día refresca la misma fila, como hace la de firmeza pendiente (F2-09).
/// </para>
/// <para>
/// <b>Y el aviso se cierra cuando deja de aplicar.</b> Registrar es la mitad del trabajo; la otra es
/// resolver la alerta viva de una entidad que ya no está en la condición (una baja anulada, un
/// bloqueo que expiró, un conflicto que se overruled). Una alerta que sigue viva cuando el problema
/// ya no existe es peor que no tenerla: entrena a quien mira a ignorar el panel.
/// </para>
/// </summary>
public sealed class ServicioAvisosOperacion(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    private const string TipoContrato = "contrato";
    private const string TipoReserva = "reserva";
    private const string TipoSeguridad = "seguridad";

    /// <summary>Ventana, en días, en la que se avisa de una baja programada (de hoy a hoy + 7).</summary>
    public const int DiasDeAvisoBaja = 7;

    /// <summary>
    /// Ventana, en días, en la que se avisa de que una unidad se libera (§3.16 dice literal:
    /// "unidades que se liberan en 30 días").
    /// </summary>
    public const int DiasDeAvisoLiberacion = 30;

    private const string PrefijoBaja = "contrato-baja-programada:";
    private const string PrefijoLiberacion = "contrato-liberacion-30:";
    private const string PrefijoConflicto = "reserva-conflicto:";
    private const string PrefijoIpBloqueada = "ip-bloqueada:";

    public static string ClaveBajaProgramada(Guid contratoId) => $"{PrefijoBaja}{contratoId:D}";

    public static string ClaveLiberacion30(Guid contratoId) => $"{PrefijoLiberacion}{contratoId:D}";

    public static string ClaveReservaConflicto(Guid reservaId) => $"{PrefijoConflicto}{reservaId:D}";

    public static string ClaveIpBloqueada(Guid bloqueoId) => $"{PrefijoIpBloqueada}{bloqueoId:D}";

    /// <summary>
    /// Una pasada: deja vivos (o refresca) los avisos que aplican hoy y cierra los que ya no.
    /// Idempotente por partida doble, como los demás trabajos: la condición de fecha hace que una
    /// segunda pasada normal no encuentre nada nuevo, y la clave única hace que lo que encuentre
    /// caiga en la fila de siempre.
    /// </summary>
    public async Task<ResumenAvisos> AvisarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        // 1. Bajas programadas cuya fecha efectiva cae en [hoy, hoy + 7]. Es una ventana y no una
        // igualdad (BUG-028): una baja dada con menos de 7 días avisa el mismo día, y si el trabajo
        // falla un día el siguiente sigue avisando. Se mira FechaBajaProgramada y no FechaFinVigente
        // porque es la fecha que eligió la persona que dio la baja (§3.5) y es la que el trabajo de
        // las 07:00 va a respetar.
        var bajas = await contexto.Contrato.AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.BajaProgramada
                && c.FechaBajaProgramada >= hoy
                && c.FechaBajaProgramada <= hoy.AddDays(DiasDeAvisoBaja))
            .Select(c => new { c.Id, c.UnidadId, c.FechaBajaProgramada })
            .ToListAsync(ct);

        foreach (var baja in bajas)
        {
            await alertas.RegistrarAsync(
                TipoContrato,
                ClaveBajaProgramada(baja.Id),
                $"Contrato {baja.Id:D}: baja programada el {baja.FechaBajaProgramada:dd/MM/yyyy}, en "
                    + $"{baja.FechaBajaProgramada!.Value.DayNumber - hoy.DayNumber} día(s). La unidad {baja.UnidadId:D} sigue bloqueada hasta esa "
                    + "fecha, y al llegar el trabajo la libera.",
                ct);
        }

        // 2. Unidades que se liberan en [hoy, hoy + 30]. Solo las que se liberan de verdad: las bajas
        // programadas. Un contrato Activo se renueva solo por bloques hasta que alguien da la baja
        // (§3.5), así que su fin de bloque no libera nada y avisar de él sería ruido. Ventana y no
        // igualdad, por el mismo motivo que la baja (BUG-028).
        // TODO(A1): §3.16 también habla de "fines sin renovación automática", pero el contrato no
        // tiene ningún campo que diga "no se renueva": la baja es la única forma de que termine. Si
        // se añade ese campo, sumar aquí esos contratos. Y una baja a menos de 7 días da dos avisos
        // (baja programada y liberación en 30) con claves distintas porque §3.16 los lista como dos
        // alertas; confirmar si debe ser uno solo.
        var liberaciones = await contexto.Contrato.AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.BajaProgramada
                && c.FechaBajaProgramada >= hoy
                && c.FechaBajaProgramada <= hoy.AddDays(DiasDeAvisoLiberacion))
            .Select(c => new { c.Id, c.UnidadId, c.FechaBajaProgramada })
            .ToListAsync(ct);

        foreach (var liberacion in liberaciones)
        {
            await alertas.RegistrarAsync(
                TipoContrato,
                ClaveLiberacion30(liberacion.Id),
                $"Contrato {liberacion.Id:D}: termina el {liberacion.FechaBajaProgramada:dd/MM/yyyy}, dentro "
                    + $"de {DiasDeAvisoLiberacion} días como máximo. La unidad {liberacion.UnidadId:D} queda "
                    + "libre a partir de esa fecha: hay que anunciarla.",
                ct);
        }

        // 3. Reservas que chocan con un contrato vigente (§3.4). La reserva no se cancela sola: el
        // cliente tiene que buscar otra unidad, y eso lo tiene que ver alguien.
        var conflictos = await contexto.Reserva.AsNoTracking()
            .Where(r => r.Activo && r.Estado == EstadoReserva.EnConflicto)
            .Select(r => new { r.Id, r.UnidadId, r.ClienteId, r.ProspectoId, r.EntradaPrevista })
            .ToListAsync(ct);

        foreach (var conflicto in conflictos)
        {
            var quien = conflicto.ClienteId ?? conflicto.ProspectoId;

            await alertas.RegistrarAsync(
                TipoReserva,
                ClaveReservaConflicto(conflicto.Id),
                $"Reserva {conflicto.Id:D} en conflicto: la unidad {conflicto.UnidadId:D} ya la tiene un "
                    + $"contrato y esta reserva entra el {conflicto.EntradaPrevista:dd/MM/yyyy}. "
                    + $"Hay que avisar al cliente {quien:D} y buscarle otra unidad.",
                ct);
        }

        // 4. IPs bloqueadas por el escalado de intentos de login. La tabla `bloqueo_ip` ya existe
        // (F1-05) y dice qué está bloqueado y hasta cuándo, así que esto no necesita nada de F3.
        var bloqueos = await contexto.BloqueoIp.AsNoTracking()
            .Where(b => b.Nivel > 0 && (b.BloqueadaHasta == null || b.BloqueadaHasta > reloj.GetUtcNow()))
            .Select(b => new { b.Id, b.Ip, b.Nivel, b.Fallos, b.BloqueadaHasta })
            .ToListAsync(ct);

        foreach (var bloqueo in bloqueos)
        {
            var hasta = bloqueo.BloqueadaHasta is { } limite
                ? $" hasta el {limite:dd/MM/yyyy HH:mm} UTC"
                : " de forma indefinida (nivel 3 del escalado)";

            await alertas.RegistrarAsync(
                TipoSeguridad,
                ClaveIpBloqueada(bloqueo.Id),
                $"IP {bloqueo.Ip} bloqueada{hasta} por {bloqueo.Fallos} intento(s) de login fallidos "
                    + $"(nivel {bloqueo.Nivel}). Nadie entra desde esa dirección hasta que expire.",
                ct);
        }

        // Y lo contrario: cerrar lo que ya no aplica. Sin esto, anular una baja dejaría el aviso
        // encendido diciendo una fecha que ya no existe.
        var cerradas = 0;
        cerradas += await CerrarObsoletasAsync(
            PrefijoBaja,
            [.. bajas.Select(b => ClaveBajaProgramada(b.Id))], ct);
        cerradas += await CerrarObsoletasAsync(
            PrefijoLiberacion,
            [.. liberaciones.Select(l => ClaveLiberacion30(l.Id))], ct);
        cerradas += await CerrarObsoletasAsync(
            PrefijoConflicto,
            [.. conflictos.Select(c => ClaveReservaConflicto(c.Id))], ct);
        cerradas += await CerrarObsoletasAsync(
            PrefijoIpBloqueada,
            [.. bloqueos.Select(b => ClaveIpBloqueada(b.Id))], ct);

        Log.Information(
            "Avisos de operación: {Bajas} baja(s) próxima(s), {Liberaciones} liberación(es) en {Dias} "
                + "días, {Conflictos} conflicto(s), {Bloqueos} IP bloqueada(s), {Cerradas} alerta(s) "
                + "cerradas por caducar",
            bajas.Count,
            liberaciones.Count,
            DiasDeAvisoLiberacion,
            conflictos.Count,
            bloqueos.Count,
            cerradas);

        return new ResumenAvisos(bajas.Count, liberaciones.Count, conflictos.Count, bloqueos.Count, cerradas);
    }

    /// <summary>
    /// Cierra las alertas vivas de este prefijo cuya clave no está en <paramref name="vigientes"/>.
    /// Se lee la tabla <c>alerta</c> por prefijo y no se reescribe nada: el aviso viejo se queda como
    /// está (AGENTS.md §6) y solo se le pone <c>resuelta_at</c>, que es cerrar, no borrar.
    /// <para>
    /// La comparación contra las claves vigentes va en memoria a propósito. Las alertas vivas de un
    /// prefijo son pocas —una por entidad con el problema abierto— y meter una lista en el
    /// <c>Contains</c> de LINQ-to-Entities depende de que EF la traduzca a <c>ANY</c>: si mañana no lo
    /// hace, el trabajo entero falla por un detalle de traducir una consulta que cabe en memoria.
    /// </para>
    /// </summary>
    private async Task<int> CerrarObsoletasAsync(
        string prefijo,
        IReadOnlyCollection<string> vigentes,
        CancellationToken ct)
    {
        var vivas = await contexto.Alerta.AsNoTracking()
            .Where(a => a.ResueltaAt == null && a.Clave.StartsWith(prefijo))
            .Select(a => a.Clave)
            .ToListAsync(ct);

        string[] obsoletas = [.. vivas.Where(clave => !vigentes.Contains(clave))];

        foreach (var clave in obsoletas)
        {
            await alertas.ResolverAsync(clave, ct);
        }

        return obsoletas.Length;
    }

    // ---------------------------------------------------------------- lo que falta

    // TODO(A#) F2/F3: "entradas desde IP nueva". La tabla `sesion` sí existe y trae la IP, así que
    // falta poco, pero no se implementa aquí porque falta una decisión de negocio que no es del
    // implementador: qué cuenta como IP nueva (global en el sistema, por usuario, o por red), porque
    // las tres dan números muy distintos y un aviso que sale cada día por una IP que no es sospechosa
    // entrena a ignorar el panel.
    //
    // Las demás alertas de §3.16 (facturas vencidas, periodos sin facturar, comprobantes, retenciones,
    // fianzas, envíos fallidos, tipo de cambio) viven en sus propios servicios y trabajos.
}

/// <summary>Qué ha hecho una pasada de los avisos de operación. Sirve para el log y para los tests.</summary>
/// <param name="BajasProgramadas">Contratos en baja cuya fecha efectiva entra en la ventana de aviso.</param>
/// <param name="Liberaciones">Contratos en baja cuya unidad queda libre dentro de la ventana de aviso.</param>
/// <param name="Conflictos">Reservas que siguen chocando con un contrato.</param>
/// <param name="IpsBloqueadas">Direcciones con el bloqueo de login vigente.</param>
/// <param name="AlertasCerradas">Avisos cerrados porque la entidad dejó de estar en la condición.</param>
public sealed record ResumenAvisos(
    int BajasProgramadas,
    int Liberaciones,
    int Conflictos,
    int IpsBloqueadas,
    int AlertasCerradas);