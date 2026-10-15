using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using System.Text.Json;

namespace AtlasRent.Api.Services;

/// <summary>
/// Renovación automática por bloques e incrementos de renta de un contrato activo (F2-06,
/// docs/ESPECIFICACION.md §3.5).
/// <para>
/// La especificación deja las dos cosas en la misma frase: "la renovación automática por bloques
/// queda en el historial, sin anexo ni firma nueva" y "los incrementos de renta, en porcentaje o
/// importe fijo, con fecha, se aplican solos con aviso interno". Aquí las dos se ejecutan juntas y en
/// el mismo orden, porque el día que se renueva un bloque es el día que vencen los incrementos que
/// entran en vigor en él.
/// </para>
/// </summary>
public sealed class ServicioRenovaciones(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>
    /// Origen de la auditoría de una renovación. No es "api": no hay ninguna petición detrás, la
    /// escribió el planificador (docs/DOC_TECNICA.md §8).
    /// </summary>
    private const string Origen = "job";

    /// <summary>
    /// IP que se audita. La auditoría exige una IP en toda mutación (AGENTS.md §6) y el trabajo no
    /// tiene cliente: la que corresponde es la del propio servidor, que es donde ocurre.
    /// </summary>
    private const string IpDelTrabajo = "127.0.0.1";

    /// <summary>Tipo de alerta, el mismo que usan las de reservas, copias e integridad.</summary>
    private const string TipoAlerta = "contrato";

    private const string AccionAuditoria = "contrato.renovacion_aplicada";

    /// <summary>
    /// Clave de deduplicación del aviso de un incremento. Lleva el incremento, no la ejecución: el
    /// aviso es una vez y para siempre por incremento, como el resto de alertas del sistema, así que
    /// que el trabajo pase dos veces el mismo día no lo repite.
    /// </summary>
    public static string ClaveIncremento(Guid incrementoId) => $"contrato-incremento:{incrementoId:D}";

    /// <summary>
    /// Renueva todos los contratos activos cuyo bloque en curso ya venció.
    /// <para>
    /// El criterio es <see cref="Contrato.FechaFinVigente"/> anterior a hoy en la zona de negocio
    /// (D16, como en el trabajo de aviso de reservas: el planificador corre en UTC y el día que
    /// cuenta es el de Santo Domingo). No basta con "está activo": un contrato cuyo bloque termina
    /// dentro de tres meses no se toca hoy, y esa es también la razón de que correr el trabajo dos
    /// veces el mismo día no renueve dos veces.
    /// </para>
    /// </summary>
    public async Task<ResumenRenovacion> RenovarVencidosAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        var candidatos = await contexto.Contrato
            .AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.Activo && c.FechaFinVigente < hoy)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var resumen = new ResumenRenovacion();

        foreach (var contratoId in candidatos)
        {
            // Cada contrato va por su cuenta: uno que falle no puede dejar a medias la renovación de
            // los demás, y su transacción se deshace sola.
            contexto.ChangeTracker.Clear();

            try
            {
                var hecho = await RenovarAsync(contratoId, hoy, ct);
                if (hecho is not null)
                {
                    resumen.ContratosRenovados++;
                    resumen.Bloques += hecho.Bloques;
                    resumen.IncrementosAplicados += hecho.Incrementos;
                }
            }
            catch (DbUpdateException excepcion) when (EsViolacionDeIndiceUnico(excepcion))
            {
                // Otra copia del trabajo renovó este contrato a la vez y su bloque se coló primero
                // (índice único de contrato_bloque). No es un fallo: es la red funcionando, y lo
                // correcto es quedarse con el bloque que ya existe.
                resumen.OmitidosPorCarrera++;
                Log.Information(
                    "Renovación: el contrato {ContratoId:D} se renovó en otra ejecución a la vez; "
                    + "este trabajo no lo vuelve a tocar",
                    contratoId);
            }
            catch (DbUpdateConcurrencyException)
            {
                // xmin: la misma carrera del párrafo anterior, vista por el otro lado. `xmin` es
                // token de concurrencia en todas las entidades menos `Auditoria`, así que el
                // `UPDATE` de `contrato` lleva `WHERE xmin = <el que se leyó>`; si la otra ejecución
                // renovó y confirmó antes, esa condición no casa con ninguna fila y EF lanza
                // `DbUpdateConcurrencyException`, que hereda de `DbUpdateException` pero cuyo
                // `InnerException` no es un `PostgresException`, de modo que el filtro de arriba no
                // la recoge. Sin este `catch` la ejecución perdedora escapaba y Hangfire daba el
                // trabajo por fallido en vez de contarlo como omitido.
                //
                // Mismo tratamiento que en `ServicioFines` (que ya capturaba el `xmin`): contar y
                // seguir, porque el bloque ganador ya está escrito y rehacerlo sería peor.
                resumen.OmitidosPorCarrera++;
                Log.Information(
                    "Renovación: el contrato {ContratoId:D} cambió mientras este trabajo lo leía; "
                    + "otra ejecución se adelantó y este trabajo no lo vuelve a tocar",
                    contratoId);
            }
        }

        Log.Information(
            "Renovación de contratos: {Renovados} contrato(s), {Bloques} bloque(s), {Incrementos} "
                + "incremento(s), {Omitidos} omitido(s) por ejecución simultánea",
            resumen.ContratosRenovados,
            resumen.Bloques,
            resumen.IncrementosAplicados,
            resumen.OmitidosPorCarrera);

        return resumen;
    }

    /// <summary>
    /// Renueva un contrato en su propia transacción y devuelve qué hizo, o <c>null</c> si no había
    /// nada que hacer.
    /// <para>
    /// <b>Por qué aquí no se toca <c>ocupacion_rango</c>.</b> Al activarse, el contrato ocupa la
    /// unidad con un rango abierto (<c>fecha_fin = NULL</c>) que sigue bloqueando hasta que se
    /// finaliza. La unidad está ocupada desde el inicio del contrato y lo está también después de
    /// renovar, porque renovar no la libera de nada: no hay hueco entre el bloque viejo y el nuevo en
    /// el que quepa una reserva. Si algún día alguien mete aquí una llamada a
    /// <c>OcuparRangoAsync</c> o a <c>LiberarRangoAsync</c>, abre un agujero: la reserva que se
    /// aceptan entre los dos bloques se lleva la unidad por delante del contrato.
    /// </para>
    /// </summary>
    private async Task<HechoRenovacion?> RenovarAsync(Guid contratoId, DateOnly hoy, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // Releído dentro de la transacción: entre la lista de candidatos y aquí, otro proceso puede
        // haber renovado este contrato, dado de baja o finalizado. Sin esta comprobación, un
        // relanzamiento del trabajo a media noche renueva dos veces.
        var contrato = await contexto.Contrato
            .FirstOrDefaultAsync(c => c.Id == contratoId, ct);

        if (contrato is null || contrato.Estado != EstadoContrato.Activo || contrato.FechaFinVigente >= hoy)
        {
            return null;
        }

        // Un contrato con duración de renovación 0 no puede avanzar nunca, y el bucle de bloques no
        // terminaría. Se avisa en lugar de colgar el trabajo de las seis de la mañana.
        if (contrato.RenovacionMeses <= 0)
        {
            Log.Error(
                "Renovación: el contrato {ContratoId:D} tiene RenovacionMeses = {Meses}; no se renueva",
                contratoId,
                contrato.RenovacionMeses);
            return null;
        }

        var ahora = reloj.GetUtcNow();
        var finVigenteAnterior = contrato.FechaFinVigente;

        // El número de bloque se calcula leyendo el máximo, igual que el de versión: el bloque 1 es
        // el inicial del contrato, que no tiene fila, así que el primero que se escribe es el 2.
        var maximo = await contexto.ContratoBloque
            .Where(b => b.ContratoId == contratoId)
            .MaxAsync(b => (int?)b.Numero, ct);
        var numeroBloque = maximo is { } valor ? valor + 1 : 2;
        var numeroVersion = await NumeroSiguienteVersionAsync(contratoId, ct);

        // Cancelados fuera: un incremento que alguien retiró ya no sube la renta, y mirarlo sería
        // aplicar una decisión que el negocio dio por deshecha. La fila se queda, con su marca.
        var pendientes = await contexto.ContratoIncremento
            .Where(i => i.ContratoId == contratoId && !i.Aplicado && !i.Cancelado)
            .OrderBy(i => i.FechaVigencia)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(ct);

        var bloques = 0;
        var incrementos = 0;

        // Un contrato que lleva meses sin renovar entra por <b>todos</b> los bloques que debería
        // haber tenido, no solo por uno, y en una sola transacción. El motivo es que la renovación
        // automática no depende de que nadie decidiera nada: el contrato se renovaba igual, solo que
        // el trabajo no llegó a correr. Saltarse los bloques perdidos dejaría el historial incompleto
        // —la renta de un tramo que alguien vivió y se facturó no constaría en ninguna parte— y un
        // incremento con fecha dentro de uno de esos bloques nunca se aplicaría, que es justo el
        // caso que §3.5 quiere cubrir. Además, el bucle termina en cuanto el bloque en curso cubre
        // hoy, así que un contrato parado hace años no genera cientos de filas de golpe.
        while (contrato.FechaFinVigente < hoy)
        {
            var inicio = contrato.FechaFinVigente;
            var fin = inicio.AddMonths(contrato.RenovacionMeses);

            // Los incrementos cuya FechaVigencia es anterior o igual al inicio del bloque nuevo: uno con fecha
            // futura no adelanta la renta, espera a su bloque. Con más de un bloque de atraso, el
            // incremento que ya se aplicó en el anterior no se vuelve a aplicar aquí, que es lo que
            // hace que se aplique una sola vez y no dependa solo de que el trabajo pase.
            foreach (var incremento in pendientes)
            {
                if (incremento.Aplicado || incremento.FechaVigencia > inicio)
                {
                    continue;
                }
                var rentaAnterior = contrato.Renta;
                contrato.Renta = CalcularRenta(incremento, rentaAnterior);

                incremento.Aplicado = true;
                incremento.AplicadoAt = ahora;
                incremento.RentaAntes = rentaAnterior;
                incremento.RentaDespues = contrato.Renta;
                incremento.UpdatedAt = ahora;
                incremento.UpdatedBy = null;

                // El cambio de renta es un cambio de renta: entra por el historial de versiones con
                // su alcance, igual que un cambio pedido por un gerente, o "los cambios de renta
                // afectan solo a facturas futuras" dejaría de ser cierto para los incrementos.
                contexto.ContratoVersion.Add(ConstruirVersion(
                    contrato,
                    numeroVersion++,
                    $"Incremento {Descripcion(incremento)} con fecha {incremento.FechaVigencia:dd/MM/yyyy}, "
                        + $"aplicado automáticamente (F2-06): renta {rentaAnterior:0.00} -> "
                        + $"{contrato.Renta:0.00} {contrato.Moneda}",
                    rentaAnterior,
                    ahora));

                // El aviso va en la misma transacción que el cambio y no después: si el proceso
                // muriera entre el commit y el aviso, el incremento quedaría aplicado sin que nadie
                // se enterara y no volvería a avisar, porque ya está marcado como aplicado.
                //
                // Que `RegistrarAsync` escriba por SQL crudo y no por EF no obliga a volcar el
                // `ChangeTracker` antes: el aviso no compite por ningún índice único con lo que
                // falta por guardar, así que no hay ninguna colisión que "salte" aquí. Lo que
                // compite (`contrato_bloque`, `contrato_version` y la versión de `contrato` con su
                // `xmin`) se decide entero en el `SaveChangesAsync` de más abajo, dentro de la misma
                // transacción, y la carrera la recogen los dos `catch` del trabajo. Forzar un
                // `SaveChangesAsync` aquí solo movería el punto de fallo sin evitarlo.
                await alertas.RegistrarAsync(
                    TipoAlerta,
                    ClaveIncremento(incremento.Id),
                    $"Incremento {Descripcion(incremento)} aplicado al contrato {contratoId:D} con "
                        + $"fecha {incremento.FechaVigencia:dd/MM/yyyy}: la renta pasa de "
                        + $"{rentaAnterior:0.00} a {contrato.Renta:0.00} {contrato.Moneda}.",
                    ct);

                incrementos++;
            }

            await contexto.ContratoBloque.AddAsync(new ContratoBloque
            {
                Id = Guid.NewGuid(),
                CreatedAt = ahora,
                CreatedBy = null,
                ContratoId = contratoId,
                Numero = numeroBloque++,
                FechaInicio = inicio,
                FechaFin = fin,
                Renta = contrato.Renta,
            }, ct);

            contrato.FechaFinVigente = fin;
            bloques++;
        }

        contrato.UpdatedAt = ahora;
        contrato.UpdatedBy = null;

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = AccionAuditoria,
            Ip = IpDelTrabajo,
            UsuarioId = null,
            Entidad = "contrato",
            EntidadId = contratoId.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new
            {
                FechaFinVigenteAnterior = finVigenteAnterior,
                FechaFinVigenteNueva = contrato.FechaFinVigente,
                BloquesAbiertos = bloques,
                IncrementosAplicados = incrementos,
            }),
            Resultado = "permitido",
            Origen = Origen,
        }, ct);

        await transaccion.CommitAsync(ct);

        return new HechoRenovacion(bloques, incrementos);
    }

    /// <summary>
    /// Historial de bloques de renovación de un contrato, del más antiguo al más reciente.
    /// <para>
    /// Los incrementos los lista <see cref="ServicioIncrementos"/>: el alta y la cancelación viven
    /// allí, y partir la lectura en dos sitios haría que se le olvidara una columna.
    /// </para>
    /// </summary>
    public async Task<List<ContratoBloqueRespuesta>> ListarBloquesAsync(
        Guid contratoId,
        CancellationToken ct) =>
        await contexto.ContratoBloque
            .AsNoTracking()
            .Where(b => b.ContratoId == contratoId)
            .OrderBy(b => b.Numero)
            .Select(b => new ContratoBloqueRespuesta(
                b.Numero,
                b.FechaInicio,
                b.FechaFin,
                b.Renta,
                b.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// La renta que queda tras el incremento. El porcentaje se aplica sobre la renta vigente en ese
    /// momento (no sobre la original del contrato) y el importe fijo se suma tal cual, en la moneda
    /// del contrato, sin convertir. El redondeo es el de <see cref="Redondeo"/>: medio hacia arriba a
    /// dos decimales.
    /// <para>
    /// El divisor del porcentaje es una constante con nombre y no una cifra en la línea del
    /// <c>switch</c> por D14: una línea que habla de porcentajes no puede traer números escritos, y
    /// el 100 es el divisor, no un tipo de impuesto. El valor del incremento, ese sí, viene de la
    /// tabla y no del código.
    /// </para>
    /// </summary>
    private static decimal CalcularRenta(ContratoIncremento incremento, decimal renta)
    {
        const decimal DivisorPorcentaje = 100m;

        return incremento.Tipo switch
        {
            TipoIncremento.Porcentaje => Redondeo.Importe(
                renta * (1 + (incremento.Porcentaje ?? 0) / DivisorPorcentaje)),
            _ => Redondeo.Importe(renta + (incremento.Importe ?? 0)),
        };
    }

    /// <summary>El incremento en la frase que va al historial y a la alerta, tal como lo escribió el cliente.</summary>
    private static string Descripcion(ContratoIncremento incremento) =>
        incremento.Tipo switch
        {
            TipoIncremento.Porcentaje => $"porcentaje {incremento.Porcentaje:0.####} %",
            _ => $"importe fijo {incremento.Importe:0.00}",
        };

    private ContratoVersion ConstruirVersion(
        Contrato contrato,
        int numero,
        string cambio,
        decimal rentaAnterior,
        DateTimeOffset ahora) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAt = ahora,
            CreatedBy = null,
            ContratoId = contrato.Id,
            Numero = numero,
            Cambio = cambio,
            RentaAnterior = rentaAnterior,
            RentaNueva = contrato.Renta,
            Moneda = contrato.Moneda,
            Alcance = "Afecta solo a facturas futuras. Lo ya facturado no cambia.",
        };

    /// <summary>
    /// Siguiente número de versión del contrato. Se lee una vez por renovación y se va sumando en
    /// memoria: un incremento aplica varios cambios de renta seguidos en la misma transacción y no
    /// pueden salir con el mismo número. El índice único de <c>contrato_version</c> sigue siendo la
    /// red si dos trabajos se pisan.
    /// </summary>
    private async Task<int> NumeroSiguienteVersionAsync(Guid contratoId, CancellationToken ct)
    {
        var maximo = await contexto.ContratoVersion
            .Where(v => v.ContratoId == contratoId)
            .MaxAsync(v => (int?)v.Numero, ct);

        return maximo is { } valor ? valor + 1 : 1;
    }

    /// <summary>
    /// Si el fallo es la red de seguridad (índice único) y no un problema de los datos. La violet
    /// interesa porque significa que otra ejecución se adelantó y que lo correcto es no rehacer nada.
    /// </summary>
    private static bool EsViolacionDeIndiceUnico(DbUpdateException excepcion) =>
        excepcion.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Qué hizo el trabajo en una pasada.</summary>
    private sealed record HechoRenovacion(int Bloques, int Incrementos);
}

/// <summary>
/// Resultado de una pasada del trabajo de renovación, para el log del planificador y para que las
/// pruebas puedan comprobar qué pasó sin mirar la base de datos.
/// </summary>
public sealed record ResumenRenovacion
{
    /// <summary>Contratos a los que se les abrió al menos un bloque.</summary>
    public int ContratosRenovados { get; set; }

    /// <summary>Bloques abiertos en total. Puede ser más de uno por contrato si llevaba meses vencido.</summary>
    public int Bloques { get; set; }

    /// <summary>Incrementos aplicados a la renta en total.</summary>
    public int IncrementosAplicados { get; set; }

    /// <summary>Contratos que otro trabajo renovó a la vez y que este no volvió a tocar.</summary>
    public int OmitidosPorCarrera { get; set; }
}