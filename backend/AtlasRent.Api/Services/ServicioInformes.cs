using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Informes económicos y dashboard del gerente y el administrador (F5-01,
/// docs/ESPECIFICACION.md §3.15).
/// <para>
/// <b>Los bloques salen de la vista única</b> (<see cref="ServicioCuentas.ListarAsync"/>, F3-10) y no de
/// una segunda consulta con su propia fórmula de dinero. Es la misma razón por la que el estado de
/// cuenta en PDF suma sobre las filas de la vista (§3.13): §3.15 exige que el informe cuadre con la
/// tabla, y dos cálculos de pendiente que un día dicen cosas distintas es exactamente cómo se rompe
/// eso. Los importes salen de la fila; lo único que se consulta aparte es lo que la fila no trae —el
/// cambio congelado, si la serie suma a facturado y el estado del comprobante de retención— y solo para
/// las filas que ya están en el conjunto filtrado.
/// </para>
/// <para>
/// <b>El consolidado en dólares usa el tipo de cambio congelado de cada documento</b> (D9, §3.12), nunca
/// el del día: un informe que se recalcula con el cambio de hoy dice que el mes pasado se facturó otra
/// cantidad. Un documento nunca se convierte (D7); esto solo suma para el informe.
/// </para>
/// <para>
/// <b>El permiso es <c>informe.ver</c></b> y lo pone el endpoint. Aquí no se comprueba otra vez porque
/// todos los caminos de este servicio son HTTP con <c>[Accion]</c>, y una segunda puerta sin trabajo en
/// Hangfire detrás sería una comprobación que no protege de nada.
/// </para>
/// </summary>
public sealed class ServicioInformes(AtlasRentDbContext contexto, ServicioCuentas cuentas, TimeProvider reloj)
{
    private DateOnly Hoy() => TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

    /// <summary>
    /// El resumen económico de §3.15: los ocho bloques y la serie mensual del gráfico.
    /// </summary>
    public async Task<InformeResumenRespuesta> ResumenAsync(FiltroInformes filtro, CancellationToken ct) =>
        await ConstruirResumenAsync(filtro, fechaCorte: null, ct);

    /// <summary>
    /// El dashboard de §3.15: los indicadores de ocupación del día y los mismos bloques del resumen.
    /// </summary>
    /// <param name="fecha">
    /// El día del corte. Por defecto es hoy, y es también la fecha de corte de lo vencido, para que el
    /// dashboard sea el resumen de «ahora» y no el de otro día distinto.
    /// </param>
    public async Task<DashboardRespuesta> DashboardAsync(
        FiltroInformes filtro,
        DateOnly? fecha,
        CancellationToken ct)
    {
        var dia = fecha ?? Hoy();

        return new DashboardRespuesta(
            dia,
            await IndicadoresDeUnidadesAsync(filtro, dia, ct),
            await ContratosActivosAsync(filtro, dia, ct),
            await ConstruirResumenAsync(filtro, dia, ct));
    }

    // ---------------------------------------------------------------- resumen

    private async Task<InformeResumenRespuesta> ConstruirResumenAsync(
        FiltroInformes filtro,
        DateOnly? fechaCorte,
        CancellationToken ct)
    {
        var corte = fechaCorte ?? filtro.Hasta ?? Hoy();
        var filas = await FilasDelInformeAsync(filtro, ct);

        var facturas = filas.Where(f => f.Tipo == TipoFilaCuenta.Factura).ToArray();
        var proformas = filas.Where(f => f.Tipo == TipoFilaCuenta.Proforma).ToArray();
        var fianzas = filas.Where(f => f.Tipo == TipoFilaCuenta.Fianza).ToArray();

        // Lo que la fila no trae y §3.15 necesita: el cambio congelado del documento (D9) y si su serie
        // suma a facturado (§3.7). Se pregunta solo por las facturas que ya están en el conjunto
        // filtrado, así que esto no es una consulta de facturas alternativa: es la metadata de las
        // mismas.
        var documentos = await DocumentosAsync([.. facturas.Select(f => f.Id).Concat(proformas.Select(f => f.Id))], ct);

        // Proformado: lo presupuestado y no convertido. Va aparte y es informativo porque una proforma
        // no genera deuda (§3.8): sumarla al facturado sería inventar una venta.
        var proformado = new Acumulador();

        foreach (var fila in proformas)
        {
            proformado.Agregar(fila.Moneda, fila.Total ?? 0m, documentos.GetValueOrDefault(fila.Id)?.TipoCambioValor);
        }

        var facturadas = facturas
            .Where(f => !EstaAnulada(f) && documentos.GetValueOrDefault(f.Id)?.SumaAFacturado == true)
            .ToArray();

        var facturadasIds = facturadas.Select(f => f.Id).ToHashSet();

        var facturado = new Acumulador();
        var cobradoFacturas = new Acumulador();
        var retenido = new Acumulador();
        var retenidoConComprobante = new Acumulador();
        var retenidoSinComprobante = new Acumulador();
        var pendienteFacturas = new Acumulador();
        var vencido = new Acumulador();

        var comprobantes = await ComprobantesDeRetencionAsync([.. facturas.Select(f => f.Id)], ct);

        foreach (var fila in facturas)
        {
            var documento = documentos.GetValueOrDefault(fila.Id);
            var cambio = documento?.TipoCambioValor;

            if (facturadasIds.Contains(fila.Id))
            {
                facturado.Agregar(fila.Moneda, fila.Total ?? 0m, cambio);
            }

            // La retención va con el comprobante de F3-04 o sin él: es lo que decide si el gerente ya
            // tiene lo que le prometieron o todavía no.
            if (fila.Retencion is { } retencion && retencion > 0m)
            {
                var destino = comprobantes.GetValueOrDefault(fila.Id) == EstadoComprobanteRetencion.Recibido
                    ? retenidoConComprobante
                    : retenidoSinComprobante;

                destino.Agregar(fila.Moneda, retencion, cambio);
                retenido.Sumar(destino);
            }

            cobradoFacturas.Agregar(fila.Moneda, fila.Cobrado ?? 0m, cambio);

            if (fila.Pendiente is { } pendiente && pendiente > 0m)
            {
                pendienteFacturas.Agregar(fila.Moneda, pendiente, cambio);

                // Vencido es el pendiente con el vencimiento pasado en la fecha de corte, no la
                // bandera de la fila: esa está atada al hoy de la aplicación y el corte puede ser otro.
                // El vencimiento del mismo día no está vencido (§3.8).
                if (fila.Vencimiento is { } vencimiento && vencimiento < corte)
                {
                    vencido.Agregar(fila.Moneda, pendiente, cambio);
                }
            }
        }

        // Las fianzas. No hay cambio congelado en la fianza —no tiene columna—, así que el de una fianza
        // en pesos es el del último cobro vigente que la pagó; sin ese cobro no se puede consolidar y
        // el documento se cuenta en `documentosSinTipoCambio`.
        var cambiosDeFianza = await TiposDeCambioDeFianzaAsync([.. fianzas.Select(f => f.Id)], ct);

        var cobradoFianzas = new Acumulador();
        var pendienteFianzas = new Acumulador();
        var fianzasRetenidas = new Acumulador();

        foreach (var fila in fianzas)
        {
            var cambio = cambiosDeFianza.GetValueOrDefault(fila.Id);

            cobradoFianzas.Agregar(fila.Moneda, fila.Cobrado ?? 0m, cambio);
            pendienteFianzas.Agregar(fila.Moneda, fila.Pendiente ?? 0m, cambio);

            // Retenidas (§3.15): cobradas y con saldo por devolver. Una fianza devuelta tiene el saldo a
            // cero, así que el saldo ya hace de filtro y no hace falta mirar el estado guardado.
            if (fila.Cobrado is > 0m && fila.SaldoADevolver is > 0m)
            {
                fianzasRetenidas.Agregar(fila.Moneda, fila.SaldoADevolver.Value, cambio);
            }
        }

        var saldoAFavor = await SaldoAFavorAsync(filas, ct);
        var diferencias = await DiferenciasDeCambioAsync(facturas, ct);
        var serie = await SerieMensualAsync(facturadas, facturas, fianzas, documentos, ct);

        // El saldo a favor no tiene fecha de negocio: es una fila por contrato y moneda, no un
        // movimiento (Data/SaldoAFavor.cs). Sus únicas fechas son de auditoría, y además cambian cada
        // vez que otro cobro lo consume, así que no acotan nada. Con rango de fechas, sumarlo mezclaría
        // los movimientos del periodo con una foto de hoy y el «cobrado total» dejaría de ser la suma
        // de sus tres líneas; ahí queda fuera y se dice en la respuesta. Sin rango el informe es del
        // histórico entero, y ahí sí es la foto del momento.
        var saldo = saldoAFavor.Importe();
        var saldoFueraDelRango =
            (filtro.Desde is not null || filtro.Hasta is not null) && (saldo.Usd != 0m || saldo.Dop != 0m);

        var cobradoTotal = new Acumulador();
        cobradoTotal.Sumar(cobradoFacturas);
        cobradoTotal.Sumar(cobradoFianzas);

        if (!saldoFueraDelRango)
        {
            cobradoTotal.Sumar(saldoAFavor);
        }

        var pendienteTotal = new Acumulador();
        pendienteTotal.Sumar(pendienteFacturas);
        pendienteTotal.Sumar(pendienteFianzas);

        return new InformeResumenRespuesta(
            filtro.Desde,
            filtro.Hasta,
            corte,
            proformado.Importe(),
            facturado.Importe(),
            new BloqueCobradoInforme(
                cobradoFacturas.Importe(),
                cobradoFianzas.Importe(),
                saldo,
                cobradoTotal.Importe()),
            new BloqueRetenidoInforme(
                retenidoConComprobante.Importe(),
                retenidoSinComprobante.Importe(),
                retenido.Importe()),
            new BloquePendienteInforme(
                pendienteFacturas.Importe(),
                pendienteFianzas.Importe(),
                pendienteTotal.Importe()),
            vencido.Importe(),
            fianzasRetenidas.Importe(),
            diferencias.Importe(),
            serie,
            proformado.SinTipoCambio + facturado.SinTipoCambio + cobradoFacturas.SinTipoCambio
                + retenido.SinTipoCambio + pendienteFacturas.SinTipoCambio + vencido.SinTipoCambio
                + fianzasRetenidas.SinTipoCambio + diferencias.SinTipoCambio + saldoAFavor.SinTipoCambio,
            saldoFueraDelRango);
    }

    /// <summary>
    /// Las filas de la vista única con los filtros del informe, todas y no una página.
    /// <para>
    /// Se piden por páginas porque <see cref="ServicioCuentas.ListarAsync"/> pagina, y se acumulan
    /// porque un informe que solo mirase los primeros 200 documentos daría la mitad de la respuesta sin
    /// decirlo: un total recortado en silencio es peor que uno que no se genera.
    /// </para>
    /// <para>
    /// <b>F5-02 la usa también la exportación</b> (<see cref="ServicioExportacionInformes"/>), y por eso
    /// no es privada: la hoja tiene que salir de las mismas filas que el resumen, o el Excel y la
    /// tabla empezarían a decir cosas distintas.
    /// </para>
    /// </summary>
    internal async Task<List<CuentaFila>> FilasDelInformeAsync(FiltroInformes filtro, CancellationToken ct)
    {
        var filas = new List<CuentaFila>();
        var total = 0;

        for (var pagina = 1; ; pagina++)
        {
            var vista = await cuentas.ListarAsync(
                new ConsultaLista(pagina, ConsultaLista.PageSizeMaximo, SortBy: null, ConsultaLista.OrdenAscendente),
                anio: null,
                filtro.SerieId,
                filtro.Moneda,
                filtro.Estado,
                estadoCobro: null,
                filtro.ClienteId,
                filtro.UnidadId,
                vencidas: false,
                modificadas: false,
                noEnviadas: false,
                envioFallido: false,
                modificadasTrasEnvio: false,
                incluirProformas: true,
                incluirFianzas: true,
                ct,
                filtro.Desde,
                filtro.Hasta,
                filtro.InmuebleId,
                filtro.PropietariaId,
                filtro.FacturadoraId);

            filas.AddRange(vista.Data);
            total = vista.Total;

            // La segunda condición es la red de seguridad: si una página viniera vacía con documentos
            // pendientes, sin ella el bucle no terminaría nunca.
            if (filas.Count >= total || vista.Data.Count == 0)
            {
                break;
            }
        }

        if (filtro.ContratoId is { } contrato)
        {
            // El contrato no es filtro de la vista única, así que no se puede pasar a
            // `ListarAsync`; se aplica sobre las filas, que sí traen la columna.
            return [.. filas.Where(f => f.ContratoId == contrato)];
        }

        return filas;
    }

    /// <summary>
    /// El cambio congelado y la bandera de serie de las facturas del informe (§3.7 y D9).
    /// </summary>
    private async Task<Dictionary<Guid, DocumentoFactura>> DocumentosAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        var documentos = await contexto.Factura.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new DocumentoFactura(f.Id, f.TipoCambioValor, f.Serie!.SumaAFacturado))
            .ToListAsync(ct);

        return documentos.ToDictionary(d => d.Id);
    }

    /// <summary>
    /// El estado del comprobante de retención de cada factura (§3.9). Lo que no tiene comprobante cuenta
    /// como «sin comprobante»: lo que se retuvo sin que el comprobante haya llegado es justo lo que el
    /// bloque tiene que enseñar.
    /// </summary>
    private async Task<Dictionary<Guid, EstadoComprobanteRetencion>> ComprobantesDeRetencionAsync(
        Guid[] ids,
        CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        var comprobantes = await contexto.ComprobanteRetencion.AsNoTracking()
            .Where(c => ids.Contains(c.FacturaId))
            .Select(c => new { c.FacturaId, c.Estado })
            .ToListAsync(ct);

        return comprobantes.ToDictionary(c => c.FacturaId, c => c.Estado);
    }

    /// <summary>
    /// El cambio congelado de cada fianza: el del último cobro vigente que la pagó. La fianza no tiene
    /// columna propia —es un contrato, no un documento con cambio congelado (D9)— y el cobro sí lo tiene.
    /// Con dos cobros a cambio distinto se queda el último: consolidar un saldo a medias no es mejor que
    /// consolidar la última operación, y el desglose por documento es lo que permite preguntarlo.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> TiposDeCambioDeFianzaAsync(
        Guid[] fianzaIds,
        CancellationToken ct)
    {
        if (fianzaIds.Length == 0)
        {
            return [];
        }

        var cobros = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.FianzaId != null
                && fianzaIds.Contains(a.FianzaId.Value)
                && a.Vigente
                && a.Cobro!.Estado == EstadoCobro.Registrado)
            .OrderByDescending(a => a.Cobro!.FechaEfectiva)
            .ThenByDescending(a => a.Id)
            .Select(a => new { FianzaId = a.FianzaId!.Value, a.Cobro!.TipoCambioValor })
            .ToListAsync(ct);

        // Viene ordenado, y GroupBy conserva el orden de aparición: el primero de cada grupo es el último.
        return cobros.GroupBy(c => c.FianzaId).ToDictionary(g => g.Key, g => g.First().TipoCambioValor);
    }

    /// <summary>
    /// El saldo a favor de los contratos que aparecen en las filas, que es dinero recibido que todavía no
    /// está aplicado a ningún documento. No sale en la vista única —no es una fila de documento— y por eso
    /// va separado en el bloque de cobrado en vez de mezclado con lo cobrado a facturas y fianzas.
    /// <para>
    /// <b>No se puede acotar al periodo</b>: la fila es un saldo por contrato y moneda
    /// (<c>saldo_a_favor</c>) y no tiene fecha de negocio, solo las de auditoría, que además cambian cada
    /// vez que otro cobro lo consume. Por eso quien decide si entra en el total es
    /// <see cref="ConstruirResumenAsync"/>: con rango de fechas se queda fuera y la respuesta lo dice.
    /// </para>
    /// </summary>
    private async Task<Acumulador> SaldoAFavorAsync(IReadOnlyCollection<CuentaFila> filas, CancellationToken ct)
    {
        var contratos = filas
            .Select(f => f.ContratoId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var acumulado = new Acumulador();

        if (contratos.Length == 0)
        {
            return acumulado;
        }

        var creditos = await contexto.SaldoAFavor.AsNoTracking()
            .Where(s => contratos.Contains(s.ContratoId))
            .Select(s => new { s.Importe, s.Moneda })
            .ToListAsync(ct);

        // Sin cambio congelado propio: el crédito es saldo, no un movimiento con fecha y cambio.
        foreach (var credito in creditos)
        {
            acumulado.Agregar(credito.Moneda, credito.Importe, null);
        }

        return acumulado;
    }

    /// <summary>
    /// Los ajustes por diferencia de cambio vigentes (§3.10.2, F3-07) de las facturas no anuladas del
    /// informe. Van aparte del cobrado a propósito: <see cref="CalculoCobro"/> ya enseña que el ajuste
    /// cierra saldo sin ser dinero que entró en caja, y mezclarlo haría que un informe dijera que la
    /// caja recibió dinero que nadie recibió.
    /// </summary>
    private async Task<Acumulador> DiferenciasDeCambioAsync(CuentaFila[] facturas, CancellationToken ct)
    {
        var ids = facturas.Where(f => !EstaAnulada(f)).Select(f => f.Id).ToArray();
        var acumulado = new Acumulador();

        if (ids.Length == 0)
        {
            return acumulado;
        }

        var ajustes = await contexto.AjusteDiferenciaCambio.AsNoTracking()
            .Where(a => a.AnuladoAt == null && ids.Contains(a.FacturaId))
            .Select(a => new { a.Importe, a.Moneda, a.TipoCambioValor })
            .ToListAsync(ct);

        foreach (var ajuste in ajustes)
        {
            acumulado.Agregar(ajuste.Moneda, ajuste.Importe, ajuste.TipoCambioValor);
        }

        return acumulado;
    }

    /// <summary>
    /// La serie mensual del gráfico: facturado por mes de emisión y cobrado por mes en que entró el
    /// dinero.
    /// <para>
    /// <b>El facturado sale de las filas</b> —una factura es de un mes por su fecha de emisión— y
    /// <b>el cobrado sale de los cobros</b>, agrupados por <c>fecha_efectiva</c> y no por la fecha de la
    /// factura que pagan: un gráfico en el que el cobro aparece en el mes de la factura es un gráfico
    /// de facturación, no de caja. Aquí cada movimiento usa además su propio cambio congelado, que es el
    /// del día que entró el dinero.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<SerieMensualInforme>> SerieMensualAsync(
        CuentaFila[] facturadas,
        CuentaFila[] facturas,
        CuentaFila[] fianzas,
        Dictionary<Guid, DocumentoFactura> documentos,
        CancellationToken ct)
    {
        var porMes = new Dictionary<string, SerieMensual>();

        foreach (var fila in facturadas)
        {
            var mes = MesDe(fila.Fecha);
            var acumulado = porMes.GetValueOrDefault(mes) ?? new SerieMensual();

            // Una factura en dólares no necesita cambio; una en pesos que no lo tenga congelado no entra
            // en el consolidado, y ya está contada en `documentosSinTipoCambio`.
            acumulado.FacturadoUsd += EnUsd(
                fila.Moneda,
                fila.Total ?? 0m,
                documentos.GetValueOrDefault(fila.Id)?.TipoCambioValor) ?? 0m;

            porMes[mes] = acumulado;
        }

        var porFactura = facturas.ToDictionary(f => f.Id, f => f);
        var porFianza = fianzas.ToDictionary(f => f.Id, f => f);
        var facturasIds = facturas.Select(f => f.Id).ToArray();
        var fianzaIds = fianzas.Select(f => f.Id).ToArray();

        var movimientos = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.Vigente
                && a.Cobro!.Estado == EstadoCobro.Registrado
                && ((a.FacturaId != null && facturasIds.Contains(a.FacturaId.Value))
                    || (a.FianzaId != null && fianzaIds.Contains(a.FianzaId.Value))))
            .Select(a => new
            {
                a.FacturaId,
                a.FianzaId,
                a.Importe,
                Fecha = a.Cobro!.FechaEfectiva,
                a.Cobro!.TipoCambioValor,
            })
            .ToListAsync(ct);

        foreach (var movimiento in movimientos)
        {
            var destino = movimiento.FacturaId is { } facturaId
                ? porFactura.GetValueOrDefault(facturaId)
                : porFianza.GetValueOrDefault(movimiento.FianzaId!.Value);

            if (destino is null)
            {
                continue;
            }

            var mes = MesDe(movimiento.Fecha);
            var acumulado = porMes.GetValueOrDefault(mes) ?? new SerieMensual();

            acumulado.CobradoUsd += EnUsd(destino.Moneda, movimiento.Importe, movimiento.TipoCambioValor) ?? 0m;

            porMes[mes] = acumulado;
        }

        return
        [
            .. porMes
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new SerieMensualInforme(
                    p.Key,
                    Redondeo.Importe(p.Value.FacturadoUsd),
                    Redondeo.Importe(p.Value.CobradoUsd)))
        ];
    }

    /// <summary>
    /// El mes de negocio de una fecha, en <c>AAAA-MM</c>. Una fila sin fecha no es posible —la vista no
    /// las trae— pero si lo fuera se contaría en enero del año 1 en vez de desaparecer del gráfico.
    /// </summary>
    private static string MesDe(DateOnly? fecha) =>
        (fecha ?? DateOnly.MinValue).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- dashboard

    /// <summary>
    /// Las unidades en el día de corte: disponibles, reservadas, ocupadas y en mantenimiento (§3.15).
    /// <para>
    /// <b>«Ocupada» y «reservada» no son campos</b> (§3.1): salen de los bloqueos por fechas de
    /// <c>ocupacion_rango</c> que están vigentes ese día, y como la base no deja que dos bloqueos se
    /// solapen en la misma unidad los cuatro grupos son excluyentes. «En mantenimiento» sí es un dato
    /// (<c>unidad.estado_manual</c>) y también cuenta un bloqueo de origen <c>Mantenimiento</c>.
    /// </para>
    /// </summary>
    private async Task<IndicadoresUnidadesInforme> IndicadoresDeUnidadesAsync(
        FiltroInformes filtro,
        DateOnly fecha,
        CancellationToken ct)
    {
        var unidades = await UnidadesFiltradasAsync(filtro, ct);

        if (unidades.Count == 0)
        {
            return new IndicadoresUnidadesInforme(0, 0, 0, 0);
        }

        var ids = unidades.Select(u => u.Id).ToArray();

        var bloqueos = await contexto.OcupacionRango.AsNoTracking()
            .Where(r => r.Bloquea
                && ids.Contains(r.UnidadId)
                && r.FechaInicio <= fecha
                && (r.FechaFin == null || r.FechaFin > fecha))
            .Select(r => new { r.UnidadId, r.Origen })
            .ToListAsync(ct);

        var mantenimiento = unidades
            .Where(u => u.EstadoManual == EstadoUnidad.Mantenimiento)
            .Select(u => u.Id)
            .ToHashSet();

        var reservadas = new HashSet<Guid>();
        var ocupadas = new HashSet<Guid>();

        foreach (var bloqueo in bloqueos)
        {
            switch (bloqueo.Origen)
            {
                case OrigenOcupacion.Mantenimiento:
                    mantenimiento.Add(bloqueo.UnidadId);
                    break;

                case OrigenOcupacion.Contrato:
                    ocupadas.Add(bloqueo.UnidadId);
                    break;

                default:
                    reservadas.Add(bloqueo.UnidadId);
                    break;
            }
        }

        var enAlguno = new HashSet<Guid>(mantenimiento);
        enAlguno.UnionWith(reservadas);
        enAlguno.UnionWith(ocupadas);

        return new IndicadoresUnidadesInforme(
            unidades.Count - enAlguno.Count,
            reservadas.Count,
            ocupadas.Count,
            mantenimiento.Count);
    }

    /// <summary>
    /// Los contratos que están vigentes el día del corte: estado activo y dentro de sus fechas (§3.5).
    /// No cuenta los de baja programada ni los finalizados, que ya no facturan.
    /// </summary>
    private async Task<int> ContratosActivosAsync(FiltroInformes filtro, DateOnly fecha, CancellationToken ct)
    {
        var consulta = contexto.Contrato.AsNoTracking()
            .Where(c => c.Estado == EstadoContrato.Activo
                && c.FechaInicio <= fecha
                && c.FechaFinVigente >= fecha);

        if (filtro.ClienteId is { } cliente)
        {
            consulta = consulta.Where(c => c.ClienteId == cliente);
        }

        if (filtro.ContratoId is { } contrato)
        {
            consulta = consulta.Where(c => c.Id == contrato);
        }

        if (HayFiltroDeUnidad(filtro))
        {
            // Los de unidad, inmueble, propietaria y facturadora se resuelven con la misma definición que
            // los del resto del informe: una subconsulta con la misma regla, no una segunda.
            var unidades = UnidadesFiltradas(filtro).Select(u => u.Id);

            consulta = consulta.Where(c => unidades.Contains(c.UnidadId));
        }

        return await consulta.CountAsync(ct);
    }

    /// <summary>
    /// Las unidades activas que los filtros de unidad dejan pasar. Los de cliente, serie, moneda, estado
    /// y contrato no aplican aquí: son filtros de documento, y una unidad no tiene de ellos.
    /// </summary>
    private async Task<List<Unidad>> UnidadesFiltradasAsync(FiltroInformes filtro, CancellationToken ct) =>
        await UnidadesFiltradas(filtro).ToListAsync(ct);

    /// <summary>
    /// La misma regla de <see cref="UnidadesFiltradasAsync"/> como consulta, porque un contrato la usa
    /// como subconsulta y no puede materializar una lista dentro de un <c>Where</c>.
    /// </summary>
    private IQueryable<Unidad> UnidadesFiltradas(FiltroInformes filtro)
    {
        IQueryable<Unidad> consulta = contexto.Unidad.AsNoTracking().Where(u => u.Activo);

        if (filtro.UnidadId is { } unidad)
        {
            consulta = consulta.Where(u => u.Id == unidad);
        }

        if (filtro.InmuebleId is { } inmueble)
        {
            consulta = consulta.Where(u => u.InmuebleId == inmueble);
        }

        if (filtro.PropietariaId is { } propietaria)
        {
            // La misma herencia que en la vista única: una unidad sin propietaria propia es del
            // propietario del inmueble (§3.1).
            consulta = consulta.Where(u => u.PropietariaId == propietaria
                || (u.PropietariaId == null
                    && contexto.Inmueble.Any(i => i.Id == u.InmuebleId && i.PropietariaId == propietaria)));
        }

        if (filtro.FacturadoraId is { } facturadora)
        {
            consulta = consulta.Where(u => u.FacturadoraPorDefectoId == facturadora);
        }

        return consulta;
    }

    private static bool HayFiltroDeUnidad(FiltroInformes filtro) =>
        filtro.UnidadId is not null || filtro.InmuebleId is not null
        || filtro.PropietariaId is not null || filtro.FacturadoraId is not null;

    // ---------------------------------------------------------------- conversión

    /// <summary>
    /// Una factura anulada. La fila lo dice en <c>estado</c> como <c>Anulada</c> —que es
    /// <see cref="EstadoCobroFactura.Anulada"/> y no un estado de pago: el número se conserva pero la
    /// deuda desapareció— y no hace falta volver a la tabla para saberlo.
    /// </summary>
    private static bool EstaAnulada(CuentaFila fila) =>
        fila.Estado == nameof(EstadoCobroFactura.Anulada);

    /// <summary>
    /// El importe en dólares, o <c>null</c> si no se puede consolidar.
    /// <para>
    /// <b>El valor del cambio congelado es lo que vale un dólar en la moneda del documento</b>
    /// (<c>ServicioCobros.cs:1194</c>: lo que sale de la caja en otra moneda es el importe por el
    /// cambio), así que convertir es dividir. En dólares el par no existe y el importe ya es el
    /// consolidado; en pesos sin cambio congelado no hay nada honesto que devolver.
    /// </para>
    /// </summary>
    private static decimal? EnUsd(Moneda moneda, decimal importe, decimal? tipoCambio)
    {
        if (moneda == Moneda.USD)
        {
            return importe;
        }

        return tipoCambio is { } cambio && cambio > 0m ? importe / cambio : null;
    }

    /// <summary>Lo que la fila no trae de su documento, en lo que hace falta para consolidar.</summary>
    private sealed record DocumentoFactura(Guid Id, decimal? TipoCambioValor, bool SumaAFacturado);

    /// <summary>
    /// Un mes en construcción de la serie. Es una clase mutable a propósito: el gráfico se arma mes a mes
    /// y lleva dos sumas, no dos registros.
    /// </summary>
    private sealed class SerieMensual
    {
        public decimal FacturadoUsd { get; set; }

        public decimal CobradoUsd { get; set; }
    }

    /// <summary>
    /// Suma de un bloque de dinero: las dos monedas por separado y el consolidado en dólares.
    /// <para>
    /// Se acumula sin ir redondeando y se redondea una vez al final, en <see cref="Importe"/>. Redondear
    /// documento a documento haría que la suma dependiera del orden de las filas.
    /// </para>
    /// <para>
    /// <b>Lo que no tiene cambio congelado no se convierte</b> y se cuenta en
    /// <see cref="SinTipoCambio"/>: se dice en la respuesta en vez de echarla con el cambio de hoy,
    /// que es lo que §3.12 prohíbe.
    /// </para>
    /// </summary>
    private sealed class Acumulador
    {
        private decimal usd;
        private decimal dop;
        private decimal consolidadoUsd;

        /// <summary>Documentos en pesos que se han quedado fuera del consolidado.</summary>
        public int SinTipoCambio { get; private set; }

        public void Agregar(Moneda moneda, decimal importe, decimal? tipoCambio)
        {
            if (moneda == Moneda.USD)
            {
                usd += importe;
                consolidadoUsd += importe;
                return;
            }

            dop += importe;

            if (tipoCambio is not { } cambio || cambio <= 0m)
            {
                SinTipoCambio++;
                return;
            }

            consolidadoUsd += importe / cambio;
        }

        /// <summary>Suma otro bloque en este, para el total de un bloque que tiene desglose.</summary>
        public void Sumar(Acumulador otro)
        {
            usd += otro.usd;
            dop += otro.dop;
            consolidadoUsd += otro.consolidadoUsd;
            SinTipoCambio += otro.SinTipoCambio;
        }

        public ImporteInforme Importe() =>
            new(Redondeo.Importe(usd), Redondeo.Importe(dop), Redondeo.Importe(consolidadoUsd));
    }
}