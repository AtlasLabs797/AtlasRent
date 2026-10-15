using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cobros: registro, asignación a facturas, saldo a favor y anulación (F3-06,
/// docs/ESPECIFICACION.md §3.10.1); pago en otra moneda y ajuste por diferencia de cambio (F3-07,
/// §3.10.2 y D8).
/// <list type="bullet">
/// <item>
/// <b>La asignación por defecto es FIFO dentro del contrato</b> (§3.10.1, «la más antigua
/// primero»), por vencimiento y a igualdad por emisión, con el identificador como último desempate
/// para que dos facturas del mismo día no den un orden que cambie entre dos llamadas.
/// </item>
/// <item>
/// <b>La FIFO no filtra por moneda.</b> Desde F3-07 propone también las facturas que están en otra
/// moneda que la del pago, con su equivalente en la moneda recibida (§3.10.2: cobrar una factura de
/// 1.000 USD con 59.500 DOP es un cobro de 59.500 DOP, no dos documentos). El orden entre monedas es
/// el de vencimiento de cada una, que es el mismo criterio de siempre.
/// </item>
/// <item>
/// <b>La asignación es editable.</b> Quien registra el cobro puede mandar sus propias asignaciones y
/// entonces el servicio no propone nada: el dinero va a la factura que le digan. Lo que no puede es
/// asignar de más, ni a una factura de otro contrato, ni a una factura cuya moneda no se pueda
/// convertir con un cambio conocido.
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
/// <b>Qué cambio se aplica.</b> Por defecto el del día de la moneda recibida, que es el que queda
/// congelado en el cobro (D9). El gerente puede fijar por petición el que se usó de verdad en el
/// mostrador, y ese va en cada asignación; <see cref="Data.Cobro.TipoCambioValor"/> conserva el del
/// día aunque no coincidan, porque el del día es el que justifica los informes y el aplicado es el
/// que explica el cobro.
/// </para>
/// <para>
/// <b>El ajuste por diferencia de cambio no es dinero recibido.</b> Vive en su propia tabla
/// (<c>ajuste_diferencia_cambio</c>) y por eso nunca se suma dentro de «cobrado»: es el movimiento
/// con el que se cierra un pendiente que solo salió porque el cambio no dividía exacto. En la
/// respuesta de la factura va aparte, y los dos juntos son los que la cierran. Su tolerancia sale de
/// <c>ajuste_general</c>, que escribe el administrador, y <b>sin tolerancia configurada no hay ajuste</b>.
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
    ServicioConfiguracion configuracion,
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
    /// Suma de los ajustes por diferencia de cambio <b>vigentes</b> de cada factura (F3-07, §3.10.2).
    /// <para>
    /// Va aparte de <see cref="CobradoPorFacturaAsync"/> y se consulta aparte a propósito: el ajuste
    /// no es dinero que entró en la caja y una consulta que los mezclara invitaría a sumar los dos al
    /// informar de caja, que es justo el error que hay que evitar. Los dos cierran la factura; cada
    /// uno dice lo suyo.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> AjustadoPorFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken ct)
    {
        if (facturaIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var ids = facturaIds.ToArray();

        return await contexto.AjusteDiferenciaCambio.AsNoTracking()
            .Where(a => a.AnuladoAt == null && ids.Contains(a.FacturaId))
            .GroupBy(a => a.FacturaId)
            .Select(g => new { FacturaId = g.Key, Ajustado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Ajustado, ct);
    }

    /// <summary>
    /// Situación de cobro de una factura con lo que hay cobrado hoy. Es la proyección de los cinco
    /// campos que el DTO de factura expone, y vive aquí para que la vista de cuentas (§3.13) y el
    /// panel de cobro no puedan discrepar sobre la misma factura.
    /// </summary>
    public SituacionCobroFactura SituacionDeFactura(
        decimal total,
        decimal cobrado,
        decimal ajustado,
        DateOnly vencimiento)
        => CalculoCobro.Calcular(total, 0m, cobrado, ajustado, vencimiento, FechaDeNegocio());

    // ---------------------------------------------------------------- simulación

    /// <summary>
    /// Propuesta de asignación de un cobro <b>sin escribir nada</b>. Es lo mismo que hace el registro
    /// —mismas validaciones, mismo FIFO, misma conversión— pero sin transacción ni auditoría, y
    /// devuelve además la deuda que el cobro <b>no</b> va a cubrir: la del mismo contrato en otra moneda
    /// que queda viva después de aplicar la propuesta (§3.10.2, F3-07).
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

    // ---------------------------------------------------------------- ajuste por diferencia de cambio

    /// <summary>
    /// Cierra por ajuste el pendiente que dejó una diferencia de cambio (F3-07,
    /// docs/ESPECIFICACION.md §3.10.2).
    /// <para>
    /// <b>El importe es el pendiente real, no uno que se pida.</b> Quien firma el ajuste dice que la
    /// diferencia es de un céntimo, y si le pudiéramos elegir el importe, el mismo botón serviría
    /// para tapar un saldo de cualquier tamaño con la palabra «diferencia de cambio» delante. Aquí el
    /// servicio lo calcula y además lo contrasta con la tolerancia configurada (§3.10.2 la marca como
    /// [Supuesto], A#), así que solo se puede cerrar lo que la empresa ha dicho que se puede cerrar.
    /// </para>
    /// <para>
    /// <b>Dos salidas cuando la diferencia es grande, y ninguna es ajustar.</b> O se deja el pendiente
    /// y el cliente paga la diferencia en un cobro nuevo, o se corrige el cambio del cobro anterior
    /// registrando de nuevo el pago con el cambio que se usó de verdad. El mensaje del 409 dice las dos,
    /// porque elegir entre ellas depende de qué haya pasado en la casa y eso lo sabe quien está delante
    /// del pantalla, no el servicio.
    /// </para>
    /// <para>
    /// El ajuste se guarda con su IP y su motivo en la misma transacción que lo audita, y en la tabla
    /// propia: no es una asignación de cobro porque no es dinero que entró.
    /// </para>
    /// </summary>
    public async Task<AjusteDiferenciaCambioRespuesta> RegistrarAjusteAsync(
        AjusteDiferenciaCambioPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(
            Acciones.CobroRegistrar,
            $"factura/{peticion.FacturaId:D}/ajuste-diferencia-cambio",
            usuario,
            ct);

        var motivo = peticion.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            // 400 y no 409: no es un conflicto con lo que hay, es una petición incompleta. El modelo
            // ya rechaza el motivo ausente; esto solo cubre el de puros espacios.
            throw new ArgumentException(
                "El motivo del ajuste es obligatorio: sin motivo, un importe que no cuadra no se distingue de un error de captura.",
                nameof(peticion));
        }

        var tipoCambio = Redondeo.TipoCambio(peticion.TipoCambioValor);
        if (tipoCambio <= 0m)
        {
            throw new ArgumentException(
                "El tipo de cambio del ajuste tiene que ser mayor que cero.",
                nameof(peticion));
        }

        var factura = await contexto.Factura
            .Include(f => f.Serie)
            .FirstOrDefaultAsync(f => f.Id == peticion.FacturaId, ct)
            ?? throw new KeyNotFoundException($"La factura '{peticion.FacturaId:D}' no existe.");

        if (factura.Estado != EstadoFactura.Emitida)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} está en {factura.Estado} y no tiene pendiente que ajustar.");
        }

        var pendiente = await PendienteDeFacturaAsync(factura, ct);

        if (pendiente <= 0m)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} no tiene nada pendiente: ya está pagada y no hay diferencia que ajustar.");
        }

        var tolerancia = await ToleranciaDeAjusteAsync(factura.Moneda, ct);

        if (pendiente > tolerancia)
        {
            // El 409 lleva la tolerancia dentro porque §3.10.2 manda lo que el mensaje dice, y no
            // solo lo que impide: quien lo lee tiene que poder decidir sin abrir la configuración.
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} tiene {pendiente} {factura.Moneda} pendientes y la "
                    + $"tolerancia de diferencia de cambio es de {tolerancia}. Con una diferencia mayor no se "
                    + "ajusta: o el cliente paga la diferencia en un cobro nuevo, o se corrige el cambio del cobro "
                    + "anterior registrando de nuevo el mismo pago con el cambio que se usó de verdad.");
        }

        var ajuste = new AjusteDiferenciaCambio
        {
            FacturaId = factura.Id,
            Factura = factura,
            Importe = pendiente,
            Moneda = factura.Moneda,
            TipoCambioValor = tipoCambio,
            Motivo = motivo,
            // La IP va en la propia fila y no solo en la auditoría: la fila tiene que poder responder
            // por sí sola desde dónde se corrigió el saldo, y con la auditoría si algún día se archiva
            // o se filtra, la respuesta se queda sin su contexto.
            Ip = string.IsNullOrWhiteSpace(ip) ? null : ip,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.AjusteDiferenciaCambio, ajuste,
            esAlta: true,
            accion: "ajuste_diferencia_cambio.registrado",
            entidad: "ajuste_diferencia_cambio",
            usuario,
            ip,
            mutacion,
            proyectar: AAjusteRespuesta,
            datosAuditoria: DatosAuditoriaAjuste,
            ct: ct);
    }

    /// <summary>
    /// Anula un ajuste por diferencia de cambio con motivo (F3-07).
    /// <para>
    /// <b>No borra nada:</b> la fila queda con su motivo, su fecha y su usuario y deja de contar, que
    /// es lo mismo que hace un cobro anulado con sus asignaciones (AGENTS.md §6). Anular deja la
    /// factura como estaba, con el pendiente que le había salido del cobro.
    /// </para>
    /// </summary>
    public async Task<AjusteDiferenciaCambioRespuesta> AnularAjusteAsync(
        Guid id,
        AjusteDiferenciaCambioAnulacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.CobroRegistrar, $"ajuste-diferencia-cambio/{id:D}", usuario, ct);

        var motivo = peticion.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException(
                "El motivo de la anulación del ajuste es obligatorio: sin motivo, la corrección de un saldo no se explica.",
                nameof(peticion));
        }

        var ajuste = await contexto.AjusteDiferenciaCambio
            .Include(a => a.Factura)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"El ajuste por diferencia de cambio '{id:D}' no existe.");

        if (ajuste.AnuladoAt is not null)
        {
            throw new InvalidOperationException($"El ajuste '{id:D}' ya está anulado y no se anula dos veces.");
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.AjusteDiferenciaCambio, ajuste,
            esAlta: false,
            accion: "ajuste_diferencia_cambio.anulado",
            entidad: "ajuste_diferencia_cambio",
            usuario,
            ip,
            mutacion,
            proyectar: AAjusteRespuesta,
            datosAuditoria: DatosAuditoriaAjuste,
            antesDeGuardar: () =>
            {
                ajuste.AnuladoAt = reloj.GetUtcNow();
                ajuste.AnuladoPor = usuario;
                ajuste.MotivoAnulacion = motivo;
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>
    /// Tolerancia vigente para una moneda, leída de <c>ajuste_general</c> por el mismo camino que usa
    /// la pantalla de *Configuración → Ajustes generales* (F1-04). Es la única fuente: si además
    /// hubiera una en variables de entorno habría dos sitios donde escribir una cifra que no se sabe
    /// cuál manda (BUG-007).
    /// <para>
    /// <b>Sin tolerancia configurada no se ajusta nada</b>, y el 409 lo dice con nombre y apellidos.
    /// Las dos columnas nacen a <c>NULL</c> —la fila de la migración `Configuracion` se inserta sin
    /// ellas— y §3.10.2 marca los importes como [Supuesto], no como decisión. Un ajuste que se
    /// permite sin que nadie haya escrito una cifra es una pérdida de dinero silenciosa: el pendiente
    /// se cierra sin que nadie haya dicho que se podía cerrar. Un cero es lo mismo que un nulo a
    /// efectos prácticos (nada es «menor o igual a cero»), pero se distingue en el mensaje porque
    /// poner un cero es una decisión activa y no aparecer la fila es un olvido.
    /// </para>
    /// </summary>
    private async Task<decimal> ToleranciaDeAjusteAsync(Moneda moneda, CancellationToken ct)
    {
        var ajustes = await configuracion.LeerAjustesAsync(ct);

        var tolerancia = moneda switch
        {
            Moneda.USD => ajustes.ToleranciaCambioUsd,
            Moneda.DOP => ajustes.ToleranciaCambioDop,
            _ => null,
        };

        if (tolerancia is not { } valor || valor <= 0m)
        {
            throw new InvalidOperationException(
                $"No hay tolerancia de diferencia de cambio configurada para {moneda}, así que no se puede "
                    + "ajustar nada. La fija el administrador en Configuración → Ajustes generales "
                    + "(«tolerancia de diferencia de cambio»): el importe que aparece en §3.10.2 es un supuesto y "
                    + "está sin confirmar, así que hasta que alguien lo escriba no se cierra ninguna diferencia.");
        }

        return Redondeo.Importe(valor);
    }

    /// <summary>Pendiente actual de una factura: total menos retenciones, menos cobrado, menos ajustado.</summary>
    private async Task<decimal> PendienteDeFacturaAsync(Factura factura, CancellationToken ct)
    {
        var cobrados = await CobradoPorFacturaAsync([factura.Id], ct);
        var ajustados = await AjustadoPorFacturaAsync([factura.Id], ct);

        return CalculoCobro.Calcular(
            factura.Total,
            0m,
            cobrados.GetValueOrDefault(factura.Id),
            ajustados.GetValueOrDefault(factura.Id),
            factura.FechaVencimiento,
            FechaDeNegocio()).Pendiente;
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

        // El cambio del día es el dato de referencia y es lo que se congela en el cobro (D9). El
        // aplicado es el que se usa para convertir las líneas de otra moneda y va en cada asignación.
        // Son dos cosas porque el gerente pudo cobrar al cambio del banco y no al de la casa, y el
        // informe del día tiene que seguir siendo el del día.
        var cambioAplicado = peticion.TipoCambioAplicado is { } fijado
            ? Redondeo.TipoCambio(fijado)
            : Redondeo.TipoCambio(cambio.Valor);

        if (cambioAplicado <= 0m)
        {
            // 400 y no 409: un cambio de cero o negativo es una petición mal formada, no un conflicto
            // con lo que hay en la base.
            throw new ArgumentException(
                "El tipo de cambio aplicado tiene que ser mayor que cero.",
                nameof(peticion));
        }

        List<Factura> candidatas = [];
        Dictionary<Guid, decimal> pendientes = [];

        if (peticion.ContratoId is { } contratoConDeuda)
        {
            // Desde F3-07 la deuda del contrato se trae de todas sus monedas: el pago en otra moneda
            // (§3.10.2) es precisamente esto, y seguir filtrando por la moneda recibida dejaría sin
            // pagar justo las facturas que este cobro viene a pagar.
            (candidatas, pendientes) = await DeudasDelContratoAsync(contratoConDeuda, ct);
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
            ? await LineasExplícitasAsync(pedidas, peticion, cambioAplicado, pendientes, disponible, ct)
            : LineasFifo(candidatas, pendientes, disponible, peticion.MonedaRecibida, cambioAplicado);

        var totalAsignado = Redondeo.Importe(lineas.Sum(l => l.Importe));
        var totalRecibidoAsignado = Redondeo.Importe(lineas.Sum(l => l.Consumido));
        var excedente = Redondeo.Importe(disponible - totalRecibidoAsignado);

        // Lo que queda en moneda distinta de la recibida **después** de aplicar el cobro. Antes de
        // F3-07 era la deuda completa de las otras monedas, porque no se podía tocar; ahora es la
        // deuda que este cobro deja viva, que es lo que la interfaz necesita para decir «no lo cubre
        // todo» sin mentir por omisión.
        var deudaEnOtraMoneda = DeudaNoCubierta(candidatas, pendientes, lineas, peticion.MonedaRecibida);

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
    /// <para>
    /// <b>Reparte en la moneda recibida y convierte hacia la de la factura</b> (§3.10.2), que es la
    /// dirección natural: el dinero está en pesos y hay que decidir a qué facturas llega. Por eso el
    /// cálculo va de «cuánto de esta factura cuesta en lo que tengo» y no al revés: convertir el
    /// pendiente de la factura a pesos, quedarse con lo que hay, y devolver el equivalente a la
    /// factura redondeado a céntimo (D27).
    /// </para>
    /// <para>
    /// El importe nunca pasa del pendiente, ni aunque sobren pesos: sobrar es saldo a favor (§3.10.1),
    /// no sobrepago de una factura. Y cuando el redondeo deja un céntimo sin cubrir, ese céntimo es
    /// exactamente lo que después mide el ajuste por diferencia de cambio.
    /// </para>
    /// </summary>
    private static List<LineaPlan> LineasFifo(
        List<Factura> candidatas,
        Dictionary<Guid, decimal> pendientes,
        decimal disponible,
        Moneda monedaRecibida,
        decimal cambioAplicado)
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

            var pendiente = pendientes[factura.Id];

            // Una factura cuya moneda no se puede convertir con este cobro no se salda, pero tampoco
            // hace fracasar el cobro entero: se salta y sale en «deudaEnOtraMoneda» con lo que le
            // queda. Romper aquí dejaría sin poder cobrar por una factura que este cobro no podía
            // pagar de todos modos.
            var cambio = CambioParaFactura(factura, monedaRecibida, cambioAplicado);
            if (cambio is null)
            {
                continue;
            }

            var recibido = Redondeo.Importe(Math.Min(restante, Redondeo.Importe(pendiente * cambio.Value)));

            // El equivalente en la moneda de la factura. El mínimo con el pendiente no es
            // decorativo: el redondeo del paso anterior puede dejar el equivalente una centésima por
            // encima de lo que se debe, y asignar eso sería sobrepagar la factura.
            var importe = Redondeo.Importe(Math.Min(recibido / cambio.Value, pendiente));

            if (importe <= 0m)
            {
                // Lo que queda no llega a un céntimo en la moneda de la factura. No se asigna una línea
                // de cero (la base la rechaza) ni se pierde el dinero: sigue en `restante` y se
                // convierte en excedente, que es un saldo a favor real del contrato.
                continue;
            }

            restante = Redondeo.Importe(restante - recibido);

            // El importe recibido solo se guarda cuando hubo conversión de verdad, y eso lo decide la
            // moneda de la factura, no que el cambio valga 1: con un cambio fijado a 1 la línea
            // sigue siendo de otra moneda y guardarla como si no se hubiera convertido perdería el
            // hecho más importante de la fila.
            var mismaMoneda = factura.Moneda == monedaRecibida;

            lineas.Add(LineaPlan.Nueva(factura, importe, mismaMoneda ? null : recibido, cambio.Value, pendiente));
        }

        return lineas;
    }

    /// <summary>
    /// Cambio con el que se convierte lo recibido en la moneda de esa factura, o <b>null</b> si esta
    /// factura no se puede pagar con este cobro.
    /// <list type="bullet">
    /// <item><b>Misma moneda que el pago: 1.</b> No hay conversión y por eso la asignación se guarda
    /// sin importe recibido ni tipo de cambio.</item>
    /// <item><b>Factura en dólares: el cambio de la moneda recibida.</b> Es el único sentido que el
    /// cambio del día permite, porque el dólar es la base en la que vive el tipo de cambio (F3-01,
    /// D9) y «59,50» significa 59,50 pesos por dólar: dividir lo recibido entre eso da dólares.</item>
    /// <item><b>Cualquier otra moneda: null.</b> Convertir al revés («pagar una factura en pesos con
    /// dólares») necesita el cambio de la moneda de la factura, y el cobro solo trae el de la moneda
    /// en que se recibe el dinero. <c>TODO(A#)</c> Cuando la casa registre cambio de más monedas
    /// (F3-01 recorre las del enumerado), hay que decidir de dónde sale el de la factura antes de
    /// permitirlo; mientras tanto la FIFO se salta esas facturas y la asignación explícita las rechaza
    /// con un 409 que lo explica.</item>
    /// </list>
    /// </summary>
    private static decimal? CambioParaFactura(Factura factura, Moneda monedaRecibida, decimal cambioAplicado)
    {
        if (factura.Moneda == monedaRecibida)
        {
            return 1m;
        }

        return factura.Moneda == Moneda.USD ? Redondeo.TipoCambio(cambioAplicado) : null;
    }

    /// <summary>
    /// Asignaciones que trae quien registra, validadas contra el pendiente real de cada factura y contra
    /// lo que el cobro trae. Los mensajes dicen por qué se rechaza cada una, porque una lista de
    /// identificadores sin explicación no le sirve de nada a quien está corrigiendo un cobro.
    /// <para>
    /// El importe se pide <b>siempre en la moneda de la factura</b>, también cuando la factura está en
    /// otra moneda que la del pago. Quien registra sabe cuánto vale la factura y es ese número el que
    /// decide a qué se aplica el dinero; lo que salió de la caja lo calcula el servicio con el cambio y
    /// lo guarda, para que las dos cifras no se puedan contradecir en la misma fila.
    /// </para>
    /// </summary>
    private async Task<List<LineaPlan>> LineasExplícitasAsync(
        IReadOnlyList<CobroAsignacionPeticion> pedidas,
        CobroPeticion peticion,
        decimal cambioAplicado,
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

            var cambio = ExigirFacturaAsignable(factura, peticion, cambioAplicado);

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

            // Lo que sale de la caja por esta línea. En la moneda del cobro es el importe tal cual; en
            // otra moneda es su equivalente con el cambio aplicado, que puede no salir exacto y por eso
            // se redondea antes de guardarlo.
            var recibido = cambio is null ? importe : Redondeo.Importe(importe * cambio.Value);

            // Cada factura tiene su pendiente, pero el conjunto tiene que caber en el cobro, y en la
            // moneda del cobro. Sin esta comprobación, asignar 300 a una factura con un cobro de 100
            // llegaría al CHECK de la base (excedente negativo) y quien lo escribió vería un 409
            // genérico de «error al guardar» en vez de un mensaje que dice qué se pasó.
            asignado = Redondeo.Importe(asignado + recibido);
            if (asignado > disponible)
            {
                throw new InvalidOperationException(
                    $"Las asignaciones suman {asignado} {peticion.MonedaRecibida} y este cobro solo trae "
                        + $"{disponible} {peticion.MonedaRecibida} (importe recibido más el saldo a favor aplicado).");
            }

            lineas.Add(LineaPlan.Nueva(factura, importe, cambio is null ? null : recibido, cambio ?? 1m, pendiente));
        }

        return lineas;
    }

    /// <summary>
    /// Por qué una factura no puede recibir este cobro, y con qué cambio se convierte si puede. Cada
    /// motivo dice el suyo porque el camino de vuelta es distinto: una factura cuya moneda no se puede
    /// convertir no es un error de captura sino una limitación de qué cambios hay registrados, y una
    /// proforma no genera deuda (§3.7).
    /// <para>
    /// Devuelve <b>null</b> cuando la factura está en la moneda del pago (no hubo conversión y la
    /// línea se guarda sin importe recibido ni cambio) y el cambio a aplicar cuando está en dólares.
    /// Que sea el valor y no el <c>== 1</c> lo que decide no es un detalle: con un cambio fijado a 1
    /// una línea de otra moneda seguiría siendo de otra moneda, y guardarla como si no se hubiera
    /// convertido perdería el hecho más importante de la fila.
    /// </para>
    /// </summary>
    private static decimal? ExigirFacturaAsignable(
        Factura factura,
        CobroPeticion peticion,
        decimal cambioAplicado)
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

        if (factura.Moneda != peticion.MonedaRecibida
            && factura.Moneda != Moneda.USD)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} está en {factura.Moneda} y el cobro se recibe en "
                    + $"{peticion.MonedaRecibida}. Para convertir haría falta el cambio de {factura.Moneda}, "
                    + "y este cobro solo trae el cambio de la moneda en que se recibe el dinero.");
        }

        if (peticion.ContratoId is null || factura.ContratoId != peticion.ContratoId)
        {
            throw new InvalidOperationException(
                $"La factura {NumeroDe(factura)} no es de este contrato: un cobro mueve el dinero de su "
                    + "propio contrato y nunca cruza de uno a otro por su cuenta.");
        }

        return factura.Moneda == peticion.MonedaRecibida ? null : Redondeo.TipoCambio(cambioAplicado);
    }

    // ---------------------------------------------------------------- deudas del contrato

    /// <summary>
    /// Facturas del contrato que generan deuda, <b>en cualquier moneda</b>, con lo que les queda
    /// pendiente. Se excluyen las anuladas, las que siguen en borrador (no tienen número ni deuda) y las
    /// de series que no suman a saldos.
    /// <para>
    /// Desde F3-07 no se filtra por la moneda recibida. La deuda de un contrato está en la moneda en
    /// que se emitieron sus facturas y el pago en pesos es perfectamente válido contra deuda en
    /// dólares (§3.10.2, D8); filtrar por moneda era la manera que tenía F3-06 de decir «esto todavía
    /// no se puede», y ya se puede.
    /// </para>
    /// </summary>
    private async Task<(List<Factura> Facturas, Dictionary<Guid, decimal> Pendientes)> DeudasDelContratoAsync(
        Guid contratoId,
        CancellationToken ct)
    {
        var facturas = await contexto.Factura
            .Include(f => f.Serie)
            .Where(f => f.ContratoId == contratoId
                && f.Estado == EstadoFactura.Emitida
                && f.Serie!.SumaASaldos)
            .ToListAsync(ct);

        return (facturas, await PendientesAsync(facturas, ct));
    }

    /// <summary>
    /// Deuda que queda viva en monedas distintas de la recibida <b>después</b> de aplicar este cobro.
    /// <para>
    /// Antes de F3-07 esto era la deuda completa de las otras monedas y no se podía tocar; ahora es la
    /// parte que el cobro no alcanza, que es lo único que la interfaz puede afirmar con verdad. Por eso
    /// se calcula aquí y no antes: con las pendientes ya descontadas, no con las de antes del cobro.
    /// </para>
    /// </summary>
    private static List<CobroDeudaOtraMoneda> DeudaNoCubierta(
        List<Factura> candidatas,
        Dictionary<Guid, decimal> pendientes,
        List<LineaPlan> lineas,
        Moneda monedaRecibida)
    {
        var aplicado = lineas.GroupBy(l => l.Factura.Id).ToDictionary(g => g.Key, g => g.Sum(l => l.Importe));

        return
        [
            .. candidatas
                .Where(f => f.Moneda != monedaRecibida)
                .GroupBy(f => f.Moneda)
                .Select(g =>
                {
                    var facturas = g.ToList();
                    var queda = Redondeo.Importe(facturas.Sum(f =>
                        Math.Max(0m, pendientes.GetValueOrDefault(f.Id) - aplicado.GetValueOrDefault(f.Id))));
                    return new CobroDeudaOtraMoneda(g.Key, queda, facturas.Count);
                })
                .Where(d => d.Pendiente > 0m)
                .OrderBy(d => d.Moneda),
        ];
    }

    private async Task<Dictionary<Guid, decimal>> PendientesAsync(List<Factura> facturas, CancellationToken ct)
    {
        if (facturas.Count == 0)
        {
            return [];
        }

        var ids = facturas.Select(f => f.Id).ToArray();

        var cobrados = await CobradoPorFacturaAsync(ids, ct);
        var ajustados = await AjustadoPorFacturaAsync(ids, ct);

        return facturas.ToDictionary(
            f => f.Id,
            // TODO(F3-04): la retención de la factura entra aquí en su día. Hoy es cero porque la
            // columna no existe, y el cálculo ya está escrito para cuando llegue (§3.9, D14). El
            // ajuste sí entra ya: es lo que cierra el pendiente de una diferencia de cambio (§3.10.2).
            f => CalculoCobro.Calcular(
                f.Total,
                0m,
                cobrados.GetValueOrDefault(f.Id),
                ajustados.GetValueOrDefault(f.Id),
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
                a.Factura?.Moneda ?? Moneda.USD,
                a.Importe,
                a.ImporteRecibido,
                a.TipoCambioValor,
                a.Vigente,
                a.CreatedAt))]);

    /// <summary>Ajuste por diferencia de cambio tal y como sale por la API.</summary>
    private static AjusteDiferenciaCambioRespuesta AAjusteRespuesta(AjusteDiferenciaCambio a) => new(
        a.Id,
        a.FacturaId,
        a.Importe,
        a.Moneda,
        a.TipoCambioValor,
        a.Motivo,
        a.Ip,
        a.AnuladoAt is null,
        a.CreatedAt,
        a.AnuladoAt,
        a.AnuladoPor,
        a.MotivoAnulacion);

    /// <summary>
    /// Lo que va en la auditoría firmada del ajuste. Los identificadores van como texto y las fechas
    /// como día de negocio, como en el resto.
    /// </summary>
    private static object DatosAuditoriaAjuste(AjusteDiferenciaCambio a) => new
    {
        a.Id,
        Factura = a.FacturaId.ToString("D"),
        a.Importe,
        Moneda = a.Moneda.ToString(),
        a.TipoCambioValor,
        a.Motivo,
        a.MotivoAnulacion,
    };

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
    /// Una línea de la propuesta: qué factura, cuánto se le aplica en su moneda, cuánto salió de la caja
    /// para ello y con qué cambio. Se convierte en fila de <c>cobro_asignacion</c> al guardar y es lo
    /// que devuelve la simulación.
    /// <para>
    /// <paramref name="ImporteRecibido"/> y <paramref name="Cambio"/> van a null cuando la factura
    /// está en la moneda del pago: ahí no hubo conversión y guardar un cambio de 1 sería afirmar que
    /// la hubo. <see cref="Consumido"/> da el número con el que se consume lo que trae el cobro, que
    /// es siempre la moneda recibida y por eso no necesita mirar el caso especial.
    /// </para>
    /// </summary>
    private sealed record LineaPlan(
        Factura Factura,
        decimal Importe,
        decimal? ImporteRecibido,
        decimal Cambio,
        decimal PendienteAntes)
    {
        /// <summary>Cuánto del cobro se gasta en esta línea, siempre en la moneda recibida.</summary>
        public decimal Consumido => ImporteRecibido ?? Importe;

        public static LineaPlan Nueva(
            Factura factura,
            decimal importe,
            decimal? importeRecibido,
            decimal cambio,
            decimal pendienteAntes)
            => new(factura, importe, importeRecibido, cambio, pendienteAntes);

        public CobroAsignacion Entidad => new()
        {
            FacturaId = Factura.Id,
            Factura = Factura,
            Importe = Importe,
            ImporteRecibido = ImporteRecibido,
            TipoCambioValor = ImporteRecibido is null ? null : Redondeo.TipoCambio(Cambio),
            Vigente = true,
        };

        public CobroAsignacionSimulada Simulada() => new(
            Factura.Id,
            Factura.Numero,
            Factura.FechaVencimiento,
            Factura.Moneda,
            Importe,
            ImporteRecibido,
            ImporteRecibido is null ? null : Redondeo.TipoCambio(Cambio),
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