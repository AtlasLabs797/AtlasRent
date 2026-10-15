using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cobros: registro, asignación a facturas, saldo a favor y anulación (F3-06,
/// docs/ESPECIFICACION.md §3.10.1).
/// <list type="bullet">
/// <item>
/// <b>La asignación por defecto es FIFO dentro del contrato y de la moneda del pago</b>
/// (§3.10.1, «la más antigua primero»), por vencimiento y a igualdad por emisión, con el
/// identificador como último desempate para que dos facturas del mismo día no den un orden que
/// cambie entre dos llamadas.
/// </item>
/// <item>
/// <b>La asignación es editable.</b> Quien registra el cobro puede mandar sus propias asignaciones y
/// entonces el servicio no propone nada: el dinero va a la factura que le digan. Lo que no puede es
/// asignar de más, ni a una factura de otro contrato, ni a una de otra moneda.
/// </item>
/// <item>
/// <b>El excedente es saldo a favor del contrato</b>, y el crédito se gasta solo cuando quien cobra
/// lo pide. Cobrar de más es una cosa y devolver el cobro es otra.
/// </item>
/// <item>
/// <b>Nada se borra.</b> Un cobro se anula con motivo y sus asignaciones dejan de contar sin
/// desaparecer de la tabla (AGENTS.md §6).
/// </item>
/// </list>
/// <para>
/// <b>Limitación conocida y temporal: un cobro solo asigna a facturas de la moneda recibida.</b>
/// Pagar deuda en dólares con pesos es el pago en otra moneda de §3.10.2, que es de F3-07 y añade a
/// <c>cobro_asignacion</c> el importe recibido y el cambio aplicado. Aquí eso se rechaza con un 409
/// que lo dice, en vez de convertir en silencio (D7: una factura nunca se convierte).
/// </para>
/// <para>
/// El servicio comprueba el permiso y no solo el endpoint, porque los trabajos de Hangfire llaman a
/// los servicios sin sesión HTTP ni filtro <c>[Accion]</c> (como en F3-02 y F3-05).
/// </para>
/// </summary>
public sealed class ServicioCobros(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioTiposCambio tiposCambio,
    IPolicyService politica,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<Cobro>, bool, IOrderedQueryable<Cobro>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            ["fechaEfectiva"] = (q, desc) => desc ? q.OrderByDescending(c => c.FechaEfectiva) : q.OrderBy(c => c.FechaEfectiva),
            ["importe"] = (q, desc) => desc ? q.OrderByDescending(c => c.ImporteRecibido) : q.OrderBy(c => c.ImporteRecibido),
            ["estado"] = (q, desc) => desc ? q.OrderByDescending(c => c.Estado) : q.OrderBy(c => c.Estado),
        };

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Listado paginado con filtros de cliente, contrato, moneda, estado y rango de fechas efectivas.
    /// Sin totales agregados: los informes son de §3.15 (F3-10).
    /// </summary>
    public Task<Pagina<CobroResumen>> ListarAsync(
        ConsultaLista paginacion,
        Guid? clienteId,
        Guid? contratoId,
        Moneda? moneda,
        EstadoCobro? estado,
        DateOnly? desde,
        DateOnly? hasta,
        CancellationToken ct)
    {
        IQueryable<Cobro> consulta = contexto.Cobro.AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.FormaPago)
            .Include(c => c.CuentaReceptora);

        if (clienteId is { } filtroCliente)
        {
            consulta = consulta.Where(c => c.ClienteId == filtroCliente);
        }

        if (contratoId is { } filtroContrato)
        {
            consulta = consulta.Where(c => c.ContratoId == filtroContrato);
        }

        if (moneda is { } filtroMoneda)
        {
            consulta = consulta.Where(c => c.MonedaRecibida == filtroMoneda);
        }

        if (estado is { } filtroEstado)
        {
            consulta = consulta.Where(c => c.Estado == filtroEstado);
        }

        if (desde is { } desdeFecha)
        {
            consulta = consulta.Where(c => c.FechaEfectiva >= desdeFecha);
        }

        if (hasta is { } hastaFecha)
        {
            consulta = consulta.Where(c => c.FechaEfectiva <= hastaFecha);
        }

        return ConsultaPaginada.PaginarAsync(consulta, Ordenes, "fechaEfectiva", paginacion, AResumen, ct);
    }

    public async Task<CobroRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var cobro = await CargarAsync(id, ct);

        return ARespuesta(cobro);
    }

    /// <summary>
    /// Suma de lo cobrado de cada factura por asignaciones <b>vigentes</b> de cobros registrados.
    /// <para>
    /// Es lo que rellena <c>cobrado</c>, <c>pendiente</c> y <c>estado_cobro</c> de las facturas
    /// (§3.8), y está en una sola consulta agrupada para que la vista de cuentas no sea una consulta
    /// por factura (§3.13). Las asignaciones de un cobro anulado no cuentan, que es lo mismo que
    /// haberlas borrado sin borrarlas.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> CobradoPorFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken ct)
    {
        if (facturaIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var ids = facturaIds.ToArray();

        return await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.Vigente && ids.Contains(a.FacturaId) && a.Cobro!.Estado == EstadoCobro.Registrado)
            .GroupBy(a => a.FacturaId)
            .Select(g => new { FacturaId = g.Key, Cobrado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Cobrado, ct);
    }

    /// <summary>
    /// Situación de cobro de una factura con lo que hay cobrado hoy. Es la proyección de los cuatro
    /// campos que el DTO de factura expone, y vive aquí para que la vista de cuentas (§3.13) y el
    /// panel de cobro no puedan discrepar sobre la misma factura.
    /// </summary>
    public SituacionCobroFactura SituacionDeFactura(decimal total, decimal cobrado, DateOnly vencimiento)
        => CalculoCobro.Calcular(total, 0m, cobrado, vencimiento, FechaDeNegocio());

    // ---------------------------------------------------------------- simulación

    /// <summary>
    /// Propuesta de asignación de un cobro <b>sin escribir nada</b>. Es lo mismo que hace el registro
    /// —mismas validaciones, mismo FIFO— pero sin transacción ni auditoría, y devuelve además la
    /// deuda que el cobro <b>no</b> va a cubrir: la del mismo contrato en otra moneda (§3.10.2, F3-07).
    /// <para>
    /// Devolver esa deuda a propósito es lo que permite a la interfaz decir «este cobro no lo cubre
    /// todo» en vez de dejar a alguien creyéndolo.
    /// </para>
    /// </summary>
    public async Task<CobroSimulacionRespuesta> SimularAsync(CobroPeticion peticion, CancellationToken ct)
    {
        var plan = await PlanificarAsync(peticion, ct);

        return new CobroSimulacionRespuesta(
            plan.Cobro.ClienteId,
            plan.Cobro.ContratoId,
            plan.Cobro.FechaEfectiva,
            plan.Cobro.MonedaRecibida,
            plan.Cobro.ImporteRecibido,
            plan.Cobro.SaldoFavorUsado,
            plan.CreditoDisponible,
            plan.Disponible,
            plan.TotalAsignado,
            plan.Cobro.Excedente,
            plan.ActualizaSaldo,
            [.. plan.Lineas.Select(l => l.Simulada())],
            plan.DeudaEnOtraMoneda);
    }

    // ---------------------------------------------------------------- escritura

    /// <summary>
    /// Registro de un cobro. El cobro, sus asignaciones y el saldo a favor entran en <b>un solo
    /// commit</b>: o se guarda el dinero con su reparto, o no se guarda nada.
    /// <para>
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve el cobro ya registrado en vez de
    /// registrar el mismo dinero dos veces.
    /// </para>
    /// </summary>
    public async Task<CobroRespuesta> RegistrarAsync(
        CobroPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.CobroRegistrar, $"cliente/{peticion.ClienteId:D}", usuario, ct);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var plan = await PlanificarAsync(peticion, ct);

        var respuesta = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cobro, plan.Cobro,
            esAlta: true,
            accion: "cobro.registrado",
            entidad: "cobro",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                // Las asignaciones cuelgan de la navegación del cobro, que todavía no está guardado:
                // salen en el mismo INSERT que él y en la misma transacción.
                plan.Cobro.Asignaciones.AddRange(plan.Lineas.Select(l => l.Entidad));
                AplicarSaldo(plan, usuario);
                return Task.CompletedTask;
            },
            despuesDeGuardar: async () =>
            {
                if (plan.Cobro.SaldoFavorUsado <= 0m)
                {
                    return;
                }

                // El crédito consumido es un segundo hecho económico dentro del mismo cobro, y por eso
                // tiene su propia acción: si solo se mirara 'cobro.registrado', no se podría
                // responder a cuánto crédito se gastó ni en cuál cobro.
                await auditoria.RegistrarAsync(new RegistroAuditoria
                {
                    Accion = "cobro.saldo_a_favor_usado",
                    Ip = ip,
                    UsuarioId = usuario,
                    Entidad = "cobro",
                    EntidadId = plan.Cobro.Id.ToString("D"),
                    DatosJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Contrato = plan.Cobro.ContratoId?.ToString("D"),
                        Moneda = plan.Cobro.MonedaRecibida.ToString(),
                        Importe = plan.Cobro.SaldoFavorUsado,
                        DisponibleAntes = plan.CreditoDisponible,
                    }),
                    Resultado = "permitido",
                    Origen = "api",
                }, ct);
            },
            enTransaccionDelLlamador: true,
            ct: ct);

        await transaccion.CommitAsync(ct);

        return respuesta;
    }

    /// <summary>
    /// Anulación de un cobro con motivo (F3-06, docs/ESPECIFICACION.md §3.10.1).
    /// <para>
    /// <b>No borra nada:</b> el cobro queda <c>Anulado</c> con su motivo y su fecha, sus asignaciones
    /// dejan de contar —la fila sigue ahí, que es la diferencia entre anular y borrar— y el excedente
    /// y el crédito vuelven al saldo a favor del contrato.
    /// </para>
    /// </summary>
    public async Task<CobroRespuesta> AnularAsync(
        Guid id,
        CobroAnulacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.CobroRegistrar, $"cobro/{id:D}", usuario, ct);

        var motivo = peticion.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            // 400 y no 409: no es un conflicto con lo que hay, es una petición incompleta. El modelo
            // ya rechaza el motivo ausente; esto solo cubre el de puros espacios.
            throw new ArgumentException(
                "El motivo de la anulación es obligatorio: sin motivo, la corrección de un cobro no se explica.",
                nameof(peticion));
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var cobro = await CargarAsync(id, ct);

        if (cobro.Estado != EstadoCobro.Registrado)
        {
            throw new InvalidOperationException(
                $"El cobro '{id:D}' ya está en {cobro.Estado} y no se anula dos veces.");
        }

        var saldo = await CargarSaldoAsync(cobro.ContratoId, cobro.MonedaRecibida, ct);

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cobro, cobro,
            esAlta: false,
            accion: "cobro.anulado",
            entidad: "cobro",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                cobro.Estado = EstadoCobro.Anulado;
                cobro.AnuladoAt = reloj.GetUtcNow();
                cobro.AnuladoPor = usuario;
                cobro.MotivoAnulacion = motivo;

                foreach (var asignacion in cobro.Asignaciones.Where(a => a.Vigente))
                {
                    asignacion.Vigente = false;
                }

                // Devolver el cobro es devolver el saldo a favor del contrato a como estaba antes:
                // sale el excedente que este cobro dejó y vuelve el crédito que consumió.
                if (saldo is not null)
                {
                    saldo.Importe = Redondeo.Importe(
                        Math.Max(0m, saldo.Importe + cobro.SaldoFavorUsado - cobro.Excedente));
                    saldo.UpdatedAt = reloj.GetUtcNow();
                    saldo.UpdatedBy = usuario;
                }

                return Task.CompletedTask;
            },
            enTransaccionDelLlamador: true,
            ct: ct);

        await transaccion.CommitAsync(ct);

        return ARespuesta(cobro);
    }

    // ---------------------------------------------------------------- planificación

    /// <summary>
    /// Valida la petición y decide a qué facturas va el dinero, sin escribir. La comparten la
    /// simulación y el registro, y por eso el registro no puede aceptar algo que la simulación
    /// rechazaría.
    /// </summary>
    private async Task<Plan> PlanificarAsync(CobroPeticion peticion, CancellationToken ct)
    {
        var importe = Redondeo.Importe(peticion.ImporteRecibido);
        if (importe <= 0m)
        {
            throw new InvalidOperationException("El importe recibido tiene que ser mayor que cero.");
        }

        var usado = Redondeo.Importe(peticion.UsarSaldoAFavor);
        if (usado < 0m)
        {
            throw new InvalidOperationException("El saldo a favor que se aplica no puede ser negativo.");
        }

        var cliente = await contexto.Cliente.FirstOrDefaultAsync(c => c.Id == peticion.ClienteId, ct)
            ?? throw new KeyNotFoundException($"El cliente '{peticion.ClienteId:D}' no existe.");

        Contrato? contrato = null;
        if (peticion.ContratoId is { } contratoId)
        {
            contrato = await contexto.Contrato.FirstOrDefaultAsync(c => c.Id == contratoId, ct)
                ?? throw new KeyNotFoundException($"El contrato '{contratoId:D}' no existe.");

            if (contrato.ClienteId != peticion.ClienteId)
            {
                throw new InvalidOperationException(
                    $"El contrato '{contratoId:D}' no es del cliente '{peticion.ClienteId:D}'.");
            }
        }

        var formaPago = await CargarFormaPagoAsync(peticion.FormaPagoId, ct);
        var cuenta = await CargarCuentaAsync(peticion.CuentaReceptoraId, ct);

        // La cuenta receptora tiene una moneda y esa es la del importe recibido (D8, AGENTS.md §6). Si
        // no coinciden, el cobro apuntaría a una cuenta que no puede contener esa moneda.
        if (cuenta.Moneda != peticion.MonedaRecibida)
        {
            throw new InvalidOperationException(
                $"La cuenta '{cuenta.Nombre}' es en {cuenta.Moneda} y el cobro se recibe en "
                    + $"{peticion.MonedaRecibida}. El importe recibido va en la moneda de la cuenta.");
        }

        // Sin cambio congelado el movimiento no se puede consolidar en dólares (D9) y la salida es la
        // carga manual de §3.12. El 409 lo dice, igual que en la emisión de facturas.
        var cambio = await tiposCambio.ActualAsync(peticion.MonedaRecibida, FechaDeNegocio(), ct);

        // El crédito es del contrato: sin contrato no hay de dónde sacarlo ni a qué aplicarlo.
        if (usado > 0m && peticion.ContratoId is null)
        {
            throw new InvalidOperationException(
                "El saldo a favor es del contrato: un cobro sin contrato no lo puede aplicar.");
        }

        var saldo = await CargarSaldoAsync(peticion.ContratoId, peticion.MonedaRecibida, ct);
        var creditoDisponible = saldo?.Importe ?? 0m;
        if (usado > creditoDisponible)
        {
            throw new InvalidOperationException(
                $"Ese contrato tiene {Redondeo.Importe(creditoDisponible)} de saldo a favor en "
                    + $"{peticion.MonedaRecibida} y el cobro quiere aplicar {usado}.");
        }

        var disponible = Redondeo.Importe(importe + usado);

        List<Factura> candidatas = [];
        Dictionary<Guid, decimal> pendientes = [];
        var deudaEnOtraMoneda = new List<CobroDeudaOtraMoneda>();

        if (peticion.ContratoId is { } contratoConDeuda)
        {
            (candidatas, pendientes) = await DeudasDelContratoAsync(contratoConDeuda, peticion.MonedaRecibida, ct);
            deudaEnOtraMoneda = await DeudaEnOtrasMonedasAsync(contratoConDeuda, peticion.MonedaRecibida, ct);
        }
        else if (peticion.Asignaciones is { Count: > 0 })
        {
            // TODO(A#): sin contrato no hay a qué facturas del cliente aplicar el importe ni dónde
            // dejar el excedente, así que un cobro suelto no admite asignaciones. La especificación no
            // lo dice: si quien cobra puede señalar una factura concreta de un cobro sin contrato, hay
            // que decidir aquí cuál es (la primera del contrato del cliente, la que diga quien cobra) y
            // qué pasa con su excedente.
            throw new InvalidOperationException(
                "Un cobro sin contrato no se puede asignar a ninguna factura: sin contrato no hay deuda "
                    + "que saldar ni dónde dejar el excedente.");
        }

        var lineas = peticion.Asignaciones is { Count: > 0 } pedidas
            ? await LineasExplícitasAsync(pedidas, peticion, pendientes, disponible, ct)
            : LineasFifo(candidatas, pendientes, disponible);

        var totalAsignado = Redondeo.Importe(lineas.Sum(l => l.Importe));
        var excedente = Redondeo.Importe(disponible - totalAsignado);

        var cobro = new Cobro
        {
            ClienteId = peticion.ClienteId,
            Cliente = cliente,
            ContratoId = peticion.ContratoId,
            Contrato = contrato,
            FechaEfectiva = peticion.FechaEfectiva,
            MonedaRecibida = peticion.MonedaRecibida,
            ImporteRecibido = importe,
            FormaPagoId = peticion.FormaPagoId,
            FormaPago = formaPago,
            Referencia = string.IsNullOrWhiteSpace(peticion.Referencia) ? null : peticion.Referencia.Trim(),
            CuentaReceptoraId = peticion.CuentaReceptoraId,
            CuentaReceptora = cuenta,
            TipoCambioValor = Redondeo.TipoCambio(cambio.Valor),
            TipoCambioFecha = cambio.Fecha,
            TipoCambioId = cambio.Id,
            SaldoFavorUsado = usado,
            Excedente = excedente,
            Estado = EstadoCobro.Registrado,
        };

        return new Plan
        {
            Cobro = cobro,
            Lineas = lineas,
            Saldo = saldo,
            CreditoDisponible = creditoDisponible,
            Disponible = disponible,
            TotalAsignado = totalAsignado,
            ActualizaSaldo = peticion.ContratoId is not null,
            DeudaEnOtraMoneda = deudaEnOtraMoneda,
        };
    }

    /// <summary>
    /// FIFO: primero lo más vencido y, a igualdad, lo más antiguo (docs/ESPECIFICACION.md §3.10.1).
    /// Un cobro parcial deja la siguiente a medias y no la salta: es el orden del aging, y saltar
    /// dejaría la deuda más antigua para el final, que es justo lo que §3.13 no quiere.
    /// </summary>
    private static List<LineaPlan> LineasFifo(
        List<Factura> candidatas,
        Dictionary<Guid, decimal> pendientes,
        decimal disponible)
    {
        var lineas = new List<LineaPlan>();
        var restante = disponible;

        var orden = candidatas
            .Where(f => pendientes[f.Id] > 0m)
            .OrderBy(f => f.FechaVencimiento)
            .ThenBy(f => f.FechaEmision)
            .ThenBy(f => f.Id);

        foreach (var factura in orden)
        {
            if (restante <= 0m)
            {
                break;
            }

            var importe = Redondeo.Importe(Math.Min(restante, pendientes[factura.Id]));
            restante = Redondeo.Importe(restante - importe);
            lineas.Add(LineaPlan.Nueva(factura, importe, pendientes[factura.Id]));
        }

        return lineas;
    }

    /// <summary>
    /// Asignaciones que trae quien registra, validadas contra el pendiente real de cada factura y contra
    /// lo que el cobro trae. Los mensajes dicen por qué se rechaza cada una, porque una lista de
    /// identificadores sin explicación no le sirve de nada a quien está corrigiendo un cobro.
    /// </summary>
    private async Task<List<LineaPlan>> LineasExplícitasAsync(
        IReadOnlyList<CobroAsignacionPeticion> pedidas,
        CobroPeticion peticion,
        Dictionary<Guid, decimal> pendientes,
        decimal disponible,
        CancellationToken ct)
    {
        var repetida = pedidas
            .GroupBy(p => p.FacturaId)
            .FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
        {
            throw new InvalidOperationException(
                $"La factura '{repetida.Key:D}' aparece dos veces en las asignaciones: de un cobro a una "
                    + "factura hay una sola línea.");
        }

        var ids = pedidas.Select(p => p.FacturaId).ToArray();

        // Se cargan también las que no son candidatas, para poder decir por qué no lo son en vez de
        // un «factura no encontrada» que no sería cierto.
        var encontradas = await contexto.Factura
            .Include(f => f.Serie)
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var lineas = new List<LineaPlan>();
        var asignado = 0m;

        foreach (var pedida in pedidas)
        {
            var importe = Redondeo.Importe(pedida.Importe);
            if (importe <= 0m)
            {
                throw new InvalidOperationException(
                    $"La asignación a la factura '{pedida.FacturaId:D}' es de {importe}: tiene que ser mayor que cero.");
            }

            if (!encontradas.TryGetValue(pedida.FacturaId, out var factura))
            {
                throw new KeyNotFoundException($"La factura '{pedida.FacturaId:D}' no existe.");
            }

            ExigirFacturaAsignable(factura, peticion);

            if (!pendientes.TryGetValue(factura.Id, out var pendiente))
            {
                throw new InvalidOperationException(
                    $"La factura {NumeroDe(factura)} ya está pagada y no admite más importe.");
            }

            if (importe > pendiente)
            {
                throw new InvalidOperationException(
                    $"La asignación a la factura {NumeroDe(factura)} es de {importe} y a esa factura le "
                        + $"quedan {pendiente} pendientes.");
            }

            // Cada factura tiene su pendiente, pero el conjunto tiene que caber en el cobro. Sin esta
            // comprobación, asignar 300 a una factura con un cobro de 100 llegaría al CHECK de la base
            // (excedente negativo) y quien lo escribió vería un 409 genérico de "error al guardar" en vez de
            // un mensaje que dice qué se pasó.
            asignado = Redondeo.Importe(asignado + importe);
            if (asignado > disponible)
            {
                throw new InvalidOperationException(
                    $"Las asignaciones suman {asignado} y este cobro solo trae {disponible} "
                        + "(importe recibido más el saldo a favor aplicado).");
            }

            lineas.Add(LineaPlan.Nueva(factura, importe, pendiente));
        }

        return lineas;
    }

    /// <summary>
    /// Por qué una factura no puede recibir este cobro. Cada motivo dice el suyo porque el camino de
    /// vuelta es distinto: una factura de otra moneda no es un error de captura sino un pago en otra
    /// moneda (§3.10.2), y una proforma no genera deuda (§3.7).
    /// </summary>
    private static void ExigirFacturaAsignable(Factura factura, CobroPeticion peticion)
    {
        if (factura.Estado != EstadoFactura.Emitida)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} está en {factura.Estado} y no genera deuda cobrable.");
        }

        if (factura.Serie?.SumaASaldos != true)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} es de la serie '{factura.Serie?.Numero}', que no suma a los saldos.");
        }

        if (factura.Moneda != peticion.MonedaRecibida)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} está en {factura.Moneda} y el cobro se recibe en "
                    + $"{peticion.MonedaRecibida}. Pagar deuda en una moneda con dinero de otra es el pago en "
                    + "otra moneda de §3.10.2, que llega con F3-07; aquí no se convierte nada (D7).");
        }

        if (peticion.ContratoId is null || factura.ContratoId != peticion.ContratoId)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} no es de este contrato: un cobro mueve el dinero de su "
                    + "propio contrato y nunca cruza de uno a otro por su cuenta.");
        }
    }

    // ---------------------------------------------------------------- deudas del contrato

    /// <summary>
    /// Facturas del contrato que generan deuda en la moneda del pago, con lo que les queda pendiente.
    /// Se excluyen las anuladas, las que siguen en borrador (no tienen número ni deuda) y las de
    /// series que no suman a saldos.
    /// </summary>
    private async Task<(List<Factura> Facturas, Dictionary<Guid, decimal> Pendientes)> DeudasDelContratoAsync(
        Guid contratoId,
        Moneda moneda,
        CancellationToken ct)
    {
        var facturas = await contexto.Factura
            .Include(f => f.Serie)
            .Where(f => f.ContratoId == contratoId
                && f.Estado == EstadoFactura.Emitida
                && f.Moneda == moneda
                && f.Serie!.SumaASaldos)
            .ToListAsync(ct);

        return (facturas, await PendientesAsync(facturas, ct));
    }

    /// <summary>
    /// Deuda pendiente del mismo contrato en las otras monedas. <b>No se asigna</b> en esta fase; se
    /// devuelve en la simulación para que la interfaz pueda explicar que el cobro no la cubre
    /// (§3.10.2, D7).
    /// </summary>
    private async Task<List<CobroDeudaOtraMoneda>> DeudaEnOtrasMonedasAsync(
        Guid contratoId,
        Moneda moneda,
        CancellationToken ct)
    {
        var facturas = await contexto.Factura
            .Include(f => f.Serie)
            .Where(f => f.ContratoId == contratoId
                && f.Estado == EstadoFactura.Emitida
                && f.Moneda != moneda
                && f.Serie!.SumaASaldos)
            .ToListAsync(ct);

        var pendientes = await PendientesAsync(facturas, ct);

        return
        [
            .. facturas
                .Where(f => pendientes[f.Id] > 0m)
                .GroupBy(f => f.Moneda)
                .Select(g => new CobroDeudaOtraMoneda(g.Key, Redondeo.Importe(g.Sum(f => pendientes[f.Id])), g.Count()))
                .OrderBy(d => d.Moneda),
        ];
    }

    private async Task<Dictionary<Guid, decimal>> PendientesAsync(List<Factura> facturas, CancellationToken ct)
    {
        if (facturas.Count == 0)
        {
            return [];
        }

        var cobrados = await CobradoPorFacturaAsync([.. facturas.Select(f => f.Id)], ct);

        return facturas.ToDictionary(
            f => f.Id,
            // TODO(F3-04): la retención de la factura entra aquí en su día. Hoy es cero porque la
            // columna no existe, y el cálculo ya está escrito para cuando llegue (§3.9, D14).
            f => CalculoCobro.Calcular(
                f.Total,
                0m,
                cobrados.GetValueOrDefault(f.Id),
                f.FechaVencimiento,
                FechaDeNegocio()).Pendiente);
    }

    // ---------------------------------------------------------------- saldo a favor

    private Task<SaldoAFavor?> CargarSaldoAsync(Guid? contratoId, Moneda moneda, CancellationToken ct)
        => contratoId is null
            ? Task.FromResult<SaldoAFavor?>(null)
            : contexto.SaldoAFavor.FirstOrDefaultAsync(s => s.ContratoId == contratoId && s.Moneda == moneda, ct);

    /// <summary>
    /// Aplica el efecto del cobro en el saldo a favor, dentro del mismo <c>SaveChanges</c> y por tanto
    /// en la misma transacción que el cobro: el crédito y el dinero no pueden quedar en estados
    /// distintos.
    /// <para>
    /// Sin contrato no hay saldo a favor: el excedente se queda en la columna <c>excedente</c> del
    /// propio cobro, que es donde se puede ver y de donde se revierte al anularlo. La fila del saldo
    /// se crea aquí si no existía, porque el crédito es único por contrato y moneda.
    /// </para>
    /// </summary>
    private void AplicarSaldo(Plan plan, Guid usuario)
    {
        if (!plan.ActualizaSaldo || plan.Cobro.ContratoId is not { } contratoId)
        {
            return;
        }

        var saldo = plan.Saldo;

        // Sin fila y sin efecto no se crea fila: un cobro que se gasta justo lo que debía no tiene por
        // qué dejar un saldo a favor en cero, y así la tabla sigue siendo «dónde hay crédito», no «qué
        // contratos han cobrado alguna vez».
        if (saldo is null)
        {
            if (plan.Cobro.Excedente <= 0m && plan.Cobro.SaldoFavorUsado <= 0m)
            {
                return;
            }

            // La fila se crea aquí porque el crédito es único por contrato y moneda y esta es la
            // primera vez que ese contrato cobra de más. Se añade explícitamente: no cuelga de la
            // navegación de ninguna entidad y EF no la vería por su cuenta.
            saldo = new SaldoAFavor
            {
                ContratoId = contratoId,
                Moneda = plan.Cobro.MonedaRecibida,
                Importe = 0m,
                CreatedAt = reloj.GetUtcNow(),
                CreatedBy = usuario,
            };

            contexto.SaldoAFavor.Add(saldo);
            plan.Saldo = saldo;
        }

        saldo.Importe = Redondeo.Importe(
            Math.Max(0m, saldo.Importe - plan.Cobro.SaldoFavorUsado + plan.Cobro.Excedente));
        saldo.UpdatedAt = reloj.GetUtcNow();
        saldo.UpdatedBy = usuario;
    }

    // ---------------------------------------------------------------- cargas y permisos

    private async Task<Cobro> CargarAsync(Guid id, CancellationToken ct)
    {
        return await contexto.Cobro
            .Include(c => c.Cliente)
            .Include(c => c.FormaPago)
            .Include(c => c.CuentaReceptora)
            .Include(c => c.Asignaciones).ThenInclude(a => a.Factura)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"El cobro '{id:D}' no existe.");
    }

    private async Task<FormaPago> CargarFormaPagoAsync(Guid id, CancellationToken ct)
    {
        var formaPago = await contexto.FormaPago.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException($"La forma de pago '{id:D}' no existe.");

        if (!formaPago.Activa)
        {
            throw new InvalidOperationException($"La forma de pago '{formaPago.Nombre}' está desactivada.");
        }

        return formaPago;
    }

    private async Task<CuentaReceptora> CargarCuentaAsync(Guid id, CancellationToken ct)
    {
        var cuenta = await contexto.CuentaReceptora.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"La cuenta receptora '{id:D}' no existe.");

        if (!cuenta.Activa)
        {
            throw new InvalidOperationException($"La cuenta receptora '{cuenta.Nombre}' está desactivada.");
        }

        return cuenta;
    }

    /// <summary>
    /// Segunda puerta del permiso: el endpoint lo pide con <c>[Accion]</c> y el atributo protege la
    /// entrada por HTTP y nada más. Un trabajo entra por el servicio, y el intento denegado se audita
    /// antes de lanzar, para que quede constancia de quién lo intentó y no pudo.
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

    private DateOnly FechaDeNegocio() => TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

    private static string NumeroDe(Factura factura) => factura.Numero is { } numero ? $"nº {numero}" : "sin número";

    // ---------------------------------------------------------------- proyecciones

    private static CobroResumen AResumen(Cobro c) => new(
        c.Id,
        c.ClienteId,
        c.Cliente?.Nombre,
        c.ContratoId,
        c.FechaEfectiva,
        c.MonedaRecibida,
        c.ImporteRecibido,
        c.FormaPagoId,
        c.FormaPago?.Nombre,
        c.CuentaReceptoraId,
        c.CuentaReceptora?.Nombre,
        c.TipoCambioValor,
        c.TipoCambioFecha,
        c.SaldoFavorUsado,
        c.Excedente,
        c.Estado,
        c.CreatedAt);

    /// <summary>
    /// El cobro con sus asignaciones. Las de un cobro anulado salen igual, con su importe y su
    /// factura: lo que deja de contar no es lo que se esconde.
    /// </summary>
    private static CobroRespuesta ARespuesta(Cobro c) => new(
        c.Id,
        c.ClienteId,
        c.Cliente?.Nombre,
        c.ContratoId,
        c.FechaEfectiva,
        c.MonedaRecibida,
        c.ImporteRecibido,
        c.FormaPagoId,
        c.FormaPago?.Nombre,
        c.Referencia,
        c.CuentaReceptoraId,
        c.CuentaReceptora?.Nombre,
        c.TipoCambioValor,
        c.TipoCambioFecha,
        c.TipoCambioId,
        c.SaldoFavorUsado,
        c.Excedente,
        Redondeo.Importe(c.Asignaciones.Where(a => a.Vigente).Sum(a => a.Importe)),
        c.Estado,
        c.AnuladoAt,
        c.AnuladoPor,
        c.MotivoAnulacion,
        c.CreatedAt,
        [.. c.Asignaciones
            .OrderBy(a => a.Factura?.FechaVencimiento)
            .ThenBy(a => a.Factura?.Numero)
            .ThenBy(a => a.FacturaId)
            .Select(a => new CobroAsignacionRespuesta(
                a.Id,
                a.FacturaId,
                a.Factura?.Numero,
                a.Factura?.FechaVencimiento,
                a.Importe,
                a.Vigente,
                a.CreatedAt))]);

    /// <summary>
    /// Lo que va en la auditoría firmada. Los identificadores van como texto y las fechas como día de
    /// negocio, como en <c>ServicioFacturas</c>: la firma cubre el documento, no su representación.
    /// </summary>
    private static object DatosAuditoria(Cobro c) => new
    {
        c.Id,
        Cliente = c.ClienteId.ToString("D"),
        Contrato = c.ContratoId?.ToString("D"),
        Efectiva = c.FechaEfectiva.ToString("yyyy-MM-dd"),
        Moneda = c.MonedaRecibida.ToString(),
        c.ImporteRecibido,
        FormaPago = c.FormaPagoId.ToString("D"),
        Cuenta = c.CuentaReceptoraId.ToString("D"),
        c.Referencia,
        c.TipoCambioValor,
        Cambio = c.TipoCambioFecha.ToString("yyyy-MM-dd"),
        c.SaldoFavorUsado,
        c.Excedente,
        Estado = c.Estado.ToString(),
        Asignado = Redondeo.Importe(c.Asignaciones.Where(a => a.Vigente).Sum(a => a.Importe)),
        Facturas = c.Asignaciones
            .Where(a => a.Vigente)
            .OrderBy(a => a.FacturaId)
            .Select(a => a.FacturaId.ToString("D"))
            .ToList(),
        c.MotivoAnulacion,
    };

    // ---------------------------------------------------------------- tipos internos

    /// <summary>
    /// Una línea de la propuesta: qué factura, cuánto se le aplica y cuánto le quedaba antes. Se
    /// convierte en fila de <c>cobro_asignacion</c> al guardar y es lo que devuelve la simulación.
    /// </summary>
    private sealed record LineaPlan(Factura Factura, decimal Importe, decimal PendienteAntes)
    {
        public static LineaPlan Nueva(Factura factura, decimal importe, decimal pendienteAntes) =>
            new(factura, importe, pendienteAntes);

        public CobroAsignacion Entidad => new()
        {
            FacturaId = Factura.Id,
            Factura = Factura,
            Importe = Importe,
            Vigente = true,
        };

        public CobroAsignacionSimulada Simulada() => new(
            Factura.Id,
            Factura.Numero,
            Factura.FechaVencimiento,
            Importe,
            PendienteAntes);
    }

    /// <summary>
    /// Lo que el registro necesita de una planificación: el cobro ya poblado, las líneas, el saldo a
    /// favor que hay que mover y los números que salen en las respuestas. Existe para que la
    /// simulación y el registro compartan exactamente el mismo reparto.
    /// </summary>
    private sealed class Plan
    {
        public required Cobro Cobro { get; init; }

        public required List<LineaPlan> Lineas { get; init; }

        public SaldoAFavor? Saldo { get; set; }

        public decimal CreditoDisponible { get; init; }

        public decimal Disponible { get; init; }

        public decimal TotalAsignado { get; init; }

        /// <summary>
        /// Si este cobro toca el saldo a favor. Sin contrato no lo toca: el excedente se queda en el
        /// propio cobro (§3.10.1).
        /// </summary>
        public bool ActualizaSaldo { get; init; }

        public required List<CobroDeudaOtraMoneda> DeudaEnOtraMoneda { get; init; }
    }
}