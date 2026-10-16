using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Facturación periódica de un mes natural y factura de la reserva corta (F3-05,
/// docs/ESPECIFICACION.md §3.8 «Periódica» y §3.4).
/// <para>
/// <b>Simular y ejecutar comparten este mismo cálculo.</b> La simulación no es una vista aparte que
/// pueda desfasarse de la ejecución: es la ejecución sin la última línea, la que escribe. Quien
/// mira ve exactamente los importes, los vencimientos, la serie y el comprobante que se van a
/// tocar, antes de que se toque nada.
/// </para>
/// <para>
/// <b>Lo que sale, sale por el camino de F3-02.</b> Este servicio no tiene contador de números, ni
/// numerador de comprobantes, ni congelado de tipo de cambio: llama a <see cref="ServicioFacturas"/>
/// para crear el borrador, añadir la línea y emitir, con el mismo permiso, la misma transacción y
/// la misma auditoría que una factura tecleada a mano. Por eso no hay una factura periódica que
/// numere distinto de las demás, y por eso la emisión no se puede saltar.
/// </para>
/// <para>
/// <b>Doble ejecución no duplica, y no por cortesía del código.</b> El índice único parcial
/// <c>ix_factura_periodo_unico</c> sobre <c>(contrato_id, periodo_inicio, concepto)</c> es la red:
/// aunque dos ejecuciones se solapen, la segunda se encuentra una violación de unicidad en lugar de
/// una segunda factura. Aquí, que va por delante, la segunda ejecución ni lo intenta y lo dice:
/// «este contrato ya tiene la factura de este mes».
/// </para>
/// </summary>
public sealed class ServicioFacturacionPeriodica(
    AtlasRentDbContext contexto,
    ServicioFacturas facturas,
    IRegistroAlertas alertas,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    TimeProvider reloj)
{
    /// <summary>Auditoría de la ejecución completa, que no es una fila sino un lote.</summary>
    public const string AccionEjecucion = "factura.periodicas.generadas";

    /// <summary>
    /// Concepto de la factura de renta de un mes. Va incluido en la clave de no duplicado, así que
    /// tiene que salir del periodo y solo del periodo: dos ejecuciones del mismo mes tienen que
    /// escribir la misma cadena.
    /// </summary>
    public static string ConceptoRenta(PeriodoFacturacion periodo) =>
        $"Renta {periodo.Inicio:yyyy-MM}";

    /// <summary>
    /// Concepto de la factura de una reserva corta. Lleva el identificador porque la clave de no
    /// duplicado de la factura de reserva no tiene contrato que la distinga: dos reservas cortas que
    /// empiezan el mismo día comparten periodo, así que el concepto es lo único que las separa.
    /// </summary>
    public static string ConceptoReserva(Guid reservaId) => $"Reserva corta {reservaId:D}";

    /// <summary>Clave de la alerta de «este mes no está facturado» (F3-05, §3.16).</summary>
    public static string ClaveAlertaSinFacturar(Guid contratoId, DateOnly periodoInicio) =>
        $"factura-sin-facturar:{contratoId:D}:{periodoInicio:yyyy-MM}";

    // ------------------------------------------------------------------ simulación

    /// <summary>
    /// Lo que se va a crear si se ejecuta ahora. No escribe nada: ni facturas, ni series, ni
    /// alertas. Solo lee, que es lo que la hace segura de enseñar antes de ejecutar.
    /// </summary>
    public async Task<SimulacionPeriodicaRespuesta> SimularAsync(
        SimulacionPeriodicaPeticion peticion,
        CancellationToken ct)
    {
        var calculo = await CalcularAsync(peticion.Periodo, peticion.SerieId, peticion.ContratosIds, ct);

        var items = calculo.Items.Select(i => AItem(calculo, i)).ToList();

        return new SimulacionPeriodicaRespuesta(
            peticion.Periodo,
            calculo.Periodo.Inicio,
            calculo.Periodo.Fin,
            calculo.Periodo.DiasDelMes,
            calculo.Serie.Id,
            calculo.Serie.Numero,
            calculo.Serie.Tipo.ToString(),
            calculo.Serie.GeneraComprobante,
            Redondeo.Importe(items.Where(i => i.SeEmite).Sum(i => i.Importe)),
            items);
    }

    // ------------------------------------------------------------------ ejecución

    /// <summary>
    /// Emite las facturas de renta del mes. La emisión va dentro del cálculo de la simulación, de
    /// modo que lo que sale es exactamente lo que se enseñó.
    /// </summary>
    public async Task<GeneracionPeriodicaRespuesta> GenerarAsync(
        GeneracionPeriodicaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var calculo = await CalcularAsync(peticion.Periodo, peticion.SerieId, peticion.ContratosIds, ct);

        var emitidas = new List<FacturaEmitida>();
        var omitidas = new List<ContratoOmitido>();

        // El tipo de retención por defecto se busca una vez y no por contrato (§3.9): el catálogo
        // tiene un único tipo marcado como defecto, y decidirlo por factura daría resultados
        // distintos a mitad del mismo lote.
        var tipoRetencionDefecto = await contexto.TipoRetencion
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.EsDefecto && t.Activo, ct);

        foreach (var item in calculo.Items)
        {
            if (item.MotivoOmision is not null)
            {
                omitidas.Add(new ContratoOmitido(item.Contrato.Id, item.MotivoOmision, null, null));
                continue;
            }

            if (item.FacturaExistente is not null)
            {
                omitidas.Add(new ContratoOmitido(
                    item.Contrato.Id,
                    $"El contrato ya tiene la factura de {calculo.Periodo.Inicio:yyyy-MM}"
                        + $" (número {item.FacturaExistente.Numero?.ToString() ?? "sin número"}).",
                    item.FacturaExistente.Id,
                    item.FacturaExistente.Numero));
                continue;
            }

            var emitida = await EmitirRentaAsync(calculo, item, tipoRetencionDefecto?.Id, usuario, ip, ct);

            emitidas.Add(emitida);
        }

        var respuesta = new GeneracionPeriodicaRespuesta(
            peticion.Periodo,
            calculo.Periodo.Inicio,
            calculo.Periodo.Fin,
            calculo.Serie.Id,
            calculo.Serie.Numero,
            calculo.Serie.GeneraComprobante,
            peticion.ContratosIds is not null,
            emitidas.Count,
            omitidas.Count,
            Redondeo.Importe(emitidas.Sum(e => e.Importe)),
            emitidas,
            omitidas);

        await CerrarEjecucionAsync(calculo, respuesta, peticion.ContratosIds is not null, usuario, ip, mutacion, ct);

        return respuesta;
    }

    // ------------------------------------------------------------------ reserva corta

    /// <summary>
    /// Factura de una reserva corta (§3.4: «al confirmar la reserva corta se crea la factura»).
    /// <para>
    /// Se emite siempre. La especificación dice «borrador o emitida según rol», y en esta
    /// arquitectura el rol decide igual: el endpoint exige <c>factura.emitir</c>, así que quien llega
    /// hasta aquí puede emitir y quien no puede, se queda sin facturar la reserva. Un borrador que
    /// nadie emite es una reserva «sin cobrar» que no lo dice en ninguna parte.
    /// </para>
    /// <para>
    /// <b>El anticipo no se toca.</b> D10 lo deja como nota de la reserva y no como cobro: aquí no se
    /// crea ningún cobro ni se asigna nada a la factura, que es lo único que garantiza que no se
    /// cuente dos veces. Lo que se devuelve es el aviso que §3.4 pide al crear la factura, y el
    /// indicador «sin cobrar» lo saca el pendiente de los cobros de F3-06.
    /// </para>
    /// </summary>
    public async Task<FacturaReservaRespuesta> FacturarReservaAsync(
        Guid reservaId,
        FacturaReservaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var reserva = await contexto.Reserva
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == reservaId, ct)
            ?? throw new KeyNotFoundException($"La reserva '{reservaId:D}' no existe.");

        if (reserva.Tipo != TipoReserva.Corta || !reserva.Activo)
        {
            throw new InvalidOperationException(
                "Solo una reserva corta activa genera su propia factura; la mensual se factura por "
                    + "contrato y la reserva cancelada no se factura.");
        }

        if (reserva.ClienteId is not { } clienteId)
        {
            throw new InvalidOperationException(
                "La reserva corta necesita un cliente: sin él no hay a quién facturar.");
        }

        var serie = await contexto.Serie.AsNoTracking().SingleOrDefaultAsync(s => s.Id == peticion.SerieId, ct)
            ?? throw new KeyNotFoundException($"La serie '{peticion.SerieId:D}' no existe.");

        if (!serie.Activa)
        {
            throw new InvalidOperationException($"La serie {serie.Numero} está desactivada y no emite.");
        }

        if (serie.Moneda is { } fija && fija != reserva.Moneda)
        {
            throw new InvalidOperationException(
                $"La serie {serie.Numero} está fijada a {fija} y esta reserva se cobra en {reserva.Moneda} (D7).");
        }

        var concepto = ConceptoReserva(reservaId);
        var dias = await contexto.ReservaDia
            .AsNoTracking()
            .Where(d => d.ReservaId == reservaId)
            .Select(d => d.Fecha)
            .ToListAsync(ct);

        var periodo = new PeriodoFacturacion(
            dias.Count > 0 ? dias.Min() : reserva.EntradaPrevista,
            dias.Count > 0 ? dias.Max() : reserva.SalidaPrevista ?? reserva.EntradaPrevista);

        var yaExiste = await contexto.Factura.AsNoTracking()
            .Where(f => f.Concepto == concepto && f.Estado != EstadoFactura.Anulada)
            .Select(f => new { f.Id, f.Numero })
            .SingleOrDefaultAsync(ct);

        if (yaExiste is not null)
        {
            throw new InvalidOperationException(
                $"Esta reserva corta ya tiene su factura (número {yaExiste.Numero?.ToString() ?? "sin número"}).");
        }

        var factura = await facturas.CrearAsync(
            new FacturaPeticion
            {
                ClienteId = clienteId,
                UnidadId = reserva.UnidadId,
                ContratoId = null,
                SerieId = serie.Id,
                Moneda = reserva.Moneda,
                // §3.4: la reserva corta se cobra antes de empezarla, así que vence el día en que
                // empieza. La salida no se usa porque en un medio día o en un día completo no hay
                // ninguna venta que se cobre más tarde.
                FechaVencimiento = reserva.EntradaPrevista,
                Concepto = concepto,
            },
            usuario,
            ip,
            // La clave de idempotencia se guarda en la emisión, que es la última escritura: si se
            // guardara en el alta, el reintento devolvería el borrador y no la factura emitida.
            null,
            ct,
            periodo);

        await facturas.AgregarLineaAsync(
            factura.Id,
            new FacturaLineaPeticion
            {
                Tipo = TipoLineaFactura.Servicio,
                Concepto = $"Reserva corta del {reserva.EntradaPrevista:dd/MM/yyyy}",
                Cantidad = 1m,
                PrecioUnitario = reserva.Importe,
                TipoItbisId = null,
            },
            usuario,
            ip,
            null,
            ct);

        var emitida = await facturas.EmitirAsync(factura.Id, usuario, ip, mutacion, ct);

        return new FacturaReservaRespuesta(
            emitida.Id,
            reservaId,
            emitida.Numero,
            emitida.Total,
            emitida.Moneda,
            emitida.FechaVencimiento,
            AvisoAnticipo(reserva));
    }

    /// <summary>
    /// Aviso de §3.4 al crear la factura de una reserva corta. Sale de la nota del anticipo, que es
    /// lo único que hay: TODO(A#) la especificación pide también su fecha y la reserva no la
    /// guarda, así que el aviso dice el importe y la moneda y nada más hasta que exista el campo.
    /// </summary>
    private static string? AvisoAnticipo(Reserva reserva) =>
        reserva.AnticipoImporte is not { } importe
            ? null
            : $"Anticipo anotado: {reserva.Moneda} {Redondeo.Importe(importe)}"
                + (string.IsNullOrWhiteSpace(reserva.AnticipoNota) ? string.Empty : $" ({reserva.AnticipoNota})");

    // ------------------------------------------------------------------ cálculo

    /// <summary>
    /// Candidatos del mes con su importe, su vencimiento y su motivo de omitirse. Es lo que
    /// comparten la simulación y la ejecución, y por eso no puede haber dos versiones de este cálculo.
    /// </summary>
    private async Task<Calculo> CalcularAsync(
        DateOnly periodo,
        Guid serieId,
        Guid[]? contratosIndicados,
        CancellationToken ct)
    {
        var mes = PeriodoFacturacion.DelMes(periodo);
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        var serie = await contexto.Serie.AsNoTracking().SingleOrDefaultAsync(s => s.Id == serieId, ct)
            ?? throw new KeyNotFoundException($"La serie '{serieId:D}' no existe.");

        // Los candidatos son los contratos vigentes: los que están activos o con baja anunciada y cuyo
        // plazo toca el mes. Una unidad sin contrato vigente no aparece porque el listado parte de
        // los contratos, no de las unidades: no hay nada que facturar de una unidad suelta.
        var consulta = contexto.Contrato
            .AsNoTracking()
            .Where(c => (c.Estado == EstadoContrato.Activo || c.Estado == EstadoContrato.BajaProgramada)
                && c.FechaInicio <= mes.Fin
                && c.FechaFinVigente >= mes.Inicio);

        if (contratosIndicados is not null)
        {
            var pedidos = contratosIndicados.Distinct().ToArray();
            consulta = consulta.Where(c => pedidos.Contains(c.Id));
        }

        var contratos = await consulta
            .Include(c => c.Cliente)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var concepto = ConceptoRenta(mes);

        var existentes = await contexto.Factura
            .AsNoTracking()
            .Where(f => f.PeriodoInicio == mes.Inicio
                && f.Concepto == concepto
                && f.Estado != EstadoFactura.Anulada)
            .Select(f => new FacturaExistente(f.Id, f.ContratoId, f.Numero))
            .ToListAsync(ct);

        var items = new List<Item>(contratos.Count);

        foreach (var contrato in contratos)
        {
            var existente = existentes.FirstOrDefault(f => f.ContratoId == contrato.Id);
            var dias = mes.DiasFacturables(contrato.FechaInicio, contrato.FechaFinVigente);

            items.Add(new Item(
                contrato,
                dias,
                dias == 0 ? 0m : mes.Importe(contrato.Renta, dias),
                dias == 0 ? mes.Fin : mes.Vencimiento(contrato.DiaPago, hoy),
                MotivoOmision(serie, contrato),
                existente));
        }

        return new Calculo(mes, serie, concepto, items);
    }

    /// <summary>
    /// Por qué un contrato no se factura en esta ejecución. Vacío es que se factura. La serie
    /// desactivada y la que no admite la moneda del contrato son los dos motivos que puede tener
    /// la ejecución, y ninguno se resuelve con un 409: son cosas que se ven en la simulación y se
    /// corrigen facturando ese contrato con otra serie o activando la que toca.
    /// <para>
    /// Lo que <b>no</b> sale aquí es el contrato que no cubre ningún día del mes: ese no es candidato
    /// —§3.8 factura los contratos cuyo plazo toca el mes—, así que no aparece ni en la simulación ni
    /// entre los omitidos. Y como el listado exige que el plazo toque el mes, el día facturable nunca
    /// es cero: <see cref="Prorrateo"/> saltaría si lo fuera.
    /// </para>
    /// </summary>
    private static string? MotivoOmision(Serie serie, Contrato contrato) =>
        !serie.Activa ? $"La serie {serie.Numero} está desactivada."
        : serie.Moneda is { } fija && fija != contrato.Moneda
            ? $"La serie {serie.Numero} está fijada a {fija} y este contrato está en {contrato.Moneda} (D7)."
        : null;

    // ------------------------------------------------------------------ emisión

    /// <summary>
    /// Crea, línea y emite una factura de renta del mes. Los tres pasos son los de F3-02 y cada uno
    /// tiene su propia transacción: una ejecución a medias deja facturas emitidas y contratos sin
    /// facturar, que es un estado que la siguiente ejecución termina sin trabajo, porque el índice
    /// único no vuelve a mirar lo que ya está escrito.
    /// </summary>
    private async Task<FacturaEmitida> EmitirRentaAsync(
        Calculo calculo,
        Item item,
        Guid? tipoRetencionId,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        var contrato = item.Contrato;

        var factura = await facturas.CrearAsync(
            new FacturaPeticion
            {
                ClienteId = contrato.ClienteId,
                UnidadId = contrato.UnidadId,
                ContratoId = contrato.Id,
                SerieId = calculo.Serie.Id,
                Moneda = contrato.Moneda,
                FechaVencimiento = item.Vencimiento,
                Concepto = calculo.Concepto,
            },
            usuario,
            ip,
            null,
            ct,
            calculo.Periodo);

        // La línea lleva la cantidad a uno y el importe ya prorrateado en el precio: el prorrateo se
        // calcula una vez, en el dominio, y no se vuelve a repartir en la factura. Escribir los días
        // y la renta en cantidad y precio daría un importe redondeado dos veces.
        await facturas.AgregarLineaAsync(
            factura.Id,
            new FacturaLineaPeticion
            {
                Tipo = TipoLineaFactura.Renta,
                Concepto = LineaConcepto(calculo.Periodo, item),
                Cantidad = 1m,
                PrecioUnitario = item.Importe,
                // TODO(A#) ITBIS de la renta: §3.8 dice que sale «del tipo por defecto de la unidad o
                // del contrato» y ni la unidad ni el contrato tienen ese campo todavía. Cuando lo
                // tengan, esta línea lo lleva; hasta entonces la renta periódica va sin impuesto.
                TipoItbisId = null,
            },
            usuario,
            ip,
            null,
            ct);

        // §3.9: si el cliente es agente de retención, la factura periódica también retiene, con el
        // tipo que la plataforma tiene marcado por defecto. No es una excepción de la factura manual
        // y dejar fuera a estos clientes sería una diferencia invisible entre dos facturas iguales.
        if (tipoRetencionId is { } tipo && contrato.Cliente is { AgenteRetencion: true })
        {
            await AgregarRetencionAsync(factura.Id, tipo, usuario, ip, ct);
        }

        var emitida = await facturas.EmitirAsync(factura.Id, usuario, ip, null, ct);

        // El comprobante se lee de la base y no del DTO: es lo que sale de verdad, y la simulación
        // ya había dicho que la serie lo genera. Las facturas que no lo llevan lo devuelven nulo.
        var comprobante = await contexto.ComprobanteFiscal
            .AsNoTracking()
            .Where(c => c.FacturaId == emitida.Id)
            .Select(c => new { c.Id, c.Numero })
            .SingleOrDefaultAsync(ct);

        return new FacturaEmitida(
            emitida.Id,
            contrato.Id,
            emitida.Numero,
            comprobante?.Id,
            comprobante?.Numero,
            emitida.Total,
            emitida.FechaVencimiento);
    }

    /// <summary>
    /// Línea de retención de §3.9 con el tipo que la plataforma tiene marcado por defecto. El importe
    /// y la base los calcula el servicio de facturas, igual que en la factura tecleada a mano: aquí
    /// solo se dice qué tipo se aplica y a qué factura.
    /// </summary>
    private Task AgregarRetencionAsync(
        Guid facturaId,
        Guid tipoRetencionId,
        Guid usuario,
        string ip,
        CancellationToken ct) =>
        facturas.AgregarLineaAsync(
            facturaId,
            new FacturaLineaPeticion
            {
                Tipo = TipoLineaFactura.Retencion,
                Concepto = "Retención",
                TipoRetencionId = tipoRetencionId,
            },
            usuario,
            ip,
            null,
            ct);

    /// <summary>
    /// Concepto de la línea de renta: el mes y, cuando el mes no está entero, los días que entran.
    /// Los días van escritos porque el importe prorrateado no se explica solo y quien lee la línea
    /// en el PDF tiene que poder ver por qué no es la renta completa.
    /// </summary>
    private static string LineaConcepto(PeriodoFacturacion periodo, Item item) =>
        item.DiasFacturables >= periodo.DiasDelMes
            ? $"Renta {periodo.Inicio:MM/yyyy}"
            : $"Renta {periodo.Inicio:MM/yyyy} ({item.DiasFacturables} días)";

    // ------------------------------------------------------------------ cierre

    /// <summary>
    /// Cierra la ejecución: quién la generó, de qué mes, cuántas facturas salieron, cuáles se
    /// saltaron y por qué, y con qué IP. Se escribe al final y en su propia transacción, y dice
    /// además si los contratos venían de una lista (lo que se vio en la simulación) o si se
    /// facturas todos los candidatos sin mirar.
    /// <para>
    /// En esa misma transacción se resuelven las alertas de «periodo sin facturar» de los contratos
    /// que sí salieron: el aviso se cierra porque el problema dejó de estarlo (F2-09), no porque se
    /// borre.
    /// </para>
    /// </summary>
    private async Task CerrarEjecucionAsync(
        Calculo calculo,
        GeneracionPeriodicaRespuesta respuesta,
        bool contratosIndicados,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = AccionEjecucion,
            Entidad = "factura",
            EntidadId = $"periodo:{calculo.Periodo.Inicio:yyyy-MM}",
            UsuarioId = usuario,
            Ip = ip,
            Resultado = "permitido",
            Origen = "api",
            DatosJson = JsonSerializer.Serialize(new
            {
                Periodo = calculo.Periodo.Inicio.ToString("yyyy-MM-dd"),
                PeriodoFin = calculo.Periodo.Fin.ToString("yyyy-MM-dd"),
                Serie = calculo.Serie.Id.ToString("D"),
                SerieNumero = calculo.Serie.Numero,
                ContratosIndicados = contratosIndicados,
                PeriodoVisto = contratosIndicados,
                Creadas = respuesta.Creadas,
                Omitidas = respuesta.Omitidas,
                ImporteEmitido = respuesta.ImporteEmitido,
                Facturas = respuesta.Facturas.Select(f => new { f.FacturaId, f.ContratoId, f.Numero }),
                Omitidos = respuesta.Omitidos.Select(o => new { o.ContratoId, o.Motivo }),
            }),
        }, ct);

        foreach (var factura in respuesta.Facturas)
        {
            await alertas.ResolverAsync(ClaveAlertaSinFacturar(factura.ContratoId, calculo.Periodo.Inicio), ct);
        }

        await idempotencia.RegistrarAsync(usuario, mutacion, respuesta, ct);

        await transaccion.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ forma interna

    private static FacturaItemSimulado AItem(Calculo calculo, Item item) => new(
        item.Contrato.Id,
        item.Contrato.ClienteId,
        item.Contrato.UnidadId,
        item.Contrato.Moneda,
        calculo.Periodo.Inicio,
        calculo.Periodo.Fin,
        calculo.Periodo.DiasDelMes,
        item.DiasFacturables,
        item.Contrato.Renta,
        item.Importe,
        item.DiasFacturables < calculo.Periodo.DiasDelMes,
        item.Vencimiento,
        calculo.Serie.Id,
        calculo.Serie.Numero,
        calculo.Serie.GeneraComprobante,
        item.FacturaExistente is not null,
        item.FacturaExistente?.Id,
        item.FacturaExistente?.Numero,
        item.MotivoOmision,
        null,
        null);

    private sealed record FacturaExistente(Guid Id, Guid? ContratoId, int? Numero);

    private sealed record Calculo(
        PeriodoFacturacion Periodo,
        Serie Serie,
        string Concepto,
        IReadOnlyList<Item> Items);

    private sealed record Item(
        Contrato Contrato,
        int DiasFacturables,
        decimal Importe,
        DateOnly Vencimiento,
        string? MotivoOmision,
        FacturaExistente? FacturaExistente);
}