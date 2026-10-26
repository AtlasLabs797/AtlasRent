using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Fianzas de contrato: alta, cobro, devolución y descuentos (F3-09, docs/ESPECIFICACION.md §3.11 y
/// §3.6).
/// <list type="bullet">
/// <item>
/// <b>Cobrar una fianza no está aquí.</b> Es un <c>cobro</c> normal con una asignación a la fianza
/// (F3-06 y §3.10.1), porque el dinero que entra por la puerta es el mismo que entra por una factura
/// y tiene que salir por los informes de caja (F5) por el mismo camino. Este servicio solo lee lo que
/// ese cobro produjo.
/// </item>
/// <item>
/// <b>Saldo y estado no son columnas.</b> <c>cobrado</c> sale de las asignaciones vivas y el estado
/// lo compone <see cref="CalculoFianza"/>; ver <see cref="Dominio.EstadoFianza"/>.
/// </item>
/// <item>
/// <b>La devolución parcial exige motivo e importe descontado</b>, que es el criterio de aceptación de
/// F3-09 y lo que hace legible un saldo retenido: sin motivo, el dinero que no se devolvió no tiene
/// explicación y no se puede auditar.
/// </item>
/// <item>
/// <b>Nada se borra</b> (AGENTS.md §6): las devoluciones quedan con su motivo y su fecha, y una fianza
/// devuelta no se puede volver a mover.
/// </item>
/// </list>
/// <para>
/// El servicio comprueba el permiso y no solo el endpoint, porque los trabajos de Hangfire llaman a
/// los servicios sin sesión HTTP ni filtro <c>[Accion]</c> (como en F3-02, F3-05 y F3-06).
/// </para>
/// </summary>
public sealed class ServicioFianzas(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>
    /// Tipo de alerta, el nuevo que F3-09 añade para §3.16 ("fianzas pendientes de devolver").
    /// <para>
    /// <b>Los que ya había no encajan.</b> <c>contrato</c> es de contratos sin liquidar (F2-09) y su
    /// clave es el contrato; <c>comprobante</c> es de numeradores y retenciones (F3-03 y F3-04) y su
    /// clave es un papel que hay que pedir a alguien. La fianza es un tercero: es dinero ya cobrado que
    /// hay que devolver y que no depende ni de un papel fiscal ni del estado del contrato.
    /// </para>
    /// </summary>
    public const string TipoAlerta = "fianza";

    /// <summary>
    /// Clave de la alerta de una fianza sin devolver. Lleva la fianza y no el contrato, porque el aviso
    /// es de la fianza: un contrato con dos fianzas y solo una sin devolver tiene un aviso, no dos, y
    /// el contrato solo aparece en el mensaje.
    /// </summary>
    public static string ClaveAlerta(Guid fianzaId) => $"fianza:{fianzaId:D}:sin-devolver";

    private static readonly Dictionary<string, Func<IQueryable<Fianza>, bool, IOrderedQueryable<Fianza>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(f => f.CreatedAt) : q.OrderBy(f => f.CreatedAt),
            ["importeExigido"] = (q, desc) => desc
                ? q.OrderByDescending(f => f.ImporteExigido)
                : q.OrderBy(f => f.ImporteExigido),
            ["contratoId"] = (q, desc) => desc ? q.OrderByDescending(f => f.ContratoId) : q.OrderBy(f => f.ContratoId),
        };

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Listado paginado con filtros de contrato y moneda. Es lo que F3-10 consume para las filas de
    /// fianza de la vista única de cuentas (§3.13), y por eso cada fila trae ya lo exigido, lo cobrado,
    /// lo devuelto, lo retenido y el saldo.
    /// <para>
    /// <b>No se puede filtrar ni ordenar por estado</b>, y no por una manía del estilo: el estado se
    /// deduce de lo cobrado y de la decisión guardada, y filtrar en la base solo dejaría filtrar por
    /// una de las dos mitades. Una fila que entra en <c>estado=Pendiente</c> pero se lee
    /// <c>Parcial</c> es peor que no dejar filtrar. Quien lo necesite lo filtra sobre la lista, que es
    /// lo mismo que hace la interfaz con el resto de estados derivados.
    /// </para>
    /// </summary>
    public async Task<Pagina<FianzaResumen>> ListarAsync(
        ConsultaLista paginacion,
        Guid? contratoId,
        Moneda? moneda,
        CancellationToken ct)
    {
        IQueryable<Fianza> consulta = contexto.Fianza.AsNoTracking();

        if (contratoId is { } filtroContrato)
        {
            consulta = consulta.Where(f => f.ContratoId == filtroContrato);
        }

        if (moneda is { } filtroMoneda)
        {
            consulta = consulta.Where(f => f.Moneda == filtroMoneda);
        }

        var pagina = await ConsultaPaginada.PaginarAsync(
            consulta,
            Ordenes,
            "createdAt",
            paginacion,
            f => f,
            ct);

        var cifras = await CifrasAsync([.. pagina.Data.Select(f => f.Id)], ct);

        return new Pagina<FianzaResumen>(
            [.. pagina.Data.Select(f => AResumen(f, cifras[f.Id]))],
            pagina.Total,
            pagina.Page,
            pagina.PageSize,
            pagina.TotalPages);
    }

    /// <summary>La fianza con sus devoluciones.</summary>
    public async Task<FianzaRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var fianza = await CargarAsync(id, ct);
        var cifras = (await CifrasAsync([id], ct))[id];
        var devoluciones = await DevolucionesAsync(id, ct);

        return new FianzaRespuesta(AResumen(fianza, cifras), [.. devoluciones.Select(ADevolucionRespuesta)]);
    }

    // ---------------------------------------------------------------- escritura

    /// <summary>
    /// Alta de la fianza de un contrato.
    /// <para>
    /// <b>Una fianza por contrato y moneda.</b> Dos del mismo contrato y la misma moneda no se
    /// distinguen en nada que el negocio pueda decir y harían que el saldo —lo único que el cliente
    /// pregunta— se tuviera que sumar a mano. Si el negocio necesita más de una, hay que decidirlo;
    /// mientras tanto el alta de la segunda se rechaza con 409 en vez de aceptarse en silencio.
    /// </para>
    /// </summary>
    public async Task<FianzaRespuesta> AltaAsync(
        FianzaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync($"fianza/{peticion.ContratoId:D}", usuario, ct);

        // 400 y no 409: un importe de cero o un número de fianzas de cero son una petición mal formada,
        // no un conflicto con lo que hay en la base.
        var exigido = Redondeo.Importe(peticion.ImporteExigido);
        if (exigido <= 0m)
        {
            throw new ArgumentException("El importe exigido tiene que ser mayor que cero.", nameof(peticion));
        }

        if (peticion.NumeroFianzas <= 0)
        {
            throw new ArgumentException(
                "El número de fianzas tiene que ser mayor que cero: una fianza de cero fianzas no es una fianza.",
                nameof(peticion));
        }

        var contrato = await contexto.Contrato.FirstOrDefaultAsync(c => c.Id == peticion.ContratoId, ct)
            ?? throw new KeyNotFoundException($"El contrato '{peticion.ContratoId:D}' no existe.");

        var yaExiste = await contexto.Fianza
            .AnyAsync(f => f.ContratoId == contrato.Id && f.Moneda == peticion.Moneda, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException(
                $"El contrato '{contrato.Id:D}' ya tiene una fianza en {peticion.Moneda}. Una fianza por "
                    + "contrato y moneda mientras no se decida que pueda haber más de una.");
        }

        var fianza = new Fianza
        {
            ContratoId = contrato.Id,
            Contrato = contrato,
            ImporteExigido = exigido,
            Moneda = peticion.Moneda,
            NumeroFianzas = peticion.NumeroFianzas,
            // La columna nace sin decisión, que es lo que significa un 1: todavía nadie ha retenido,
            // liquidado ni devuelto esta fianza. El estado que se ve sale de lo cobrado.
            Estado = EstadoFianza.Pendiente,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Fianza, fianza,
            esAlta: true,
            accion: "fianza.creada",
            entidad: "fianza",
            usuario,
            ip,
            mutacion,
            proyectar: f => new FianzaRespuesta(
                AResumen(f, CifraVacia),
                []),
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Devolución de una fianza, entera o en parte (F3-09, §3.11 y §3.6).
    /// <para>
    /// <b>El criterio de aceptación de esta tarea es la devolución parcial: sin
    /// <see cref="FianzaDevolucionPeticion.ImporteDescontado"/> y sin
    /// <see cref="FianzaDevolucionPeticion.Motivo"/> la petición es un 400</b>, con un mensaje que lo
    /// dice. Una devolución parcial sin descuento no es una devolución parcial, y una sin motivo deja
    /// dinero sin explicar.
    /// </para>
    /// <para>
    /// <b>Devolver más de lo cobrado es un 409 con los números</b>, no un 400: es un conflicto con lo
    /// que hay, y el mensaje lleva el saldo y lo pedido para que quien lee sepa cuánto se pasó.
    /// </para>
    /// </summary>
    public async Task<FianzaRespuesta> DevolverAsync(
        Guid id,
        FianzaDevolucionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(DetalleDe(id), usuario, ct);

        var fianza = await CargarAsync(id, ct);

        // §3.11: devuelta entera o retenida a medias, la fianza está cerrada. Volver a moverla es un
        // 409, y no un 400, porque el conflicto es con el estado en que está.
        if (CalculoFianza.Cerrada(fianza.Estado))
        {
            throw new InvalidOperationException(
                $"La fianza del contrato '{fianza.ContratoId:D}' ya está en {fianza.Estado} y no admite más "
                    + "movimientos: no se vuelve a tocar una fianza devuelta.");
        }

        var importe = Redondeo.Importe(peticion.Importe);
        var descontado = Redondeo.Importe(peticion.ImporteDescontado);

        if (importe < 0m || descontado < 0m)
        {
            throw new ArgumentException(
                "Ni lo devuelto ni lo descontado pueden ser negativos: devolver es salir de caja, y lo que "
                    + "sale de caja no entra. Lo que entra se cobra.",
                nameof(peticion));
        }

        var total = Redondeo.Importe(importe + descontado);
        if (total <= 0m)
        {
            throw new ArgumentException(
                "La devolución tiene que devolver algo o retener algo: con los dos importes a cero no sale "
                    + "dinero de la fianza y no hay nada que registrar.",
                nameof(peticion));
        }

        var cifras = (await CifrasAsync([id], ct))[id];
        var saldo = cifras.Saldo;

        if (total > saldo)
        {
            throw new InvalidOperationException(
                $"La devolución pide {total} ({importe} devueltos y {descontado} retenidos) y la fianza del "
                    + $"contrato '{fianza.ContratoId:D}' solo tiene {saldo} de saldo: no se puede devolver más "
                    + "de lo que se ha cobrado.");
        }

        var completa = total == saldo;

        // Aquí el 400 es lo correcto: son reglas de §3.11 sobre cómo tiene que venir la petición, y no
        // dependen de lo que haya en la base. El descuento de la parcial es lo que convierte "devuelvo un
        // poco" en "devuelvo un poco y retengo el resto por algo concreto".
        if (!completa && descontado <= 0m)
        {
            throw new ArgumentException(
                $"La devolución es parcial (quedan {Redondeo.Importe(saldo - total)} de saldo) y una devolución "
                    + "parcial tiene que decir cuánto se retiene: sin importe descontado no se sabe qué pasa con "
                    + "el resto de la fianza.",
                nameof(peticion));
        }

        var motivo = string.IsNullOrWhiteSpace(peticion.Motivo) ? null : peticion.Motivo.Trim();

        if ((!completa || descontado > 0m) && motivo is null)
        {
            throw new ArgumentException(
                "El motivo es obligatorio: la devolución deja saldo retenido o descuenta parte de la fianza, y "
                    + "el dinero que no se devuelve necesita una razón para no devolverse.",
                nameof(peticion));
        }

        var ahora = reloj.GetUtcNow();
        // §3.11: con descuento el dinero no vuelve entero, así que es «Retenida parcial» aunque el
        // total agote el saldo. TODO(A#): confirmar con el responsable que «Devuelta» es solo sin descuento.
        var nuevaDecision = completa && descontado <= 0m ? EstadoFianza.Devuelta : EstadoFianza.RetenidaParcial;

        var devolucion = new FianzaDevolucion
        {
            FianzaId = fianza.Id,
            Fianza = fianza,
            Importe = importe,
            ImporteDescontado = descontado,
            Motivo = motivo,
            Fecha = peticion.Fecha ?? TiempoNegocio.FechaEnZonaNegocio(ahora),
            // Se fija aquí y no se deja que lo ponga GuardadoAuditado porque la respuesta se arma antes
            // de que él escriba: sin esto, la devolución que devuelve el endpoint saldría en el historial
            // con la fecha de creación en cero. La marca que queda en la tabla la vuelve a poner él con
            // su propio `reloj.GetUtcNow()`, que es el mismo instante con microsegundos de diferencia.
            CreatedAt = ahora,
            CreatedBy = usuario,
        };

        // Las cifras de después, para la respuesta. Se calculan aquí y no en la proyección porque el
        // contrato de GuardadoAuditado solo le pasa la fila que se acaba de guardar, y porque volver a
        // consultar ya guardada sería una segunda lectura de lo que este mismo método acaba de decidir.
        var despues = new Cifras(
            cifras.Cobrado,
            Redondeo.Importe(cifras.Devuelto + importe),
            Redondeo.Importe(cifras.Descontado + descontado));

        // La respuesta incluye la devolución que se acaba de crear, y esa fila todavía no está en la
        // base cuando corre la proyección. Por eso la lista se arma aquí, sumando la pendiente, y no
        // dentro de `proyectar`: leerla de la base saldría sin ella, y el cliente no vería el
        // movimiento que acaba de confirmar.
        var historial = await DevolucionesAsync(id, ct, devolucion);

        var esperado = new Fianza
        {
            Id = fianza.Id,
            ContratoId = fianza.ContratoId,
            ImporteExigido = fianza.ImporteExigido,
            Moneda = fianza.Moneda,
            NumeroFianzas = fianza.NumeroFianzas,
            Estado = nuevaDecision,
            RetenidaAt = fianza.RetenidaAt,
            EnLiquidacionAt = fianza.EnLiquidacionAt,
            DevueltaAt = ahora,
            CreatedAt = fianza.CreatedAt,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.FianzaDevolucion, devolucion,
            esAlta: true,
            accion: "fianza.devuelta",
            entidad: "fianza_devolucion",
            usuario,
            ip,
            mutacion,
            proyectar: _ => new FianzaRespuesta(
                AResumen(esperado, despues),
                [.. historial.Select(ADevolucionRespuesta)]),
            datosAuditoria: d => new
            {
                Devolucion = d.Id.ToString("D"),
                Fianza = fianza.Id.ToString("D"),
                Contrato = fianza.ContratoId.ToString("D"),
                d.Importe,
                d.ImporteDescontado,
                Moneda = fianza.Moneda.ToString(),
                d.Motivo,
                Fecha = d.Fecha.ToString("yyyy-MM-dd"),
                Estado = nuevaDecision.ToString(),
            },
            antesDeGuardar: () =>
            {
                // La devolución cuelga de la navegación de la fianza, que ya está guardada: sale en el
                // mismo INSERT que la decisión de estado, en la misma transacción.
                fianza.Devoluciones.Add(devolucion);

                fianza.Estado = nuevaDecision;
                fianza.DevueltaAt = ahora;
                fianza.UpdatedAt = ahora;
                fianza.UpdatedBy = usuario;

                return Task.CompletedTask;
            },
            // La devolución es lo que quita el problema del aviso, así que se cierra aquí y no cuando
            // alguien marca la alerta: la marca es consecuencia, no causa.
            despuesDeGuardar: () => alertas.ResolverAsync(ClaveAlerta(id), ct),
            ct: ct);
    }

    /// <summary>
    /// Retener la fianza entera (§3.11, §3.6). No mueve dinero: no hay devolución ni descuento, y por
    /// eso la fila de <c>fianza_devolucion</c> no se toca. Solo se guarda la decisión y su fecha.
    /// </summary>
    public Task<FianzaRespuesta> RetenerAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        CambiarEstadoAsync(id, EstadoFianza.Retenida, "fianza.retenida", usuario, ip, mutacion, ct);

    /// <summary>
    /// Poner la fianza en liquidación (F3-09, §3.11), que es ejecutar lo que la liquidación del
    /// contrato decidió (F2-09 y <see cref="DecisionFianza"/>).
    /// </summary>
    public Task<FianzaRespuesta> LiquidarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        CambiarEstadoAsync(id, EstadoFianza.EnLiquidacion, "fianza.en_liquidacion", usuario, ip, mutacion, ct);

    /// <summary>
    /// Los dos cambios de estado que no mueven dinero. Comparten todo menos el destino, y el destino es
    /// la única diferencia que importa: retenida se queda con la fianza, en liquidación la está
    /// ejecutando alguien.
    /// </summary>
    private async Task<FianzaRespuesta> CambiarEstadoAsync(
        Guid id,
        EstadoFianza destino,
        string accion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(DetalleDe(id), usuario, ct);

        var fianza = await CargarAsync(id, ct);

        // Cada transición se valida contra el estado actual, no contra una lista de estados posibles
        // (§3.11). Devuelta no vuelve a moverse, y una fianza ya liquidada no se vuelve a retener: las
        // dos cosas se rechazan con 409 diciendo en qué estado está.
        if (CalculoFianza.Cerrada(fianza.Estado))
        {
            throw new InvalidOperationException(
                $"La fianza del contrato '{fianza.ContratoId:D}' está en {fianza.Estado} y no admite más "
                    + "movimientos.");
        }

        if (fianza.Estado == destino)
        {
            throw new InvalidOperationException(
                $"La fianza del contrato '{fianza.ContratoId:D}' ya está en {destino}.");
        }

        if (destino is EstadoFianza.Retenida && fianza.Estado == EstadoFianza.EnLiquidacion)
        {
            throw new InvalidOperationException(
                $"La fianza del contrato '{fianza.ContratoId:D}' está en liquidación y no se puede volver a "
                    + "retener: una liquidación que ya empezó no se deshace desde aquí.");
        }

        var cifras = (await CifrasAsync([id], ct))[id];
        var ahora = reloj.GetUtcNow();

        if (destino == EstadoFianza.Retenida)
        {
            fianza.RetenidaAt = ahora;
        }
        else
        {
            fianza.EnLiquidacionAt = ahora;
        }

        fianza.Estado = destino;

        var devoluciones = await DevolucionesAsync(id, ct);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Fianza, fianza,
            esAlta: false,
            accion: accion,
            entidad: "fianza",
            usuario,
            ip,
            mutacion,
            proyectar: f => new FianzaRespuesta(AResumen(f, cifras), [.. devoluciones.Select(ADevolucionRespuesta)]),
            datosAuditoria: DatosAuditoria,
            // Una fianza en liquidación ya no es un asunto pendiente de §3.16, así que el aviso se
            // cierra en el mismo commit en que se toma la decisión.
            despuesDeGuardar: () => alertas.ResolverAsync(ClaveAlerta(id), ct),
            ct: ct);
    }

    // ---------------------------------------------------------------- cargas y permisos

    private async Task<Fianza> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Fianza
            .Include(f => f.Contrato)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
        ?? throw new KeyNotFoundException($"La fianza '{id:D}' no existe.");

    private static string DetalleDe(Guid id) => $"fianza/{id:D}";

    /// <summary>
    /// Segunda puerta del permiso: el endpoint lo pide con <c>[Accion]</c> y el atributo protege la
    /// entrada por HTTP y nada más. Un trabajo entra por el servicio, y el intento denegado se audita
    /// antes de lanzar, para que quede constancia de quién lo intentó y no pudo.
    /// </summary>
    private async Task ExigirAsync(string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.FianzaMover, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(Acciones.FianzaMover, recurso, ct);

        throw new PermisoDenegadoException(Acciones.FianzaMover, recurso);
    }

    // ---------------------------------------------------------------- cifras

    private static Cifras CifraVacia => new(0m, 0m, 0m);

    /// <summary>
    /// Cuánto se ha cobrado de cada fianza y cuánto se ha devuelto y retenido, en tres consultas
    /// agrupadas para toda la página. Es el mismo patrón que <c>CobradoPorFacturaAsync</c> de F3-06 y
    /// por el mismo motivo: una vista de cuentas no puede ser una consulta por fila (§3.13).
    /// <para>
    /// Las asignaciones de un cobro anulado no cuentan, que es lo mismo que haberlas borrado sin
    /// borrarlas.
    /// </para>
    /// <para>
    /// Desde F3-10 es <b>pública</b>: la vista única de cuentas (§3.13) mete fianzas y facturas en la
    /// misma respuesta y necesita estas mismas cifras para no volver a sumar los cobros por su cuenta.
    /// </para>
    /// </summary>
    public async Task<Dictionary<Guid, Cifras>> CifrasAsync(
        IReadOnlyCollection<Guid> fianzaIds,
        CancellationToken ct)
    {
        var cifras = fianzaIds.ToDictionary(id => id, _ => CifraVacia);

        if (fianzaIds.Count == 0)
        {
            return cifras;
        }

        var ids = fianzaIds.ToArray();

        var cobrados = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.Vigente
                && a.FianzaId != null
                && ids.Contains(a.FianzaId.Value)
                && a.Cobro!.Estado == EstadoCobro.Registrado)
            .GroupBy(a => a.FianzaId!.Value)
            .Select(g => new { FianzaId = g.Key, Cobrado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FianzaId, x => x.Cobrado, ct);

        var devoluciones = await contexto.FianzaDevolucion.AsNoTracking()
            .Where(d => ids.Contains(d.FianzaId))
            .GroupBy(d => d.FianzaId)
            .Select(g => new
            {
                FianzaId = g.Key,
                Devuelto = g.Sum(d => d.Importe),
                Descontado = g.Sum(d => d.ImporteDescontado),
            })
            .ToDictionaryAsync(x => x.FianzaId, ct);

        foreach (var id in ids)
        {
            var cobrado = cobrados.GetValueOrDefault(id, 0m);
            cifras[id] = new Cifras(
                Redondeo.Importe(cobrado),
                Redondeo.Importe(devoluciones.GetValueOrDefault(id)?.Devuelto ?? 0m),
                Redondeo.Importe(devoluciones.GetValueOrDefault(id)?.Descontado ?? 0m));
        }

        return cifras;
    }

    /// <summary>
    /// Las devoluciones de una fianza, de más antigua a más reciente. Sin filas pendientes: se usa
    /// desde <see cref="ListarAsync"/> y desde el cambio de estado, donde lo que hay en la base ya
    /// está escrito.
    /// </summary>
    private Task<List<FianzaDevolucion>> DevolucionesAsync(Guid fianzaId, CancellationToken ct) =>
        DevolucionesAsync(fianzaId, ct, _incluirPendiente: null);

    /// <summary>
    /// Las devoluciones de una fianza, incluyendo la que este mismo método está a punto de guardar. Va
    /// aparte porque la proyección de <c>GuardadoAuditado</c> corre antes del commit y la fila nueva
    /// todavía no está en la base: leerla de la base saldría con el estado de antes, que es justo lo
    /// que la respuesta no puede decir.
    /// </summary>
    private async Task<List<FianzaDevolucion>> DevolucionesAsync(
        Guid fianzaId,
        CancellationToken ct,
        FianzaDevolucion? _incluirPendiente)
    {
        var devoluciones = await contexto.FianzaDevolucion.AsNoTracking()
            .Where(d => d.FianzaId == fianzaId)
            .OrderBy(d => d.Fecha)
            .ThenBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        if (_incluirPendiente is not null)
        {
            devoluciones.Add(_incluirPendiente);
        }

        return devoluciones;
    }

    // ---------------------------------------------------------------- proyecciones

    private static FianzaResumen AResumen(Fianza f, Cifras cifras) => new(
        f.Id,
        f.ContratoId,
        Redondeo.Importe(f.ImporteExigido),
        f.Moneda,
        f.NumeroFianzas,
        cifras.Cobrado,
        cifras.Devuelto,
        cifras.Descontado,
        cifras.Saldo,
        CalculoFianza.Estado(f.Estado, cifras.Cobrado),
        f.RetenidaAt,
        f.EnLiquidacionAt,
        f.DevueltaAt,
        f.CreatedAt);

    private static FianzaDevolucionRespuesta ADevolucionRespuesta(FianzaDevolucion d) => new(
        d.Id,
        d.FianzaId,
        Redondeo.Importe(d.Importe),
        Redondeo.Importe(d.ImporteDescontado),
        d.Motivo,
        d.Fecha,
        d.CreatedAt);

    /// <summary>
    /// Lo que va en la auditoría firmada. Los identificadores van como texto y las fechas como día de
    /// negocio, como en el resto.
    /// </summary>
    private static object DatosAuditoria(Fianza f) => new
    {
        f.Id,
        Contrato = f.ContratoId.ToString("D"),
        f.ImporteExigido,
        Moneda = f.Moneda.ToString(),
        f.NumeroFianzas,
        Estado = f.Estado.ToString(),
    };

    /// <summary>
    /// Las tres sumas de una fianza. Es un tipo propio y no tres <c>decimal</c> sueltos porque en
    /// <see cref="CifrasAsync"/> se devuelven juntos y en la respuesta se usan juntos: separarlos
    /// sería el momento en que uno se cambia y los otros dos no.
    /// </summary>
    public sealed record Cifras(decimal Cobrado, decimal Devuelto, decimal Descontado)
    {
        /// <summary>Lo que queda por devolver. Nunca negativo porque el servicio no lo deja bajar de cero.</summary>
        public decimal Saldo => CalculoFianza.Saldo(Cobrado, Devuelto, Descontado);
    }
}