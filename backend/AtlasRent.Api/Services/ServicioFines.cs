using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace AtlasRent.Api.Services;

/// <summary>
/// Fin por fecha efectiva (F2-09).
/// <para>
/// El criterio de aceptación es "la unidad queda libre <b>desde la fecha efectiva</b>", y eso no lo
/// cumple un botón que alguien tiene que acordarse de apretar: la baja tiene fecha, y en esa fecha el
/// cliente se va. Aquí está el trabajo que hace que pase: cada mañana cierra los contratos en
/// <see cref="EstadoContrato.BajaProgramada"/> cuya fecha efectiva ya pasó.
/// </para>
/// <para>
/// <b>Por qué no llama a <see cref="ServicioContratos.FinalizarAsync"/>.</b> Ese método exige
/// <c>contrato.estado</c> y por lo tanto una persona con sesión, y un trabajo de las 07:00 no tiene
/// sesión. Lo que hace el trabajo es exactamente lo mismo —liberar el candado y pasar a
/// <c>Finalizado</c>— pero auditado como lo que es: una fila de auditoría con <c>origen = "job"</c>,
/// <c>usuario_id = NULL</c> e IP del propio trabajo, igual que hacen el trabajo de renovación (F2-06) y
/// el de copia de seguridad. Es la misma regla de <c>AuditoriaService</c>: si no hay persona, la
/// auditoría lo dice, no lo disfraza de usuario.
/// </para>
/// <para>
/// <b>Idempotencia en dos capas</b>, como el resto de trabajos: (1) la condición de fecha —solo entran
/// los contratos cuya fecha efectiva es anterior o igual a hoy— y (2) que reintentar sobre un contrato
/// que ya está finalizado falla por estado con 409, que aquí se cuenta como omitido y no como caída.
/// Entre una carrera y otra pasa además <c>xmin</c>, que hace que el segundo <c>SaveChanges</c> pierda.
/// </para>
/// </summary>
public sealed class ServicioFines(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    IRegistroAlertas alertas,
    ServicioDisponibilidad disponibilidad,
    TimeProvider reloj)
{
    private const string OrigenDelTrabajo = "job";

    private const string IpDelTrabajo = "127.0.0.1";

    private const string AccionAuditoria = "contrato.finalizado_por_fecha";

    private const string TipoAlerta = "contrato";

    /// <summary>
    /// Clave de la alerta persistente de un contrato finalizado sin liquidar. El prefijo la hace
    /// legible en la tabla <c>alerta</c> y el identificador la hace única por contrato, que es lo que
    /// permite que el refresco diario no duplique.
    /// </summary>
    public static string ClaveFirmezaPendiente(Guid contratoId) =>
        $"contrato-firmeza-pendiente:{contratoId:D}";

    /// <summary>
    /// Deja viva (o refresca) la alerta de un contrato finalizado y todavía no liquidado.
    /// <para>
    /// Se llama en tres sitios y siempre con la misma clave: al finalizar por la API, al finalizar
    /// por el trabajo de las 07:00 y en el barrido diario de ese mismo trabajo. El refresco lo hace el
    /// <c>ON CONFLICT</c> de la clave, de modo que da igual cuántas veces se llame: hay una fila y su
    /// mensaje dice cuántos días lleva el contrato esperando.
    /// </para>
    /// </summary>
    public static Task RefrescarAlertaFirmezaAsync(
        IRegistroAlertas alertas,
        Guid contratoId,
        DateOnly fechaFin,
        DateOnly hoy,
        CancellationToken ct)
    {
        var dias = Math.Max(0, hoy.DayNumber - fechaFin.DayNumber);

        return alertas.RegistrarAsync(
            TipoAlerta,
            ClaveFirmezaPendiente(contratoId),
            $"Contrato {contratoId:D}: fin el {fechaFin:dd/MM/yyyy} y todavía sin liquidar, "
                + $"{dias} día(s) esperando. La liquidación cierra inventario de salida, fianza y "
                + "saldos, y no es opcional (§3.5).",
            ct);
    }

    /// <summary>
    /// Cierra los contratos vencidos y deja la alerta persistente al día. Es idempotente: correrlo dos
    /// veces seguidas no finaliza dos veces ni duplica alertas.
    /// </summary>
    public async Task<ResumenFines> FinalizarVencidosAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        // Capa 1 de idempotencia: la fecha. Sin ella, cada corrida miraría el mismo contrato y
        // decidiría sobre el estado de otra corrida.
        var vencidos = await contexto.Contrato
            .AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.BajaProgramada
                && c.FechaBajaProgramada != null
                && c.FechaBajaProgramada <= hoy)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var finalizados = 0;
        var omitidos = 0;

        foreach (var id in vencidos)
        {
            // El context es del job, no de una petición: hay que soltar lo que dejó el ciclo anterior
            // o el SaveChanges de un contrato se llevaría por delante el estado que otro está
            // leyendo. Es lo mismo que hace TrabajoRenovacionContratos.
            contexto.ChangeTracker.Clear();

            try
            {
                if (await FinalizarAsync(id, hoy, ct))
                {
                    finalizados++;
                }
                else
                {
                    // Otra corrida lo movió entre la consulta y aquí.
                    omitidos++;
                }
            }
            catch (InvalidOperationException)
            {
                // Capa 2: el estado ya no admite el paso (y por tanto, con ella, el cambio sería un
                // 409 para la API). Finalizar dos veces no es un fallo del trabajo: es un trabajo que
                // no tenía nada que hacer.
                omitidos++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // xmin: dos corridas simultáneas sobre el mismo contrato.
                omitidos++;
            }
        }

        // El barrido va después del bucle, no en paralelo con él, y se salta los contratos que esta
        // pasada acaba de finalizar porque a esos ya les refrescó `FinalizarAsync`. Refrescarlos dos
        // veces en la misma mañana no es inofensivo: el contador de repeticiones es el que dice
        // "lleva dos días esperando", y sumar dos veces al mismo día lo mentiría.
        var alertas = await RefrescarAlertasPendientesAsync(hoy, vencidos, ct);

        Log.Information("Trabajo de fin: {Finalizados} contrato(s) finalizados, {Omitidos} omitido(s), "
            + "{Alertas} alerta(s) de firmeza pendiente al día", finalizados, omitidos, alertas);

        return new ResumenFines(finalizados, omitidos, alertas);
    }

    /// <summary>
    /// Finaliza un contrato concreto fuera de su sesión y devuelve si lo hizo. Devuelve <c>false</c>
    /// sin lanzar cuando el contrato ya no está en <c>BajaProgramada</c> o la fecha todavía no ha
    /// llegado: es la primera línea de la idempotencia, antes incluso de tocar nada.
    /// </summary>
    private async Task<bool> FinalizarAsync(Guid contratoId, DateOnly hoy, CancellationToken ct)
    {
        await using var tx = await contexto.Database.BeginTransactionAsync(ct);

        var contrato = await contexto.Contrato.FirstOrDefaultAsync(c => c.Id == contratoId, ct);

        if (contrato is null
            || contrato.Estado != EstadoContrato.BajaProgramada
            || contrato.FechaBajaProgramada is null
            || contrato.FechaBajaProgramada > hoy)
        {
            return false;
        }

        // La fecha efectiva es la pactada, no hoy: si el trabajo se cayó tres días, el cliente se fue
        // hace tres días y escribir la fecha de ejecución falsearía el histórico del contrato.
        contrato.FechaFinReal = contrato.FechaBajaProgramada;

        await disponibilidad.LiberarRangoAsync(OrigenOcupacion.Contrato, contrato.Id, ct);

        ServicioContratos.CambiarEstado(
            contrato,
            EstadoContrato.Finalizado,
            usuario: null,
            "Fin por fecha efectiva. El cliente se va el día de la fecha de baja y la unidad queda "
                + "libre desde ese día (F2-09).");

        await RefrescarAlertaFirmezaAsync(
            alertas,
            contrato.Id,
            contrato.FechaFinReal!.Value,
            hoy,
            ct);

        await contexto.SaveChangesAsync(ct);

        // La auditoría va dentro de la transacción, antes del commit, como en la renovación (F2-06): si
        // el proceso muriera entre el commit y el aviso, el contrato quedaría finalizado sin rastro de
        // por qué y el trabajo no lo volvería a tocar porque ya no está en BajaProgramada.
        //
        // Y va como va: `usuario_id` nulo y `origen = "job"`. La auditoría con RLS necesita un
        // contexto y este trabajo no tiene sesión, así que la fila lo dice de sí misma: la cambió el
        // sistema, no un gerente. Falsificar un usuario para que la fila "entre" sería dejar en el
        // historial una persona que no hizo esto.
        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = AccionAuditoria,
            Ip = IpDelTrabajo,
            UsuarioId = null,
            Entidad = "contrato",
            EntidadId = contrato.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new
            {
                FechaBajaProgramada = contrato.FechaBajaProgramada,
                FechaFinReal = contrato.FechaFinReal,
                FechaEjecucion = hoy,
            }),
            Resultado = "permitido",
            Origen = OrigenDelTrabajo,
        }, ct);

        await tx.CommitAsync(ct);

        return true;
    }

    /// <summary>
    /// Repasa los contratos ya finalizados y sin liquidar y les refresca la alerta. Es lo que hace que
    /// la alerta sea <i>persistente</i> y no un aviso del día en que se finalizó: sin este barrido,
    /// un contrato finalizado a mano por un gerente no tendría ninguna alerta hasta el día siguiente,
    /// y el único rastro sería un estado que nadie mira.
    /// </summary>
    /// <param name="yaRefrescados">Contratos que esta misma pasada ya avisó al finalizar. Se excluyen
    /// para que el contador sume una vez por día y por contrato, no una vez por cada vez que el
    /// trabajo lo toca.</param>
    private async Task<int> RefrescarAlertasPendientesAsync(
        DateOnly hoy,
        IReadOnlyCollection<Guid> yaRefrescados,
        CancellationToken ct)
    {
        var pendientes = await contexto.Contrato
            .AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.Finalizado
                && c.Liquidacion == null
                && !yaRefrescados.Contains(c.Id))
            .Select(c => new { c.Id, c.FechaFinReal })
            .ToListAsync(ct);

        foreach (var contrato in pendientes)
        {
            await RefrescarAlertaFirmezaAsync(
                alertas,
                contrato.Id,
                contrato.FechaFinReal ?? hoy,
                hoy,
                ct);
        }

        return pendientes.Count;
    }
}

/// <summary>Qué ha hecho una pasada del trabajo de fin. Sirve para el log y para los tests.</summary>
/// <param name="ContratosFinalizados">Contratos que pasaron a <c>Finalizado</c> en esta pasada.</param>
/// <param name="Omitidos">Candidatos que ya estaban resueltos por otra pasada o por un humano.</param>
/// <param name="AlertasVivas">Alertas de firmeza refrescadas, que son las que había sin liquidar.</param>
public sealed record ResumenFines(
    int ContratosFinalizados,
    int Omitidos,
    int AlertasVivas);