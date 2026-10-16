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
/// <item>
/// <b>Emitida, no se toca.</b> Una factura en borrador no tiene número, ni comprobante, ni deuda, así
/// que se reescribe entera. Emitida, lo que hay es anular y emitir otra (F3-08).
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
    ServicioTiposCambio tiposCambio,
    ServicioCobros cobros,
    IPolicyService politica,
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

        return pagina with
        {
            Data = [.. pagina.Data.Select(f => ConCobro(
                f,
                cobrados.GetValueOrDefault(f.Id),
                ajustados.GetValueOrDefault(f.Id)))],
        };
    }

    public async Task<FacturaRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var factura = await CargarAsync(id, ct);

        var cobrados = await cobros.CobradoPorFacturaAsync([factura.Id], ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync([factura.Id], ct);

        return ConCobro(ARespuesta(factura), cobrados.GetValueOrDefault(factura.Id), ajustados.GetValueOrDefault(factura.Id));
    }

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
        CancellationToken ct)
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
        var linea = ConstruirLinea(factura, peticion, tipoItbis, usuario);

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
        AplicarLinea(linea, peticion, tipoItbis);

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
    /// </summary>
    public async Task<FacturaRespuesta> EmitirAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaEmitir, $"factura/{id:D}", usuario, ct);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

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
            proyectar: ARespuesta,
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
            },
            enTransaccionDelLlamador: true,
            ct: ct);

        await transaccion.CommitAsync(ct);

        return respuesta;
    }

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
            ct: ct);

        return respuesta;
    }

    // ---------------------------------------------------------------- carga y validaciones

    /// <summary>
    /// La factura con sus líneas y lo que las alimenta. Sin <c>AsNoTracking</c>: la emisión cambia
    /// la fila que ha cargado y necesita que EF la vea marcada como modificada.
    /// </summary>
    private async Task<Factura> CargarAsync(Guid id, CancellationToken ct)
    {
        var factura = await contexto.Factura
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie)
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

    private static FacturaLinea ConstruirLinea(
        Factura factura,
        FacturaLineaPeticion peticion,
        TipoItbis? tipoItbis,
        Guid usuario)
    {
        var linea = new FacturaLinea
        {
            Id = Guid.NewGuid(),
            Factura = factura,
            Posicion = factura.Lineas.Count == 0
                ? 1
                : factura.Lineas.Max(l => l.Posicion) + 1,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = usuario,
        };

        AplicarLinea(linea, peticion, tipoItbis);

        return linea;
    }

    private static void AplicarLinea(FacturaLinea linea, FacturaLineaPeticion peticion, TipoItbis? tipoItbis)
    {
        linea.Tipo = peticion.Tipo;
        linea.Concepto = peticion.Concepto.Trim();
        // La cantidad vive en numeric(18,4) y el precio en numeric(18,2): se redondea a lo que
        // guarda la columna, para que el importe que se calcula y el que se lee sean el mismo.
        linea.Cantidad = Redondeo.Porcentaje(peticion.Cantidad);
        linea.PrecioUnitario = Redondeo.Importe(peticion.PrecioUnitario);
        linea.TipoItbisId = tipoItbis?.Id;

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
    /// </summary>
    private static void RecalcularTotales(Factura factura)
    {
        var subtotal = Redondeo.Importe(factura.Lineas.Sum(l => l.Importe));
        var itbis = Redondeo.Importe(factura.Lineas.Sum(l => l.ItbisImporte));

        factura.Subtotal = subtotal;
        factura.Itbis = itbis;
        factura.Total = Redondeo.Importe(subtotal + itbis);
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
            ct: ct);
    }

    // ---------------------------------------------------------------- proyecciones

    /// <summary>
    /// Rellena los campos derivados de §3.8 con lo que hay cobrado hoy. El cálculo está en
    /// <see cref="CalculoCobro"/> y lo pide <c>ServicioCobros</c>, que es quien sabe qué
    /// asignaciones y qué ajustes cuentan: un cobro anulado no cuenta, y eso no lo puede saber un
    /// lector de facturas por su cuenta.
    /// <para>
    /// La retención va a cero porque la columna es de F3-04; el cálculo ya la tiene en cuenta.
    /// </para>
    /// </summary>
    private FacturaResumen ConCobro(FacturaResumen f, decimal cobrado, decimal ajustado)
    {
        var situacion = cobros.SituacionDeFactura(f.Total, cobrado, ajustado, f.FechaVencimiento);

        return f with
        {
            Cobrado = situacion.Cobrado,
            Ajustado = situacion.Ajustado,
            Pendiente = situacion.Pendiente,
            EstadoCobro = situacion.Estado,
            Vencida = situacion.Vencida,
        };
    }

    /// <summary>Igual que <see cref="ConCobro(FacturaResumen, decimal, decimal)"/>, para el detalle.</summary>
    private FacturaRespuesta ConCobro(FacturaRespuesta f, decimal cobrado, decimal ajustado)
    {
        var situacion = cobros.SituacionDeFactura(f.Total, cobrado, ajustado, f.FechaVencimiento);

        return f with
        {
            Cobrado = situacion.Cobrado,
            Ajustado = situacion.Ajustado,
            Pendiente = situacion.Pendiente,
            EstadoCobro = situacion.Estado,
            Vencida = situacion.Vencida,
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
        l.ItbisImporte);

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
        f.Concepto,
        f.Subtotal,
        f.Itbis,
        f.Total,
        Lineas = f.Lineas.Count,
    };
}
