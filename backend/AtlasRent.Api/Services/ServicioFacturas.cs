using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Facturas y proformas (F3-02, docs/ESPECIFICACION.md §3.8).
/// <list type="bullet">
/// <item>
/// <b>Una sola tabla y un solo flujo</b>: lo que distingue una proforma de una factura es el tipo de
/// la serie (§3.7), no el sitio donde viven. Una factura nace en borrador y se emite una vez.
/// </item>
/// <item>
/// <b>El ITBIS es por línea</b> (§3.8): cada línea lleva su tipo, unas lo llevan y otras no, y cada
/// importe de impuesto se redondea en su línea. El total es la suma de lo ya redondeado, no el
/// redondeo del subtotal.
/// </item>
/// <item>
/// <b>La emisión es una sola transacción</b>: número, estado, fecha y tipo de cambio congelado salen
/// o no salen juntos (D28). Si algo falla después de sacar el número, el número no se consume.
/// </item>
/// </item>
/// <item>
/// <b>Emitida, no se toca.</b> Una factura en borrador no tiene número, ni comprobante, ni deuda, así
/// que se reescribe entera. Emitida, lo que hay es anular y emitir otra (F3-08).
/// </item>
/// <item>
/// <b>La retención es una línea más</b> (F3-04, §3.9): va en negativo como el descuento pero
/// <b>fuera</b> del subtotal, porque el total no baja por retener, lo que baja es el neto a cobrar.
/// Solo la admiten los clientes agente de retención, y emitir una factura que retiene deja creado su
/// comprobante pendiente.
/// </item>
/// </list>
/// <para>
/// <b>El servicio comprueba el permiso y no solo el endpoint.</b> F3-05 emite facturas desde un
/// trabajo de Hangfire, sin sesión HTTP ni filtro <c>[Accion]</c>: si la comprobación viviera
/// únicamente en el controlador, ese camino emitiría sin mirar quién lo hizo.
/// </para>
/// <para>
/// Nada se borra nunca (AGENTS.md §6). Las líneas de un borrador sí, porque mientras esté en
/// borrador no tienen efecto económico y borrarlas no pierde nada de lo que ya salió.
/// </para>
/// </summary>
public sealed class ServicioFacturas(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioSeries series,
    ServicioComprobantes comprobantes,
    ServicioRetenciones retenciones,
    ServicioTiposCambio tiposCambio,
    ServicioCobros cobros,
    IPolicyService politica,
    // F4-01: el PDF de factura imprime el documento de identidad del cliente descifrado (D16). Lo
    // descifra este servicio y no el compositor, que no sabe de claves.
    ServicioCifrado cifrado,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<Factura>, bool, IOrderedQueryable<Factura>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(f => f.CreatedAt) : q.OrderBy(f => f.CreatedAt),
            ["fechaEmision"] = (q, desc) => desc ? q.OrderByDescending(f => f.FechaEmision) : q.OrderBy(f => f.FechaEmision),
            ["vencimiento"] = (q, desc) => desc ? q.OrderByDescending(f => f.FechaVencimiento) : q.OrderBy(f => f.FechaVencimiento),
            ["total"] = (q, desc) => desc ? q.OrderByDescending(f => f.Total) : q.OrderBy(f => f.Total),
            ["numero"] = (q, desc) => desc ? q.OrderByDescending(f => f.Numero) : q.OrderBy(f => f.Numero),
        };

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Listado paginado. Filtra por estado, serie, cliente, unidad, moneda y rango de fechas de
    /// emisión, que es lo que necesita la vista de cuentas (§3.13) y el informe de Aging (§3.15).
    /// <para>
    /// <b>Sin totales agregados</b>: son de §3.15 y solo gerente y administrador los ven (F3-10).
    /// </para>
    /// </summary>
    public async Task<Pagina<FacturaResumen>> ListarAsync(
        ConsultaLista paginacion,
        EstadoFactura? estado,
        Guid? serieId,
        Guid? clienteId,
        Guid? unidadId,
        Moneda? moneda,
        DateOnly? desde,
        DateOnly? hasta,
        CancellationToken ct)
    {
        IQueryable<Factura> consulta = contexto.Factura.AsNoTracking()
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie);

        if (estado is { } filtroEstado)
        {
            consulta = consulta.Where(f => f.Estado == filtroEstado);
        }

        if (serieId is { } serie)
        {
            consulta = consulta.Where(f => f.SerieId == serie);
        }

        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(f => f.ClienteId == cliente);
        }

        if (unidadId is { } unidad)
        {
            consulta = consulta.Where(f => f.UnidadId == unidad);
        }

        if (moneda is { } filtroMoneda)
        {
            consulta = consulta.Where(f => f.Moneda == filtroMoneda);
        }

        // El rango es de fecha de emisión, no de creación: un borrador todavía no tiene fecha de
        // emisión y no sale por un filtro de fechas que es de documentos ya emitidos.
        if (desde is { } desdeFecha)
        {
            consulta = consulta.Where(f => f.FechaEmision >= desdeFecha);
        }

        if (hasta is { } hastaFecha)
        {
            consulta = consulta.Where(f => f.FechaEmision <= hastaFecha);
        }

        var pagina = await ConsultaPaginada.PaginarAsync(
            consulta, Ordenes, "createdAt", paginacion, AResumen, ct);

        // Cobrado, pendiente y estado de cobro (F3-06, §3.8) se resuelven en una sola consulta
        // agregada sobre la página, no con una consulta por factura: son datos de otra tabla
        // (cobro) y una vista de cuentas de 25 filas no puede costar 26 viajes. El ajuste por
        // diferencia de cambio (F3-07, §3.10.2) va en su propia consulta porque va en su propia tabla
        // y porque no se suma dentro de «cobrado»: es dinero que no entró en caja.
        var cobrados = await cobros.CobradoPorFacturaAsync(
            [.. pagina.Data.Select(f => f.Id)], ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync(
            [.. pagina.Data.Select(f => f.Id)], ct);

        // El indicador de comprobante de retención (F3-04, §3.9) va en su propia consulta agregada
        // por la misma razón que el cobrado: los comprobantes pendientes de la página, de una vez.
        var sinComprobante = await retenciones.SinComprobanteAsync(
            [.. pagina.Data.Select(f => f.Id)], ct);

        return pagina with
        {
            Data = [.. pagina.Data.Select(f => ConCobro(
                f,
                cobrados.GetValueOrDefault(f.Id),
                ajustados.GetValueOrDefault(f.Id),
                sinComprobante.Contains(f.Id)))],
        };
    }

    public async Task<FacturaRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var factura = await CargarAsync(id, ct);

        var cobrados = await cobros.CobradoPorFacturaAsync([factura.Id], ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync([factura.Id], ct);
        var sinComprobante = await retenciones.SinComprobanteAsync([factura.Id], ct);

        return await ConDatosFiscalesAsync(
            ConCobro(
                ARespuesta(factura),
                cobrados.GetValueOrDefault(factura.Id),
                ajustados.GetValueOrDefault(factura.Id),
                sinComprobante.Contains(factura.Id)),
            factura.ModificadaAt,
            ct);
    }

    /// <summary>
    /// Completa con los datos fiscales una respuesta de escritura (emitir, modificar, anular, líneas,
    /// referencia de e-factura) para que diga lo mismo que el detalle. <c>ModificadaAt</c> se lee de la
    /// base porque la entidad de la que sale la respuesta ya se guardó en la misma transacción.
    /// </summary>
    private async Task<FacturaRespuesta> EnriquecerEscrituraAsync(FacturaRespuesta r, CancellationToken ct)
    {
        var modificadaAt = await contexto.Factura.AsNoTracking()
            .Where(f => f.Id == r.Id)
            .Select(f => f.ModificadaAt)
            .FirstOrDefaultAsync(ct);

        return await ConDatosFiscalesAsync(r, modificadaAt, ct);
    }

    private async Task<FacturaRespuesta> ConDatosFiscalesAsync(
        FacturaRespuesta respuesta,
        DateTimeOffset? modificadaAt,
        CancellationToken ct)
    {
        var facturaId = respuesta.Id;

        var comprobanteFiscal = await contexto.ComprobanteFiscal.AsNoTracking()
            .Where(c => c.FacturaId == facturaId)
            .Select(c => new { c.Id, c.Tipo, c.Numero })
            .FirstOrDefaultAsync(ct);

        var comprobanteRetencionId = await contexto.ComprobanteRetencion.AsNoTracking()
            .Where(c => c.FacturaId == facturaId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // Mismo criterio que la vista de cuentas: un envío fallido o pendiente no cuenta.
        var modificadaTrasEnvio = modificadaAt != null
            && await contexto.Envio.AsNoTracking().AnyAsync(e =>
                e.TipoDocumento == TipoDocumentoEnvio.Factura
                && e.DocumentoId == facturaId
                && e.Estado != EstadoEnvio.Fallido
                && e.Estado != EstadoEnvio.Pendiente
                && (e.DocumentoModificadoAt == null || modificadaAt > e.DocumentoModificadoAt), ct);

        return respuesta with
        {
            ComprobanteFiscal = comprobanteFiscal is null
                ? null
                : $"{comprobanteFiscal.Tipo} {comprobanteFiscal.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ComprobanteFiscalId = comprobanteFiscal?.Id,
            ModificadaTrasEnvio = modificadaTrasEnvio,
            ComprobanteRetencionId = comprobanteRetencionId,
        };
    }

    // ---------------------------------------------------------------- PDF (F4-01)

    /// <summary>
    /// PDF de la factura o la proforma con el diseño de <c>docs/DESIGN.md §7</c>.
    /// <para>
    /// <b>Una factura anulada también se descarga, y a propósito.</b> Es el papel que necesita el
    /// expediente del cliente y el contador, y el documento lo lleva marcado con «ANULADA» para que
    /// nadie lo confunda con una factura viva. Lo que no se puede es <em>emitir</em> una anulada, y
    /// eso lo sigue llevando <see cref="EmitirAsync"/>.
    /// </para>
    /// </summary>
    public async Task<byte[]> PdfAsync(Guid id, CancellationToken ct) =>
        DocumentoFacturaPdf.Generar(await ModeloPdfAsync(id, ct));

    /// <summary>
    /// Lo que el PDF va a decir, separado del render (F4-01).
    /// <para>
    /// El texto de un PDF de QuestPDF no se puede leer del archivo: incrusta las fuentes con
    /// subconjunto y escribe los caracteres como índices de glifo. Sin este paso intermedio, las
    /// pruebas solo podrían comprobar que el archivo existe.
    /// </para>
    /// <para>
    /// Es <c>internal</c> porque por fuera de la API solo sale el PDF. El compositor es interno por
    /// el mismo motivo.
    /// </para>
    /// </summary>
    internal async Task<DocumentoFacturaPdfModelo> ModeloPdfAsync(Guid id, CancellationToken ct)
    {
        var factura = await CargarParaDocumentoAsync(id, ct);

        var cobrados = await cobros.CobradoPorFacturaAsync([factura.Id], ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync([factura.Id], ct);

        // Pagado y pendiente salen de Dominio/CalculoCobro, el mismo cálculo que usa la lectura: lo
        // que dice el papel y lo que dice la pantalla no pueden ser dos números distintos. Sin
        // cobros no se pone la fila, en vez de poner un pagado de 0,00.
        var situacion = cobrados.Count == 0 && ajustados.Count == 0
            ? null
            : cobros.SituacionDeFactura(
                factura.Total,
                factura.Retencion,
                cobrados.GetValueOrDefault(factura.Id),
                ajustados.GetValueOrDefault(factura.Id),
                factura.FechaVencimiento,
                factura.Estado);

        var comprobante = await contexto.ComprobanteFiscal.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FacturaId == factura.Id, ct);

        return DocumentoFacturaPdf.Modelo(
            factura,
            // La serie se validó al guardarse, así que aquí el nombre siempre existe: si no
            // existiera es datos de otra base, y sale el 400 con la lista en vez de imprimir otro
            // diseño.
            RegistroPlantillasPdf.Resolver(factura.Serie?.PlantillaPdf),
            await FacturadoraAsync(factura, ct),
            Descifrar(factura.Cliente?.Documento),
            situacion,
            comprobante);
    }

    /// <summary>
    /// Empresa facturadora de la cabecera: la del contrato y, sin contrato, la que la unidad tiene
    /// por defecto (F4-01).
    /// <para>
    /// TODO(A#): el orden es el de <c>docs/ESPECIFICACION.md</c> §3.7 —el contrato dice quién
    /// factura y la unidad solo propone—, pero está escrito aquí y no repartido entre los dos
    /// caminos para que cambiarlo sea una línea y no una decisión escondida en dos consultas.
    /// </para>
    /// <para>
    /// Sin ninguna de las dos la cabecera sale sin bloque de empresa. Imprimir la de otra factura
    /// habría sido peor que imprimirla vacía.
    /// </para>
    /// </summary>
    private async Task<Subempresa?> FacturadoraAsync(Factura factura, CancellationToken ct)
    {
        var facturadora = await BuscarFacturadoraAsync(factura, ct);

        // Las dos cargas son sin seguimiento, así que sustituir aquí el RNC cifrado por el
        // descifrado no lo guarda nadie: es lo que hace que el compositor reciba el valor y no el
        // cifrado, igual que el documento del cliente (D16). Si alguna de las dos dejara de ser
        // sin seguimiento, esto habría que cambiar por una copia.
        if (facturadora is not null)
        {
            facturadora.Rnc = Descifrar(facturadora.Rnc);
        }

        return facturadora;
    }

    private async Task<Subempresa?> BuscarFacturadoraAsync(Factura factura, CancellationToken ct)
    {
        if (factura.Contrato?.Facturadora is Subempresa delContrato)
        {
            return delContrato;
        }

        if (factura.Unidad?.FacturadoraPorDefectoId is not Guid deLaUnidad)
        {
            return null;
        }

        return await contexto.Subempresa.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == deLaUnidad, ct);
    }

    /// <summary>
    /// El documento de identidad y el RNC de la facturadora se descifran para el PDF y solo para el
    /// PDF (D16).
    /// </summary>
    private string? Descifrar(string? valorCifrado) =>
        valorCifrado is null ? null : cifrado.Descifrar(valorCifrado);

    // ---------------------------------------------------------------- escritura de borrador

    /// <summary>
    /// Alta de una factura en borrador. Nace siempre en borrador: el número lo saca la emisión, y un
    /// borrador no consume ninguno (§3.7).
    /// </summary>
    public async Task<FacturaResumen> CrearAsync(
        FacturaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct,
        PeriodoFacturacion? periodo = null)
    {
        await ExigirAsync(Acciones.FacturaBorrador, "factura", usuario, ct);
        await ExigirReferenciasAsync(peticion, ct);

        var serie = await CargarSerieAsync(peticion.SerieId, ct);
        ExigirMonedaDeSerie(serie, peticion.Moneda);

        var factura = new Factura
        {
            Estado = EstadoFactura.Borrador,
            SerieId = peticion.SerieId,
        };
        Aplicar(factura, peticion);

        // F3-05: el periodo solo lo fija quien factura un mes natural —la facturación periódica—. Una
        // factura tecleada a mano lo deja nulo, y por eso el índice único de periodo no la mira.
        factura.PeriodoInicio = periodo?.Inicio;
        factura.PeriodoFin = periodo?.Fin;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: true,
            accion: "factura.creada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c),
            ct: ct);
    }

    /// <summary>Edita la cabecera. Solo mientras la factura esté en borrador.</summary>
    public async Task<FacturaResumen> ActualizarAsync(
        Guid id,
        FacturaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaBorrador, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);
        ExigirBorrador(factura);
        await ExigirReferenciasAsync(peticion, ct);

        var serie = await CargarSerieAsync(peticion.SerieId, ct);
        ExigirMonedaDeSerie(serie, peticion.Moneda);

        Aplicar(factura, peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.actualizada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c),
            ct: ct);
    }

    /// <summary>
    /// Da de alta una línea y recalcula los totales de la factura.
    /// <para>
    /// Se edita línea a línea y no reemplazando la lista entera porque un descuento mal puesto se
    /// corrige borrando una línea, y replace-all obligaría a volver a mandar todas las demás.
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> AgregarLineaAsync(
        Guid id,
        FacturaLineaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaBorrador, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);
        ExigirBorrador(factura);

        var tipoItbis = await CargarTipoItbisAsync(peticion.TipoItbisId, ct);
        var tipoRetencion = await CargarTipoRetencionAsync(peticion.TipoRetencionId, peticion.Tipo, ct);

        if (tipoRetencion is not null)
        {
            ExigirAgenteRetencion(factura);
        }

        var linea = ConstruirLinea(factura, peticion, tipoItbis, tipoRetencion, usuario);

        // La línea entra por el DbSet y no solo por la colección de la factura: con el
        // identificador ya puesto, EF deduce que la fila existe y la marca como modificada, que en
        // un UPDATE sin fila que actualizar es un 409 sin motivo aparente. Es el mismo camino que
        // usa ServicioCotizaciones con sus líneas.
        return await GuardarLineasAsync(factura, "factura.linea_agregada", usuario, ip, mutacion, () =>
            contexto.FacturaLinea.Add(linea), ct);
    }

    /// <summary>Edita una línea y vuelve a calcular los totales.</summary>
    public async Task<FacturaRespuesta> EditarLineaAsync(
        Guid id,
        Guid lineaId,
        FacturaLineaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaBorrador, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);
        ExigirBorrador(factura);

        var linea = factura.Lineas.SingleOrDefault(l => l.Id == lineaId)
            ?? throw new KeyNotFoundException($"La línea '{lineaId:D}' no está en la factura '{id:D}'.");

        var tipoItbis = await CargarTipoItbisAsync(peticion.TipoItbisId, ct);
        var tipoRetencion = await CargarTipoRetencionAsync(peticion.TipoRetencionId, peticion.Tipo, ct);

        if (tipoRetencion is not null)
        {
            ExigirAgenteRetencion(factura);
        }

        AplicarLinea(linea, peticion, tipoItbis, tipoRetencion);

        return await GuardarLineasAsync(factura, "factura.linea_actualizada", usuario, ip, mutacion, () =>
        {
            // La línea ya está en el grafo de objetos: aquí solo se guarda y se recalcula.
        }, ct);
    }

    /// <summary>
    /// Borra una línea de una factura en borrador.
    /// <para>
    /// <b>Solo en borrador.</b> Una factura en borrador no tiene número, ni comprobante, ni deuda,
    /// así que sus líneas no tienen efecto económico y se pueden rehacer las que haga falta. Emitida,
    /// ninguna línea se borra ni se toca: eso es la anulación de F3-08, que conserva el documento.
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> BorrarLineaAsync(
        Guid id,
        Guid lineaId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaBorrador, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);
        ExigirBorrador(factura);

        var linea = factura.Lineas.SingleOrDefault(l => l.Id == lineaId)
            ?? throw new KeyNotFoundException($"La línea '{lineaId:D}' no está en la factura '{id:D}'.");

        var concepto = linea.Concepto;

        // La factura es la que se versiona por UpdatedAt/UpdatedBy: el cambio de una línea deja
        // huella en el documento, no solo en la línea que se va.
        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.linea_eliminada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => new
            {
                factura = c.Id.ToString("D"),
                linea = lineaId.ToString("D"),
                concepto,
                borrador = true,
            },
            antesDeGuardar: () =>
            {
                factura.Lineas.Remove(linea);
                RecalcularTotales(factura);
                return Task.CompletedTask;
            },
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);
    }

    // ---------------------------------------------------------------- emisión

    /// <summary>
    /// Emite la factura: le asigna el número siguiente de su serie, la pasa a <c>Emitida</c>, le
    /// pone la fecha de emisión y le congela el tipo de cambio del día (D9).
    /// <para>
    /// <b>Todo en una sola transacción.</b> El número, la factura y la fila de <c>numero_serie</c>
    /// salen o no salen juntos: si algo falla después de reservar el número, el número no se
    /// consume y la factura sigue en borrador (D28). Por eso la transacción es del servicio y
    /// <see cref="GuardadoAuditado"/> entra con <c>enTransaccionDelLlamador</c>, y no al revés.
    /// </para>
    /// <para>
    /// <b>Sin tipo de cambio del día no se emite.</b> El cambio congelado es el dato con el que los
    /// informes consolidan la factura en dólares (D9); emitir sin él dejaría un documento que no se
    /// puede consolidar. La salida es la carga manual de §3.12, y el 409 la dice.
    /// </para>
    /// <para>
    /// <b>El comprobante fiscal va en la misma transacción que la factura</b> (F3-03, §3.7): si la
    /// serie genera comprobante, se comprueba el numerador antes de gastar el número de la factura y
    /// se saca el número de comprobante después, con su propio contador. Una serie fiscal sin
    /// numerador no emite: una factura sin comprobante no se puede declarar.
    /// </para>
    /// <para>
    /// El comprobante de retención de una factura que retiene (F3-04, §3.9) también va en la misma
    /// transacción: sale con la factura o no sale. Y una factura sin retención no deja fila, que es
    /// lo que hace la consulta del indicador barata.
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> EmitirAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaEmitir, $"factura/{id:D}", usuario, ct);

        // F3-08: la conversión de proforma llama a este método desde su propia transacción para que la
        // factura nueva y la proforma caigan juntas. Cuando ya hay una abierta se usa esa y no se
        // confirma aquí; si algo falla después, el número y el comprobante tampoco salen.
        await using var transaccion = contexto.Database.CurrentTransaction is null
            ? await contexto.Database.BeginTransactionAsync(ct)
            : null;

        var factura = await CargarAsync(id, ct);

        // Exigir el borrador va antes de tocar nada: una factura ya emitida no se vuelve a emitir,
        // y el 409 tiene que salir sin haber abierto nada.
        ExigirBorrador(factura);

        if (factura.Lineas.Count == 0)
        {
            throw new InvalidOperationException("Una factura no se emite sin líneas: no hay nada que facturar.");
        }

        var serie = await CargarSerieAsync(factura.SerieId, ct);
        if (!serie.Activa)
        {
            throw new InvalidOperationException($"La serie {serie.Numero} está desactivada y no emite.");
        }

        ExigirMonedaDeSerie(serie, factura.Moneda);

        // El cambio se congela antes de gastar el número: si falta, el 409 sale sin haber tocado el
        // contador, que es justo lo que evita que un fallo de la fuente de cambios haga que la serie
        // se quede con un hueco.
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());
        var cambio = await tiposCambio.ActualAsync(factura.Moneda, hoy, ct);

        // Y el numerador del comprobante se comprueba antes que el número de factura, por el mismo
        // motivo y por el del criterio de aceptación del plan: una serie fiscal cuyo numerador está
        // agotado o vencido rechaza la emisión y no gasta el número de la factura (F3-03, §3.7).
        if (serie.GeneraComprobante)
        {
            await comprobantes.ExigirUtilizableAsync(serie.Id, serie.Numero, hoy, ct);
        }

        var serieId = factura.SerieId;
        int numero = 0;
        int? numeroComprobante = null;

        var respuesta = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.emitida",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: f => ARespuesta(f) with
            {
                // El comprobante de retención (F3-04, §3.9) nace pendiente en esta misma transacción,
                // así que la respuesta de la emisión lo dice sin tener que ir a preguntarlo: si la
                // factura retiene, hay que salir a buscar el papel.
                RetencionSinComprobante = f.Retencion > 0m,
            },
            datosAuditoria: c => DatosAuditoria(c, numeroComprobante),
            antesDeGuardar: async () =>
            {
                numero = await series.ReservarNumeroAsync(serieId, usuario, ct);
                factura.Numero = numero;
                factura.Estado = EstadoFactura.Emitida;
                factura.FechaEmision = hoy;
                factura.TipoCambioValor = Redondeo.TipoCambio(cambio.Valor);
                factura.TipoCambioFecha = cambio.Fecha;
                factura.TipoCambioId = cambio.Id;

                // El comprobante sale después del número de factura y en la misma transacción: si
                // esto falla, se deshace el número también.
                if (serie.GeneraComprobante)
                {
                    numeroComprobante = await comprobantes.ReservarAsync(serieId, factura.Id, usuario, hoy, ct);
                }

                // Y el comprobante de retención, si la factura retiene (F3-04). No hace falta su
                // propio SaveChanges: lo añade al grafo y sale con el de la factura, que es
                // justamente lo que hace que un fallo posterior lo deshaga con ella.
                await retenciones.ReservarEnEmisionAsync(factura, usuario, ct);
            },
            enTransaccionDelLlamador: true,
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);

        if (transaccion is not null)
        {
            await transaccion.CommitAsync(ct);
        }

        return respuesta;
    }

    // ---------------------------------------------------------------- ciclo de vida (F3-08)
    //
    // Aquí se cierra el ciclo del documento: lo que ya salió se modifica o se anula, y lo que se
    // anula se puede rehacer en otra moneda. Todo lo que hay aquí conserva el número ya emitido
    // (D28): anular marca, nunca borra (AGENTS.md §6).
    //
    // Las seis operaciones de este bloque piden `factura.anular`, el permiso que §2 da exactamente a
    // este trabajo («modificar o anular una factura ya emitida»), y no `factura.emitir`: emitir es
    // sacar número y comprobante, que es otra cosa y ya tiene su propio permiso.

    /// <summary>
    /// Modifica las líneas de una factura ya emitida (F3-08, §3.8).
    /// <para>
    /// <b>Solo las líneas, y solo si no hay comprobante fiscal.</b> Lo demás de la factura —número,
    /// fecha de emisión, tipo de cambio congelado, cliente, moneda— sale intacto: esos datos son los
    /// que la DGII y el cliente ya tienen delante, y una factura que cambia de número no es la factura
    /// que se emitió. Si hay comprobante fiscal tampoco se tocan los importes: el 409 dice que el
    /// camino es anular y emitir otra.
    /// </para>
    /// <para>
    /// Los totales se vuelven a calcular con las mismas funciones de dominio de F3-02 y F3-04, y el
    /// neto a cobrar también: cambiar una cantidad cambia lo que se pide, no solo el impreso.
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> ModificarAsync(
        Guid id,
        FacturaModificacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);
        ExigirModificable(factura);

        // TODO(A#): §3.7 y §3.8 marcan como [Verificar] el formato y los rangos del comprobante
        // fiscal con el contador. Mientras eso no esté confirmado con la DGII, aquí solo se mira que
        // exista: si el día se acordara que hay comprobantes anulables, esta condición es la que
        // habría que cambiar, porque es la que protege los importes.
        if (await contexto.ComprobanteFiscal.AnyAsync(c => c.FacturaId == id, ct))
        {
            throw new InvalidOperationException(
                $"La factura '{id:D}' tiene comprobante fiscal y sus importes no se modifican: lo que "
                    + "salió con comprobante se anula y se emite otra (F3-08).");
        }

        foreach (var cambio in peticion.Lineas)
        {
            var linea = factura.Lineas.SingleOrDefault(l => l.Id == cambio.LineaId)
                ?? throw new KeyNotFoundException(
                    $"La línea '{cambio.LineaId:D}' no está en la factura '{id:D}'.");

            var tipoItbis = await CargarTipoItbisAsync(cambio.TipoItbisId, ct);
            var tipoRetencion = await CargarTipoRetencionAsync(cambio.TipoRetencionId, cambio.Tipo, ct);

            if (tipoRetencion is not null)
            {
                ExigirAgenteRetencion(factura);
            }

            AplicarLinea(linea, AComoLinea(cambio), tipoItbis, tipoRetencion);
        }

        // Recalcular es obligatorio y no opcional: dejar el total viejo con líneas nuevas es un
        // documento que no cuadra contra sí mismo.
        RecalcularTotales(factura);

        // BUG-021: lo ya cobrado (y ajustado por diferencia de cambio) no se puede quedar por encima
        // del neto: el exceso sería un sobrecobro sin reflejo en ningún sitio.
        var cobrado = (await cobros.CobradoPorFacturaAsync([id], ct)).GetValueOrDefault(id);
        var ajustado = (await cobros.AjustadoPorFacturaAsync([id], ct)).GetValueOrDefault(id);
        var yaCubierto = Redondeo.Importe(cobrado + ajustado);

        if (factura.NetoACobrar < yaCubierto)
        {
            throw new InvalidOperationException(
                $"La factura '{id:D}' ya tiene cobrados {yaCubierto} {factura.Moneda} y el neto a cobrar "
                    + $"quedaría en {factura.NetoACobrar}: no se puede bajar por debajo de lo cobrado. "
                    + "Para corregirla, anula el cobro o la factura.");
        }

        // BUG-036: el comprobante de retención sigue a la factura mientras no se haya recibido.
        await retenciones.ActualizarEnModificacionAsync(factura, usuario, ct);

        // ModificadaAt se escribe una sola vez (la primera), porque es la marca que va a filtrar
        // «modificadas tras envío» de F4-02 y para eso vale la primera vez y no la última.
        factura.ModificadaAt ??= reloj.GetUtcNow();
        factura.ModificadaPor ??= usuario;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.modificada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => DatosAuditoria(c),
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);
    }

    /// <summary>
    /// Anula una factura emitida conservando todo lo que salió (F3-08, §3.8, D6, D28).
    /// <para>
    /// <b>Una sola transacción para cinco cosas:</b> la factura queda <c>Anulada</c> con su motivo, su
    /// número pasa a anulado en <c>numero_serie</c> sin que el contador retroceda, su comprobante
    /// fiscal pasa a anulado, sus asignaciones de cobro dejan de contar y, si un cobro se queda sin
    /// ninguna asignación viva, ese cobro se anula también. Si algo falla, no queda ninguna de ellas a
    /// medias: un comprobante anulado con la factura viva sería un documento fiscal que contradice al
    /// libro.
    /// </para>
    /// <para>
    /// <b>Un cobro con otras facturas vivas no se toca.</b> Si ese cobro seguía pagando otra factura,
    /// sigue vigente y solo pierde esta: anular la factura no es Deshacer el cobro.
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> AnularAsync(
        Guid id,
        FacturaAnulacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var motivo = peticion.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            // 400 y no 409, por el mismo motivo que en los cobros: no es un conflicto con lo que hay,
            // es una petición incompleta. El modelo ya rechaza el motivo ausente; esto cubre el de
            // puros espacios.
            throw new ArgumentException(
                "El motivo de la anulación es obligatorio: sin motivo, una factura que ya salió con "
                    + "número no se puede explicar.",
                nameof(peticion));
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await TomarCerrojoDeCobrosAsync(id, ct);

        var factura = await CargarAsync(id, ct);
        ExigirAnulable(factura);

        var devueltos = new List<object>();

        var respuesta = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.anulada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            // El motivo va en la auditoría firmada además de en la fila: la fila se puede leer desde
            // la factura, la auditoría es la que no se puede reescribir.
            datosAuditoria: c => new
            {
                Factura = c.Id.ToString("D"),
                Numero = c.Numero,
                Serie = c.SerieId.ToString("D"),
                Estado = c.Estado.ToString(),
                Motivo = motivo,
                TotalAnulado = c.Total,
                Devuelto = devueltos,
            },
            antesDeGuardar: async () => await AnularEnTransaccionAsync(factura, motivo, usuario, ip, devueltos, ct),
            enTransaccionDelLlamador: true,
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);

        await transaccion.CommitAsync(ct);

        return respuesta;
    }

    /// <summary>
    /// Lo que la anulación haría, sin hacerlo (§3.8: la pantalla de confirmación).
    /// <para>
    /// <b>No escribe nada.</b> Devuelve los cobros que se anularían enteros y los que solo dejarían de
    /// pagar esta factura, con sus importes, que es la información que hace falta antes de confirmar
    /// una corrección de dinero. F3-10 la usa en la vista de cuentas.
    /// </para>
    /// </summary>
    public async Task<FacturaAnulacionSimulacion> SimularAnulacionAsync(
        Guid id,
        Guid usuario,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var factura = await CargarAsync(id, ct);

        var asignaciones = await AsignacionesVivasDeFacturaAsync(id, ct);
        var cobrosIds = asignaciones.Select(a => a.CobroId).Distinct().ToArray();

        // Todas las asignaciones de esos cobros, no solo las de esta factura: hace falta saber si al
        // cobro le queda algo vivo por lo otro lado antes de decir que se anula.
        List<CobroAsignacion> todas = cobrosIds.Length == 0
            ? []
            : await contexto.CobroAsignacion
                .Include(a => a.Factura)
                .Include(a => a.Cobro)
                .Where(a => cobrosIds.Contains(a.CobroId))
                .ToListAsync(ct);

        var anulanEntero = new List<CobroAfectadoPorFactura>();
        var soloSeTocan = new List<CobroAfectadoPorFactura>();

        foreach (var cobro in todas.Where(a => a.Cobro is not null).Select(a => a.Cobro!).Distinct())
        {
            var suyas = todas.Where(a => a.CobroId == cobro.Id).ToList();
            var mias = suyas.Where(a => a.FacturaId == id).ToList();
            var quedanVivas = suyas.Any(a => a.Vigente && a.FacturaId != id);

            var seAnula = !quedanVivas && cobro.Estado == EstadoCobro.Registrado;

            var registro = new CobroAfectadoPorFactura(
                cobro.Id,
                cobro.FechaEfectiva,
                cobro.MonedaRecibida,
                cobro.ImporteRecibido,
                Redondeo.Importe(mias.Sum(a => a.Importe)),
                // Solo se cuenta lo que sigue vigente en la moneda de esta factura: sumar importes de
                // monedas distintas daría un número que no significa nada.
                Redondeo.Importe(suyas.Where(a => a.Vigente && a.FacturaId != id && a.Factura?.Moneda == factura.Moneda).Sum(a => a.Importe)),
                seAnula ? SaldoQueCambia(cobro) : 0m,
                seAnula);

            if (seAnula)
            {
                anulanEntero.Add(registro);
            }
            else
            {
                soloSeTocan.Add(registro);
            }
        }

        return new FacturaAnulacionSimulacion(
            factura.Id,
            factura.Numero,
            factura.Estado,
            factura.NetoACobrar,
            await contexto.ComprobanteFiscal.AnyAsync(c => c.FacturaId == id, ct),
            true,
            anulanEntero,
            soloSeTocan);
    }

    /// <summary>
    /// Duplica una factura en borrador (F3-08, §3.8).
    /// <para>
    /// <b>Copia el documento, no su historia.</b> Van cliente, unidad, contrato, serie, moneda,
    /// líneas y sus tipos de ITBIS y retención. No van el número, el comprobante, los cobros, los
    /// envíos, la retención recibida, la referencia de e-factura ni la proforma de origen: eso ya
    /// pasó y pasó una vez. Los porcentajes de ITBIS y retención se vuelven a leer de la
    /// configuración vigente, que es lo mismo que hace una factura nueva.
    /// </para>
    /// </summary>
    public async Task<FacturaDuplicacionRespuesta> DuplicarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var origen = await CargarAsync(id, ct);

        // Una factura periódica no se puede duplicar: su clave (contrato, periodo, concepto) es única
        // mientras no esté anulada (§3.8, F3-05), así que la copia chocaría con ella misma. El camino
        // es anular la que hizo el trabajo anterior y emitir otra, que es justo lo que hace la
        // anulación al liberar la clave.
        if (origen.PeriodoInicio is not null)
        {
            throw new InvalidOperationException(
                $"La factura '{id:D}' es de periodo ({origen.PeriodoInicio:yyyy-MM-dd} a "
                    + $"{origen.PeriodoFin:yyyy-MM-dd}) y no se puede duplicar: no puede haber dos "
                    + "facturas del mismo contrato, periodo y concepto. Si la anterior está mal, "
                    + "anúlala y emite la correcta.");
        }

        var copia = new Factura
        {
            Estado = EstadoFactura.Borrador,
            SerieId = origen.SerieId,
            ClienteId = origen.ClienteId,
            UnidadId = origen.UnidadId,
            ContratoId = origen.ContratoId,
            Moneda = origen.Moneda,
            FechaVencimiento = origen.FechaVencimiento,
            Concepto = origen.Concepto,
            PeriodoInicio = origen.PeriodoInicio,
            PeriodoFin = origen.PeriodoFin,

            // Las navegaciones ya cargadas: sin ellas, el nombre del cliente y el número de serie
            // saldrían nulos en la respuesta de la copia.
            Cliente = origen.Cliente,
            Unidad = origen.Unidad,
            Serie = origen.Serie,
        };

        foreach (var linea in origen.Lineas.OrderBy(l => l.Posicion))
        {
            var tipoItbis = await CargarTipoItbisAsync(linea.TipoItbisId, ct);
            var tipoRetencion = await CargarTipoRetencionAsync(linea.TipoRetencionId, linea.Tipo, ct);

            if (tipoRetencion is not null)
            {
                ExigirAgenteRetencion(origen);
            }

            copia.Lineas.Add(ConstruirLinea(
                copia,
                new FacturaLineaPeticion
                {
                    Tipo = linea.Tipo,
                    Concepto = linea.Concepto,
                    Cantidad = linea.Cantidad,
                    PrecioUnitario = linea.PrecioUnitario,
                    TipoItbisId = linea.TipoItbisId,
                    TipoRetencionId = linea.TipoRetencionId,
                },
                tipoItbis,
                tipoRetencion,
                usuario));
        }

        RecalcularTotales(copia);

        // La fecha de emisión la pone la emisión, con el día de salida. La copia nace hoy por el
        // camino de siempre: se crea hoy y se emite cuando se emita.
        // TODO(A#): §3.8 dice que la copia «nace con la fecha del día»; aquí se copia el vencimiento y
        // la fecha de emisión queda para la emisión, que es lo que exige el modelo. Si lo que se
        // quería era el vencimiento de hoy, es una línea.
        var creada = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, copia,
            esAlta: true,
            accion: "factura.duplicada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => new
            {
                Factura = c.Id.ToString("D"),
                Origen = id.ToString("D"),
                Total = c.Total,
                Lineas = c.Lineas.Count,
            },
            ct: ct);

        // El aviso va después de guardar, y excluye la propia copia: lo que interesa es que haya otra
        // factura igual, no que la copia se avise a sí misma.
        var similares = await contexto.Factura.AsNoTracking()
            .Where(f => f.Id != creada.Id
                && f.ContratoId == origen.ContratoId
                && f.PeriodoInicio == origen.PeriodoInicio
                && f.Concepto == origen.Concepto
                && f.Estado != EstadoFactura.Anulada)
            .OrderByDescending(f => f.CreatedAt)
            .Take(20)
            .Select(f => new FacturaDuplicadaParecida(
                f.Id,
                f.Numero,
                f.Estado,
                f.PeriodoInicio,
                f.PeriodoFin,
                f.Total,
                f.Concepto))
            .ToListAsync(ct);

        return new FacturaDuplicacionRespuesta(creada, similares);
    }

    /// <summary>
    /// Convierte una proforma emitida en factura, con su misma moneda y sus mismos importes (F3-08,
    /// D7).
    /// <para>
    /// <b>La proforma deja de contar en la misma transacción en que nace la factura</b> (§3.7): su
    /// estado pasa a <c>Convertida</c> y su serie no suma a facturado ni a saldos, así que el mismo
    /// importe no aparece dos veces en la vista de cuentas. La factura se emite aquí y no queda en
    /// borrador, porque una proforma convertida sin factura emitida sí que dejaría de contar sin
    /// que nada la reemplazara.
    /// </para>
    /// <para>
    /// La serie destino es la fiscal activa —la 3 en la instalación normal— y el usuario puede
    /// cambiarla: se valida que exista, que esté activa y que sea fiscal, porque una factura no se
    /// emite en una serie de proformas.
    /// </para>
    /// </summary>
    public async Task<ProformaConversionRespuesta> ConvertirProformaAsync(
        Guid id,
        ProformaConversionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var proforma = await CargarAsync(id, ct);

        if (proforma.Serie?.Tipo != TipoSerie.Proforma)
        {
            throw new InvalidOperationException(
                $"El documento '{id:D}' no es una proforma: sale de una serie fiscal o normal, y eso "
                    + "no se convierte en otra cosa.");
        }

        if (proforma.Estado == EstadoFactura.Convertida)
        {
            throw new InvalidOperationException(
                $"La proforma '{id:D}' ya se convirtió y no se convierte dos veces.");
        }

        if (proforma.Estado != EstadoFactura.Emitida)
        {
            throw new InvalidOperationException(
                $"La proforma '{id:D}' está en {proforma.Estado} y no se convierte: convertir es pasar de "
                    + "un documento que ya salió a uno con valor fiscal. Emítela primero.");
        }

        var destino = await SerieDestinoAsync(peticion.SerieId, proforma.SerieId, ct);

        // D7: la moneda no se toca. Si la serie destino tiene moneda fija y es otra, el 409 lo dice y
        // el usuario elige otra serie.
        ExigirMonedaDeSerie(destino, proforma.Moneda);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var factura = new Factura
        {
            Estado = EstadoFactura.Borrador,
            SerieId = destino.Id,
            ClienteId = proforma.ClienteId,
            UnidadId = proforma.UnidadId,
            ContratoId = proforma.ContratoId,
            Moneda = proforma.Moneda,
            FechaVencimiento = proforma.FechaVencimiento,
            Concepto = proforma.Concepto,
            PeriodoInicio = proforma.PeriodoInicio,
            PeriodoFin = proforma.PeriodoFin,
            Cliente = proforma.Cliente,
            Unidad = proforma.Unidad,
            Serie = destino,
        };

        foreach (var linea in proforma.Lineas.OrderBy(l => l.Posicion))
        {
            var tipoItbis = await CargarTipoItbisAsync(linea.TipoItbisId, ct);
            var tipoRetencion = await CargarTipoRetencionAsync(linea.TipoRetencionId, linea.Tipo, ct);

            if (tipoRetencion is not null)
            {
                ExigirAgenteRetencion(proforma);
            }

            factura.Lineas.Add(ConstruirLinea(
                factura,
                new FacturaLineaPeticion
                {
                    Tipo = linea.Tipo,
                    Concepto = linea.Concepto,
                    Cantidad = linea.Cantidad,
                    PrecioUnitario = linea.PrecioUnitario,
                    TipoItbisId = linea.TipoItbisId,
                    TipoRetencionId = linea.TipoRetencionId,
                },
                tipoItbis,
                tipoRetencion,
                usuario));
        }

        RecalcularTotales(factura);

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: true,
            accion: "proforma.factura_creada",
            entidad: "factura",
            usuario,
            ip,
            mutacion: null,
            proyectar: AResumen,
            datosAuditoria: c => new
            {
                Factura = c.Id.ToString("D"),
                Proforma = id.ToString("D"),
                Serie = destino.Numero,
                Total = c.Total,
            },
            // La factura nueva, la proforma convertida y la emisión salen en el mismo commit: si la
            // factura no se crea, la proforma sigue contando como antes.
            enTransaccionDelLlamador: true,
            ct: ct);

        // La proforma se marca antes de emitir la factura, y las dos cosas están en esta transacción:
        // si la emisión falla por el numerador, la proforma sigue contando como antes.
        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, proforma,
            esAlta: false,
            accion: "proforma.convertida",
            entidad: "factura",
            usuario,
            ip,
            mutacion: null,
            proyectar: AResumen,
            datosAuditoria: c => new
            {
                Proforma = c.Id.ToString("D"),
                Factura = factura.Id.ToString("D"),
                NumeroProforma = c.Numero,
                Estado = c.Estado.ToString(),
            },
            antesDeGuardar: () =>
            {
                proforma.Estado = EstadoFactura.Convertida;
                proforma.ModificadaAt ??= reloj.GetUtcNow();
                proforma.ModificadaPor ??= usuario;
                return Task.CompletedTask;
            },
            enTransaccionDelLlamador: true,
            ct: ct);

        var emitida = await EmitirAsync(factura.Id, usuario, ip, null, ct);

        await transaccion.CommitAsync(ct);

        return new ProformaConversionRespuesta(emitida, proforma.Id, destino.Id);
    }

    /// <summary>
    /// Rehace una factura en otra moneda (F3-08, §3.8): la original se anula y nace un borrador en la
    /// moneda pedida con el importe tecleado.
    /// <para>
    /// <b>El importe lo pone quien lo teclea</b>, no un tipo de cambio calculado: cambiar la moneda de
    /// un documento que ya salió es una decisión comercial con su tipo de cambio y su momento, y la
    /// aplicación no puede decidir eso. La factura original no cambia de moneda ni se borra: queda
    /// anulada con su número (D28), y la respuesta lo dice para que no quede duda de qué pasó con ella.
    /// </para>
    /// </summary>
    public async Task<FacturaRehacerRespuesta> RehacerEnMonedaAsync(
        Guid id,
        RehacerMonedaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaAnular, $"factura/{id:D}", usuario, ct);

        var motivo = peticion.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException(
                "El motivo es obligatorio: la factura original queda anulada y su número no vuelve a "
                    + "liberarse, y eso se explica siempre.",
                nameof(peticion));
        }

        if (peticion.Total <= 0m)
        {
            // 400: el importe tecleado es el dato de la operación, no un conflicto con lo guardado.
            throw new ArgumentException(
                "El importe de la factura nueva tiene que ser mayor que cero.",
                nameof(peticion));
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await TomarCerrojoDeCobrosAsync(id, ct);

        var original = await CargarAsync(id, ct);
        ExigirAnulable(original);

        // La serie va trackeada por lo mismo que en la conversión de proforma: la factura nueva la
        // lleva como navegación y una serie suelta en el grafo de un alta se insertaría con ella.
        var serie = await contexto.Serie.SingleOrDefaultAsync(s => s.Id == peticion.SerieId, ct)
            ?? throw new KeyNotFoundException($"La serie '{peticion.SerieId:D}' no existe.");

        if (!serie.Activa)
        {
            throw new InvalidOperationException($"La serie {serie.Numero} está desactivada y no emite.");
        }

        ExigirMonedaDeSerie(serie, peticion.Moneda);

        var devueltos = new List<object>();

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, original,
            esAlta: false,
            accion: "factura.anulada",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => new
            {
                Factura = c.Id.ToString("D"),
                Numero = c.Numero,
                Motivo = motivo,
                Rehecha = true,
                Devuelto = devueltos,
            },
            antesDeGuardar: async () => await AnularEnTransaccionAsync(original, motivo, usuario, ip, devueltos, ct),
            enTransaccionDelLlamador: true,
            ct: ct);

        var nueva = new Factura
        {
            Estado = EstadoFactura.Borrador,
            SerieId = serie.Id,
            ClienteId = original.ClienteId,
            UnidadId = original.UnidadId,
            ContratoId = original.ContratoId,
            Moneda = peticion.Moneda,
            FechaVencimiento = original.FechaVencimiento,
            Concepto = original.Concepto,
            // BUG-033: el periodo se hereda para que ix_factura_periodo_unico siga protegiendo ese mes.
            PeriodoInicio = original.PeriodoInicio,
            PeriodoFin = original.PeriodoFin,
            Cliente = original.Cliente,
            Unidad = original.Unidad,
            Serie = serie,
        };

        // TODO(A#): §3.8 no dice cómo se reparte el importe tecleado entre las líneas. Se copia el
        // concepto de la original en una sola línea y se le pone el importe entero, porque inventar un
        // desglose sería inventar la factura. Si el caso real es un contrato con varias líneas, aquí
        // habrá que pedir el desglose.
        nueva.Lineas.Add(ConstruirLinea(
            nueva,
            new FacturaLineaPeticion
            {
                Tipo = TipoLineaFactura.Servicio,
                Concepto = original.Concepto,
                Cantidad = 1m,
                PrecioUnitario = peticion.Total,
            },
            null,
            null,
            usuario));

        RecalcularTotales(nueva);

        var resumen = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, nueva,
            esAlta: true,
            accion: "factura.rehecha",
            entidad: "factura",
            usuario,
            ip,
            mutacion: null,
            proyectar: AResumen,
            datosAuditoria: c => new
            {
                Factura = c.Id.ToString("D"),
                Origen = id.ToString("D"),
                Serie = serie.Numero,
                Moneda = c.Moneda.ToString(),
                Total = c.Total,
            },
            enTransaccionDelLlamador: true,
            ct: ct);

        await transaccion.CommitAsync(ct);

        return new FacturaRehacerRespuesta(original.Id, original.Numero, resumen);
    }

    /// <summary>
    /// BUG-019: la anulación cambia asignaciones y saldo a favor del contrato de la factura, así que toma el
    /// mismo cerrojo que los cobros antes de leer nada. Si la factura no existe, no hay nada que serializar
    /// y la carga posterior da el 404.
    /// </summary>
    private async Task TomarCerrojoDeCobrosAsync(Guid facturaId, CancellationToken ct)
    {
        var origen = await contexto.Factura.AsNoTracking()
            .Where(f => f.Id == facturaId)
            .Select(f => new { f.ContratoId, f.ClienteId })
            .FirstOrDefaultAsync(ct);

        if (origen is not null)
        {
            await CerrojoCobros.TomarAsync(contexto, origen.ContratoId, origen.ClienteId, ct);
        }
    }

    /// <summary>
    /// Lo que la anulación hace dentro de la transacción: la factura, su número, su comprobante fiscal y
    /// sus cobros. Es una sola unidad porque las cuatro cosas tienen que caer juntas.
    /// </summary>
    private async Task AnularEnTransaccionAsync(
        Factura factura,
        string motivo,
        Guid usuario,
        string ip,
        List<object> devueltos,
        CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();

        factura.Estado = EstadoFactura.Anulada;
        factura.AnuladoAt = ahora;
        factura.AnuladoPor = usuario;
        factura.MotivoAnulacion = motivo;
        factura.ModificadaAt ??= ahora;
        factura.ModificadaPor ??= usuario;

        // El número deja de estar disponible para ese documento pero sigue quemado: el contador no
        // retrocede y el siguiente número sigue siendo el de después (D28). El servicio de series usa
        // la transacción que ya está abierta.
        await series.AnularNumeroAsync(
            factura.SerieId,
            factura.Numero ?? throw new InvalidOperationException(
                $"La factura '{factura.Id:D}' no tiene número y no se puede anular."),
            motivo,
            usuario,
            ip,
            ct);

        var comprobante = await contexto.ComprobanteFiscal
            .SingleOrDefaultAsync(c => c.FacturaId == factura.Id, ct);

        if (comprobante is not null)
        {
            comprobante.Estado = EstadoComprobanteFiscal.Anulado;
            comprobante.AnuladoAt = ahora;
            comprobante.AnuladoPor = usuario;
            comprobante.MotivoAnulacion = motivo;
            comprobante.UpdatedAt = ahora;
            comprobante.UpdatedBy = usuario;
        }

        await AnularCobrosDeFacturaAsync(factura, motivo, usuario, ahora, devueltos, ct);
    }

    /// <summary>
    /// Deja de aplicar a esta factura los cobros que la pagaban (D6, F3-08) y anula los que se quedan
    /// sin nada que pagar.
    /// <para>
    /// <b>Solo esos.</b> Un cobro que sigue pagando otra factura no se toca: es dinero que llegó y que
    /// sigue cubriendo una deuda, y anular la factura que compartía cobro con ella no devuelve ese
    /// dinero.
    /// </para>
    /// </summary>
    private async Task AnularCobrosDeFacturaAsync(
        Factura factura,
        string motivo,
        Guid usuario,
        DateTimeOffset ahora,
        List<object> devueltos,
        CancellationToken ct)
    {
        var asignaciones = await contexto.CobroAsignacion
            .Include(a => a.Cobro)
            .Where(a => a.FacturaId == factura.Id && a.Vigente)
            .ToListAsync(ct);

        if (asignaciones.Count == 0)
        {
            return;
        }

        var cobrosIds = asignaciones.Select(a => a.CobroId).Distinct().ToArray();

        foreach (var asignacion in asignaciones)
        {
            asignacion.Vigente = false;
            asignacion.AnuladaAt = ahora;
            asignacion.AnuladaPor = usuario;
            asignacion.MotivoAnulacion = motivo;
            asignacion.UpdatedAt = ahora;
            asignacion.UpdatedBy = usuario;

            // BUG-020: lo que esa asignación pagaba no desaparece, queda devuelto en el cobro, en la
            // moneda recibida. Sin saldo a favor (decisión del responsable, 26/10/2026).
            var devuelto = Redondeo.Importe(asignacion.ImporteRecibido ?? asignacion.Importe);
            asignacion.Cobro!.ImporteDevuelto = Redondeo.Importe(asignacion.Cobro.ImporteDevuelto + devuelto);
            asignacion.Cobro.UpdatedAt = ahora;
            asignacion.Cobro.UpdatedBy = usuario;
            devueltos.Add(new
            {
                Cobro = asignacion.CobroId.ToString("D"),
                Moneda = asignacion.Cobro.MonedaRecibida.ToString(),
                Importe = devuelto,
            });
        }

        // Las asignaciones de esos mismos cobros, incluidas las que no son de esta factura: el filtro
        // se aplica en memoria y no en la consulta, porque la consulta no ve los cambios que aún no
        // se han guardado y daría por hecho que las de esta factura siguen vivas.
        var todas = await contexto.CobroAsignacion
            .Include(a => a.Cobro)
            .Where(a => cobrosIds.Contains(a.CobroId))
            .ToListAsync(ct);

        foreach (var cobro in todas.Where(a => a.Cobro is not null).Select(a => a.Cobro!).Distinct())
        {
            if (todas.Any(a => a.CobroId == cobro.Id && a.Vigente) || cobro.Estado != EstadoCobro.Registrado)
            {
                continue;
            }

            var saldo = cobro.ContratoId is { } cid
                ? await contexto.SaldoAFavor
                    .SingleOrDefaultAsync(s => s.ContratoId == cid && s.Moneda == cobro.MonedaRecibida, ct)
                : null;

            // BUG-032: si anular el cobro entero deja el saldo a favor en negativo, su crédito ya se gastó.
            await ServicioCobros.ExigirCreditoSinGastarAsync(contexto, cobro, saldo, ct);

            cobro.Estado = EstadoCobro.Anulado;
            cobro.AnuladoAt = ahora;
            cobro.AnuladoPor = usuario;
            cobro.MotivoAnulacion = $"Anulado junto con la factura {factura.Numero}: {motivo}";
            cobro.UpdatedAt = ahora;
            cobro.UpdatedBy = usuario;

            // El dinero que no se aplicó a ninguna factura vuelve a ser crédito del contrato, y el
            // crédito que este cobro consumió vuelve a estar disponible (§3.10.1).
            if (saldo is not null)
            {
                saldo.Importe = Redondeo.Importe(saldo.Importe + SaldoQueCambia(cobro));
                saldo.UpdatedAt = ahora;
                saldo.UpdatedBy = usuario;
            }
        }
    }

    /// <summary>
    /// Cuánto cambia el saldo a favor del contrato al anular este cobro, con su signo: vuelve el
    /// crédito que el cobro consumió y sale el excedente que dejó. Es el mismo cálculo que hace la
    /// anulación de cobro de F3-06, escrito al revés para que la suma sea la de aquí.
    /// </summary>
    private static decimal SaldoQueCambia(Cobro cobro) =>
        Redondeo.Importe(cobro.SaldoFavorUsado - cobro.Excedente);

    private Task<List<CobroAsignacion>> AsignacionesVivasDeFacturaAsync(Guid id, CancellationToken ct) =>
        contexto.CobroAsignacion
            .Include(a => a.Cobro)
            .Where(a => a.FacturaId == id && a.Vigente)
            .ToListAsync(ct);

    /// <summary>
    /// Serie en la que se convierte una proforma: la que pidió el usuario, o la fiscal activa —la 3 si
    /// existe, que es la instalación normal—. Se comprueba que sea fiscal y que esté activa: convertir
    /// en una serie de proformas dejaría la factura en el mismo sitio del que ha salido.
    /// </summary>
    /// <remarks>
    /// La serie viene <b>sin <c>AsNoTracking</c></b> a propósito: la factura nueva la lleva como
    /// navegación para que su número salga en la respuesta, y una serie suelta que entra en el grafo de
    /// un alta se inserta con ella. Trackeada, EF la reconoce como existente y la respeta.
    /// </remarks>
    private async Task<Serie> SerieDestinoAsync(Guid? serieId, Guid serieProformaId, CancellationToken ct)
    {
        var destino = serieId is { } id
            ? await contexto.Serie.SingleOrDefaultAsync(s => s.Id == id, ct)
                ?? throw new KeyNotFoundException($"La serie '{id:D}' no existe.")
            : await contexto.Serie
                .Where(s => s.Activa && s.Tipo == TipoSerie.Fiscal)
                .OrderBy(s => s.Numero == 3 ? 0 : 1)
                .ThenBy(s => s.Numero)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    "No hay ninguna serie fiscal activa: crea una antes de convertir una proforma "
                        + "(Configuración → Series).");

        if (destino.Id == serieProformaId)
        {
            throw new InvalidOperationException(
                $"La serie {destino.Numero} es la propia de la proforma y una factura no se emite en "
                    + "una serie de proformas.");
        }

        if (!destino.Activa)
        {
            throw new InvalidOperationException($"La serie {destino.Numero} está desactivada y no emite.");
        }

        if (destino.Tipo != TipoSerie.Fiscal)
        {
            throw new InvalidOperationException(
                $"La serie {destino.Numero} no es fiscal: una factura no se emite en una serie "
                    + "histórica o de proformas.");
        }

        return destino;
    }

    /// <summary>
    /// Una factura solo se modifica si salió sin comprobante fiscal. Anulada no se modifica (lo que
    /// salió no se reescribe) y convertida tampoco: una proforma convertida ya está detrás de una
    /// factura.
    /// </summary>
    private static void ExigirModificable(Factura factura)
    {
        if (factura.Estado == EstadoFactura.Emitida)
        {
            return;
        }

        var porque = factura.Estado == EstadoFactura.Borrador
            ? "está en borrador y se reescribe entera con sus líneas"
            : factura.Estado == EstadoFactura.Anulada
                ? "está anulada y lo que salió no se reescribe"
                : "es una proforma ya convertida y lo que se modifica es la factura que salió de ella";

        throw new InvalidOperationException($"La factura '{factura.Id:D}' {porque}.");
    }

    /// <summary>
    /// Solo se anula lo que salió con número. Un borrador no se anula porque no ha salido: se
    /// descarta, y por eso este camino no existe para él.
    /// </summary>
    private static void ExigirAnulable(Factura factura)
    {
        switch (factura.Estado)
        {
            case EstadoFactura.Emitida:
                return;
            case EstadoFactura.Borrador:
                throw new InvalidOperationException(
                    $"La factura '{factura.Id:D}' está en borrador y no tiene número, así que no hay "
                        + "nada que anular ni que conservar: anulada, se descarta y se vuelve a hacer.");
            case EstadoFactura.Anulada:
                throw new InvalidOperationException(
                    $"La factura '{factura.Id:D}' ya está anulada y no se anula dos veces.");
            default:
                throw new InvalidOperationException(
                    $"La factura '{factura.Id:D}' es una proforma convertida: lo que se anula es la "
                        + "factura que salió de ella.");
        }
    }

    /// <summary>
    /// Los campos editables de una línea de modificación, en la forma que espera
    /// <see cref="AplicarLinea"/>. Existe para que el cálculo de una línea emitida sea exactamente el
    /// mismo que el de una línea de borrador, y no una copia que se pueda quedar atrás.
    /// </summary>
    private static FacturaLineaPeticion AComoLinea(FacturaLineaModificacionPeticion cambio) => new()
    {
        Tipo = cambio.Tipo,
        Concepto = cambio.Concepto,
        Cantidad = cambio.Cantidad,
        PrecioUnitario = cambio.PrecioUnitario,
        TipoItbisId = cambio.TipoItbisId,
        TipoRetencionId = cambio.TipoRetencionId,
    };

    // ---------------------------------------------------------------- referencia e-factura

    /// <summary>
    /// Anota la factura con la referencia de su e-factura externa (D5, §3.7).
    /// <para>
    /// <b>Texto y nada más</b>: la aplicación no emite e-facturas, así que no hay nada que validar ni
    /// contra qué comprobar. Es el número de un documento emitido en otra aplicación, y lo único que
    /// se hace con él es guardarlo y imprimirlo en el PDF.
    /// </para>
    /// <para>
    /// Se puede cambiar mientras la factura no esté anulada. En una anulada no: lo que salió no se
    /// reescribe, y una referencia escrita sobre un documento anulado daría una factura con dos
    /// documentos fiscales detrás. Anular y emitir otra es el camino (F3-08).
    /// </para>
    /// </summary>
    public async Task<FacturaRespuesta> ReferenciaEfacturaAsync(
        Guid id,
        ReferenciaEfacturaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaEmitir, $"factura/{id:D}", usuario, ct);

        var referencia = string.IsNullOrWhiteSpace(peticion.Referencia)
            ? null
            : peticion.Referencia.Trim();

        var factura = await CargarAsync(id, ct);

        if (factura.Estado == EstadoFactura.Anulada)
        {
            throw new InvalidOperationException(
                $"La factura '{id:D}' está anulada y no admite referencia de e-factura: lo que salió "
                    + "no se reescribe.");
        }

        var anterior = factura.ReferenciaEfactura;
        factura.ReferenciaEfactura = referencia;

        var respuesta = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: "factura.referencia_efactura",
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            // El valor anterior va en la auditoría: cambiar la referencia de un documento ya
            // emitido es justo el caso en el que hace falta saber cuál estaba antes.
            datosAuditoria: c => JsonSerializer.Serialize(new
            {
                Factura = c.Id.ToString("D"),
                Anterior = anterior,
                Nueva = referencia,
            }),
            enTransaccionDelLlamador: false,
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);

        return respuesta;
    }

    // ---------------------------------------------------------------- carga y validaciones

    /// <summary>
    /// La factura con sus líneas y lo que las alimenta. Sin <c>AsNoTracking</c>: la emisión cambia
    /// la fila que ha cargado y necesita que EF la vea marcada como modificada.
    /// <para>
    /// F3-04: de las líneas se incluye el tipo de retención, que es el que da el nombre que sale en el
    /// detalle. El de ITBIS sigue sin incluirse —lo dejó así F3-02 y no es de esta tarea—, así que
    /// <c>tipoItbisNombre</c> sale nulo en el detalle y no en la lista.
    /// </para>
    /// </summary>
    private async Task<Factura> CargarAsync(Guid id, CancellationToken ct)
    {
        var factura = await contexto.Factura
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie)
            .Include(f => f.Lineas)
            .ThenInclude(l => l.TipoRetencion)
            .SingleOrDefaultAsync(f => f.Id == id, ct);

        return factura is null
            ? throw new KeyNotFoundException($"La factura '{id:D}' no existe.")
            : factura;
    }

    /// <summary>
    /// Carga para el PDF (F4-01): además de <see cref="CargarAsync"/>, el contrato con su facturadora.
    /// <para>
    /// Va aparte y no se mete en <see cref="CargarAsync"/> porque esta carga es solo de lectura y
    /// quella la usan todos los caminos de escritura: añadirle una navegación más significaría un
    /// <c>JOIN</c> extra en cada emisión, cada cambio de línea y cada anulación para algo que solo
    /// necesita el documento.
    /// </para>
    /// </summary>
    private async Task<Factura> CargarParaDocumentoAsync(Guid id, CancellationToken ct)
    {
        var factura = await contexto.Factura
            .AsNoTracking()
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie)
            .Include(f => f.Contrato)
            .ThenInclude(c => c!.Facturadora)
            .Include(f => f.Lineas)
            .SingleOrDefaultAsync(f => f.Id == id, ct);

        return factura is null
            ? throw new KeyNotFoundException($"La factura '{id:D}' no existe.")
            : factura;
    }

    private async Task<Serie> CargarSerieAsync(Guid serieId, CancellationToken ct) =>
        await contexto.Serie.AsNoTracking().SingleOrDefaultAsync(s => s.Id == serieId, ct)
            ?? throw new KeyNotFoundException($"La serie '{serieId:D}' no existe.");

    /// <summary>
    /// Cliente, unidad y contrato tienen que existir: se valida antes de escribir para que el error
    /// sea un 409 con el motivo y no una violación de clave foránea.
    /// </summary>
    private async Task ExigirReferenciasAsync(FacturaPeticion peticion, CancellationToken ct)
    {
        var clienteExiste = await contexto.Cliente
            .AsNoTracking()
            .AnyAsync(c => c.Id == peticion.ClienteId, ct);

        if (!clienteExiste)
        {
            throw new KeyNotFoundException($"El cliente '{peticion.ClienteId:D}' no existe.");
        }

        if (peticion.UnidadId is { } unidadId
            && !await contexto.Unidad.AsNoTracking().AnyAsync(u => u.Id == unidadId, ct))
        {
            throw new KeyNotFoundException($"La unidad '{unidadId:D}' no existe.");
        }

        if (peticion.ContratoId is { } contratoId
            && !await contexto.Contrato.AsNoTracking().AnyAsync(c => c.Id == contratoId, ct))
        {
            throw new KeyNotFoundException($"El contrato '{contratoId:D}' no existe.");
        }
    }

    /// <summary>
    /// Una serie con moneda fija (la 1 en USD, la 2 en DOP) no admite otra moneda (D7). La serie de
    /// moneda libre —fiscal, proforma e histórica— guarda la que le pidan.
    /// </summary>
    private static void ExigirMonedaDeSerie(Serie serie, Moneda moneda)
    {
        if (serie.Moneda is { } fija && fija != moneda)
        {
            throw new InvalidOperationException(
                $"La serie {serie.Numero} es de {fija} y no admite una factura en {moneda}. "
                    + "Usa la serie de la otra moneda o una serie de moneda libre.");
        }
    }

    private static void ExigirBorrador(Factura factura)
    {
        if (factura.Estado != EstadoFactura.Borrador)
        {
            throw new InvalidOperationException(
                $"La factura '{factura.Id:D}' está en {factura.Estado} y ya no se toca. "
                    + "Una factura emitida se anula y se emite otra (F3-08).");
        }
    }

    /// <summary>
    /// Segunda puerta del permiso. El endpoint lo pide con <c>[Accion]</c>, pero el atributo
    /// protege la entrada por HTTP y nada más: F3-05 emite desde un trabajo, sin sesión ni filtro,
    /// y esa comprobación sigue en pie (igual que <c>ExigirEstadoAsync</c> de F2-05).
    /// </summary>
    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    // ---------------------------------------------------------------- líneas

    /// <summary>
    /// El tipo de ITBIS tiene que existir y estar activo: uno desactivado no se puede elegir en una
    /// factura nueva (§3.9). El porcentaje se copia a la línea y a partir de ahí es suyo.
    /// </summary>
    private async Task<TipoItbis?> CargarTipoItbisAsync(Guid? tipoItbisId, CancellationToken ct)
    {
        if (tipoItbisId is not { } id)
        {
            return null;
        }

        var tipo = await contexto.TipoItbis.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException($"El tipo de ITBIS '{id:D}' no existe.");

        if (!tipo.Activo)
        {
            throw new InvalidOperationException($"El tipo de ITBIS '{tipo.Nombre}' está desactivado.");
        }

        return tipo;
    }

    /// <summary>
    /// El tipo de retención tiene que existir y estar activo, y solo se admite en una línea de
    /// retención: una línea de renta con un tipo de retención no significaría nada (§3.9, D14).
    /// </summary>
    private async Task<TipoRetencion?> CargarTipoRetencionAsync(
        Guid? tipoRetencionId,
        TipoLineaFactura tipo,
        CancellationToken ct)
    {
        var esRetencion = tipo == TipoLineaFactura.Retencion;

        if (tipoRetencionId is not { } id)
        {
            if (esRetencion)
            {
                throw new InvalidOperationException(
                    "Una línea de retención necesita su tipo de retención: de ahí salen el porcentaje y "
                        + "la base sobre la que se calcula (Configuración → Tipos de retención).");
            }

            return null;
        }

        if (!esRetencion)
        {
            throw new InvalidOperationException(
                $"Una línea de tipo {tipo} no lleva tipo de retención. El tipo de retención solo va en "
                    + "una línea de tipo Retencion.");
        }

        var tipoRetencion = await contexto.TipoRetencion.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException($"El tipo de retención '{id:D}' no existe.");

        if (!tipoRetencion.Activo)
        {
            throw new InvalidOperationException(
                $"El tipo de retención '{tipoRetencion.Nombre}' está desactivado.");
        }

        return tipoRetencion;
    }

    /// <summary>
    /// <b>Solo los clientes agente de retención admiten líneas de retención</b> (§3.9, D14).
    /// <para>
    /// No se ignoran ni se quitan: una factura con una línea de retención a un cliente que no es
    /// agente se rechaza, porque aceptarla en silencio dejaría una factura que dice que retuvo y un
    /// cliente al que no se le puede retener. El mensaje dice qué hacer, no solo que no.
    /// </para>
    /// </summary>
    private static void ExigirAgenteRetencion(Factura factura)
    {
        if (factura.Cliente is { AgenteRetencion: false })
        {
            throw new InvalidOperationException(
                $"El cliente '{factura.Cliente.Nombre}' no es agente de retención y su factura no admite "
                    + "líneas de retención (Configuración → Clientes → Agente de retención). Si lo es, "
                    + " márcalo allí antes de añadir la línea.");
        }
    }

    private FacturaLinea ConstruirLinea(
        Factura factura,
        FacturaLineaPeticion peticion,
        TipoItbis? tipoItbis,
        TipoRetencion? tipoRetencion,
        Guid usuario)
    {
        var linea = new FacturaLinea
        {
            Id = Guid.NewGuid(),
            Factura = factura,
            Posicion = factura.Lineas.Count == 0
                ? 1
                : factura.Lineas.Max(l => l.Posicion) + 1,
            CreatedAt = reloj.GetUtcNow(),
            CreatedBy = usuario,
        };

        AplicarLinea(linea, peticion, tipoItbis, tipoRetencion);

        return linea;
    }

    private static void AplicarLinea(
        FacturaLinea linea,
        FacturaLineaPeticion peticion,
        TipoItbis? tipoItbis,
        TipoRetencion? tipoRetencion)
    {
        linea.Tipo = peticion.Tipo;
        linea.Concepto = peticion.Concepto.Trim();
        linea.TipoItbisId = tipoItbis?.Id;
        linea.TipoRetencionId = tipoRetencion?.Id;

        // El porcentaje y la base se congelan aquí (D14): a partir de este momento la factura dice
        // "esto lleva esta retención sobre esta base", y cambiar el tipo mañana no la cambia a ella.
        // El importe, en cambio, no se congela: sale de la base, que es un importe de la factura y
        // cambia cuando se añaden o se quitan líneas. Lo recalcula RecalcularTotales.
        linea.PorcentajeRetencion = tipoRetencion is null
            ? null
            : Redondeo.Porcentaje(tipoRetencion.Porcentaje);
        linea.BaseRetencion = tipoRetencion?.Base;

        if (tipoRetencion is not null)
        {
            // Una línea de retención no lleva cantidad ni precio: es «una retención de este tipo», y
            // su importe sale del porcentaje sobre la base. La cantidad a uno es lo que hace que el
            // importe de la línea sea el importe retenido en vez de un uno por uno de nada, y el
            // precio lo pisa RecalcularTotales con el importe ya redondeado.
            linea.Cantidad = 1m;
            linea.PrecioUnitario = 0m;
            linea.Importe = 0m;
            linea.ItbisImporte = 0m;
            return;
        }

        // La cantidad vive en numeric(18,4) y el precio en numeric(18,2): se redondea a lo que
        // guarda la columna, para que el importe que se calcula y el que se lee sean el mismo.
        linea.Cantidad = Redondeo.Porcentaje(peticion.Cantidad);
        linea.PrecioUnitario = Redondeo.Importe(peticion.PrecioUnitario);

        var calculo = CalculoImpuestosLinea.Calcular(
            linea.Cantidad,
            linea.PrecioUnitario,
            tipoItbis?.Porcentaje);

        ExigirSigno(peticion.Tipo, calculo.Importe);

        linea.Importe = calculo.Importe;
        // El porcentaje se congela aquí (D14): a partir de este momento la factura dice "este importe
        // lleva este impuesto", y cambiar el tipo de ITBIS mañana no la cambia a ella.
        linea.PorcentajeItbis = tipoItbis is null
            ? null
            : Redondeo.Porcentaje(tipoItbis.Porcentaje);
        linea.ItbisImporte = tipoItbis is null ? 0m : calculo.ItbisImporte;
    }

    /// <summary>
    /// El signo lo pone el tipo de la línea: un descuento resta y ninguna otra cosa lo hace. Sin
    /// esto, un descuento con cantidad positiva subiría la factura, que es justo el error que este
    /// dato no puede tener.
    /// <para>
    /// Una línea de retención (F3-04) nunca llega aquí: su importe no sale de cantidad por precio
    /// sino del porcentaje sobre la base, así que lo escribe <see cref="RecalcularTotales"/> y no
    /// quien la da de alta.
    /// </para>
    /// </summary>
    private static void ExigirSigno(TipoLineaFactura tipo, decimal importe)
    {
        if (tipo == TipoLineaFactura.Descuento)
        {
            if (importe > 0m)
            {
                throw new InvalidOperationException(
                    "Una línea de descuento va en negativo: pon la cantidad o el precio en negativo.");
            }

            return;
        }

        if (importe < 0m)
        {
            throw new InvalidOperationException(
                $"Una línea de tipo {tipo} no puede tener importe negativo. "
                    + "Para restar, usa una línea de tipo Descuento.");
        }
    }

    /// <summary>
    /// Los totales de la cabecera son la suma de lo que ya está redondeado línea a línea: el ITBIS de
    /// la factura es <c>Σ ItbisImporte</c> y no el ITBIS del subtotal (D27, §3.8). El descuento es
    /// una línea negativa y el subtotal es la suma algebraica.
    /// <para>
    /// <b>Las líneas de retención quedan fuera del subtotal y del total</b> (F3-04, §3.9). Retener no
    /// baja lo facturado: lo que baja es el neto a cobrar, que es <c>Total - Retencion</c>. Si la
    /// retención entrara en el subtotal, el total bajaría también y el cliente vería una factura más
    /// corta en vez de una factura con un netting en medio.
    /// </para>
    /// <para>
    /// Por eso el cálculo va en dos pasadas. La primera suma las líneas de servicio y con ellas
    /// calcula la base; la segunda aplica a cada línea de retención el porcentaje sobre esa base y
    /// vuelve a sumar. En una sola pasada la retención no tendría base, porque la base depende de las
    /// líneas y las líneas incluyen la retención. Y como el importe retenido cambia cada vez que se
    /// toca cualquier línea, esto se recalcula siempre y no solo al añadir la retención: si después
    /// se añade una línea de renta, la retención anterior deja de estar bien calculada y hay que
    /// rehacerla.
    /// </para>
    /// </summary>
    private static void RecalcularTotales(Factura factura)
    {
        var lineasServicio = factura.Lineas.Where(l => l.Tipo != TipoLineaFactura.Retencion).ToList();
        var retenciones = factura.Lineas.Where(l => l.Tipo == TipoLineaFactura.Retencion).ToList();

        var subtotal = Redondeo.Importe(lineasServicio.Sum(l => l.Importe));
        var itbis = Redondeo.Importe(lineasServicio.Sum(l => l.ItbisImporte));
        var total = Redondeo.Importe(subtotal + itbis);

        foreach (var linea in retenciones)
        {
            // El precio unitario se pone al importe ya redondeado para que la línea siga cuadrando
            // con la fórmula que usa todo lo demás (cantidad por precio, D27), y el importe sale en
            // negativo porque es lo que se le resta al cliente.
            var importe = -CalculoRetencion.Importe(
                linea.BaseRetencion ?? BaseRetencion.Total,
                subtotal,
                itbis,
                total,
                linea.PorcentajeRetencion ?? 0m);

            linea.Importe = importe;
            linea.PrecioUnitario = Redondeo.Importe(importe);
        }

        factura.Subtotal = subtotal;
        factura.Itbis = itbis;
        factura.Total = total;
        factura.Retencion = Redondeo.Importe(retenciones.Sum(l => -l.Importe));
        factura.NetoACobrar = Redondeo.Importe(total - factura.Retencion);
    }

    /// <summary>
    /// Guarda el cambio de líneas con la factura como fila versionada: la auditoría cuelga del
    /// documento, que es lo que después se va a emitir.
    /// </summary>
    private Task<FacturaRespuesta> GuardarLineasAsync(
        Factura factura,
        string accion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        Action aplicar,
        CancellationToken ct)
    {
        aplicar();
        RecalcularTotales(factura);

        return GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Factura, factura,
            esAlta: false,
            accion: accion,
            entidad: "factura",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => DatosAuditoria(c),
            enriquecer: r => EnriquecerEscrituraAsync(r, ct),
            ct: ct);
    }

    // ---------------------------------------------------------------- proyecciones

    /// <summary>
    /// Rellena los campos derivados de §3.8 con lo que hay cobrado hoy. El cálculo está en
    /// <see cref="CalculoCobro"/> y lo pide <c>ServicioCobros</c>, que es quien sabe qué
    /// asignaciones y qué ajustes cuentan: un cobro anulado no cuenta, y eso no lo puede saber un
    /// lector de facturas por su cuenta.
    /// <para>
    /// La retención va con su propio campo y <b>tampoco se suma dentro de <c>Cobrado</c></b>: retener
    /// no es que el cliente haya puesto dinero, es que se le quita. Lo que sí hace es cerrar el
    /// pendiente, y por eso el cálculo de estado la cuenta (F3-04, §3.9).
    /// </para>
    /// </summary>
    private FacturaResumen ConCobro(FacturaResumen f, decimal cobrado, decimal ajustado, bool retencionSinComprobante)
    {
        var situacion = cobros.SituacionDeFactura(f.Total, f.Retencion, cobrado, ajustado, f.FechaVencimiento, f.Estado);

        return f with
        {
            Cobrado = situacion.Cobrado,
            Ajustado = situacion.Ajustado,
            Pendiente = situacion.Pendiente,
            EstadoCobro = situacion.Estado,
            Vencida = situacion.Vencida,
            RetencionSinComprobante = retencionSinComprobante,
        };
    }

    /// <summary>Igual que <see cref="ConCobro(FacturaResumen, decimal, decimal, bool)"/>, para el detalle.</summary>
    private FacturaRespuesta ConCobro(FacturaRespuesta f, decimal cobrado, decimal ajustado, bool retencionSinComprobante)
    {
        var situacion = cobros.SituacionDeFactura(f.Total, f.Retencion, cobrado, ajustado, f.FechaVencimiento, f.Estado);

        return f with
        {
            Cobrado = situacion.Cobrado,
            Ajustado = situacion.Ajustado,
            Pendiente = situacion.Pendiente,
            EstadoCobro = situacion.Estado,
            Vencida = situacion.Vencida,
            RetencionSinComprobante = retencionSinComprobante,
        };
    }

    private static void Aplicar(Factura factura, FacturaPeticion peticion)
    {
        factura.ClienteId = peticion.ClienteId;
        factura.UnidadId = peticion.UnidadId;
        factura.ContratoId = peticion.ContratoId;
        factura.SerieId = peticion.SerieId;
        factura.Moneda = peticion.Moneda;
        factura.FechaVencimiento = peticion.FechaVencimiento;
        factura.Concepto = peticion.Concepto.Trim();
    }

    private static FacturaResumen AResumen(Factura f) => new(
        f.Id,
        f.ClienteId,
        f.Cliente?.Nombre,
        f.UnidadId,
        f.Unidad?.Nombre,
        f.ContratoId,
        f.SerieId,
        f.Serie?.Numero ?? 0,
        f.Numero,
        f.Estado,
        f.Serie?.Tipo ?? TipoSerie.Normal,
        f.Moneda,
        f.FechaEmision,
        f.FechaVencimiento,
        f.Concepto,
        f.Subtotal,
        f.Itbis,
        f.Total,
        f.Retencion,
        f.NetoACobrar,
        f.Lineas.Count,
        f.CreatedAt)
    {
        // La referencia de e-factura (D5) va en la lista porque quien mira una factura emitida
        // necesita ver de un vistazo si tiene documento fiscal externo detrás.
        ReferenciaEfactura = f.ReferenciaEfactura,
    };

    /// <summary>
    /// Detalle de la factura con sus líneas.
    /// <para>
    /// <b>Sale sin <c>cobrado</c> ni <c>pendiente</c> en los caminos de escritura</b> (crear,
    /// editar líneas, emitir) porque ahí el cero es el valor real: una factura en borrador no admite
    /// cobros y una recién emitida no puede tenerlos, así que preguntarlo a la base solo añadiría un
    /// viaje por operación. Quien lea una factura emitida por HTTP pasa por
    /// <see cref="LeerAsync"/>, que sí lo resuelve.
    /// </para>
    /// </summary>
    private static FacturaRespuesta ARespuesta(Factura f) => new(
        f.Id,
        f.ClienteId,
        f.Cliente?.Nombre,
        f.UnidadId,
        f.Unidad?.Nombre,
        f.ContratoId,
        f.SerieId,
        f.Serie?.Numero ?? 0,
        f.Numero,
        f.Estado,
        f.Serie?.Tipo ?? TipoSerie.Normal,
        f.Moneda,
        f.FechaEmision,
        f.FechaVencimiento,
        f.Concepto,
        f.Subtotal,
        f.Itbis,
        f.Total,
        f.Retencion,
        f.NetoACobrar,
        f.TipoCambioValor,
        f.TipoCambioFecha,
        f.TipoCambioId,
        f.CreatedAt,
        f.UpdatedAt,
        [.. f.Lineas.OrderBy(l => l.Posicion).Select(ALinea)])
    {
        ReferenciaEfactura = f.ReferenciaEfactura,
    };

    private static FacturaLineaRespuesta ALinea(FacturaLinea l) => new(
        l.Id,
        l.Posicion,
        l.Tipo,
        l.Concepto,
        l.Cantidad,
        l.PrecioUnitario,
        l.Importe,
        l.TipoItbisId,
        l.TipoItbis?.Nombre,
        l.PorcentajeItbis,
        l.ItbisImporte,
        l.TipoRetencionId,
        l.TipoRetencion?.Nombre,
        l.PorcentajeRetencion,
        l.BaseRetencion);

    /// <summary>
    /// Lo que va en la auditoría firmada. Ni el tipo de cambio ajeno ni nada que el documento no
    /// lleva: la firma cubre esto, y una copia congelada de la línea no aporta nada.
    /// <para>
    /// El comprobante va con su número porque la emisión de F3-03 los saca a los dos en la misma
    /// transacción: una auditoría que dice "se emitió el número 7" y otra que dice "el comprobante 4
    /// salió con ella" tienen que poder leerse juntas.
    /// </para>
    /// </summary>
    private static object DatosAuditoria(Factura f, int? numeroComprobante = null) => new
    {
        f.Id,
        Cliente = f.ClienteId.ToString("D"),
        Unidad = f.UnidadId?.ToString("D"),
        Contrato = f.ContratoId?.ToString("D"),
        Serie = f.SerieId.ToString("D"),
        f.Numero,
        NumeroComprobante = numeroComprobante,
        Estado = f.Estado.ToString(),
        Moneda = f.Moneda.ToString(),
        Emision = f.FechaEmision?.ToString("yyyy-MM-dd"),
        Vencimiento = f.FechaVencimiento.ToString("yyyy-MM-dd"),
        // F3-05: el periodo es lo que distingue una factura periódica de una tecleada a mano, así que
        // va en la auditoría junto al concepto que forma la clave de no duplicado.
        PeriodoInicio = f.PeriodoInicio?.ToString("yyyy-MM-dd"),
        PeriodoFin = f.PeriodoFin?.ToString("yyyy-MM-dd"),
        f.Concepto,
        f.Subtotal,
        f.Itbis,
        f.Total,
        f.Retencion,
        f.NetoACobrar,
        Lineas = f.Lineas.Count,
    };
}
