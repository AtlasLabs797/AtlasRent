using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Contratos (docs/ESPECIFICACION.md §3.5).
/// <list type="bullet">
/// <item>
/// Flujo: <c>Borrador → PendienteAprobacion → Aprobado → Activo → BajaProgramada → Finalizado →
/// Liquidado</c>. El rechazo vuelve a borrador y exige comentario. Cualquier transición que no esté en
/// la tabla se rechaza con 409.
/// </item>
/// <item>
/// Reparto de permisos (§2): el empleado crea y edita borradores y los envía a aprobación;
/// <b>solo gerente y administrador</b> aprueban, rechazan, activan, dan de baja, liquidan y
/// modifican activos. Eso se comprueba en el endpoint (atributo <c>[Accion]</c>) y <b>otra vez
/// aquí</b>, contra el techo de permisos del usuario que llama, porque el atributo protege la
/// entrada por HTTP y nada más.
/// </item>
/// <item>
/// La solicitud caduca si el contrato cambia: al enviar se guarda la huella del contenido; si el
/// contrato se edita mientras espera, aprobar falla con 409 y hay que enviarla otra vez. Lo explica
/// <see cref="HuellaContrato"/>.
/// </item>
/// <item>
/// Al activar, el contrato toma la unidad: escribe el candado en <c>ocupacion_rango</c> y deja en
/// conflicto las reservas activas que se solapan, <b>todo en la misma transacción</b> que el cambio
/// de estado. Un fallo a media alta no deja la unidad bloqueada sin contrato.
/// </item>
/// <item>
/// Al aprobar se <b>congela</b> la plantilla: se copia su texto y el de los anexos que declara a
/// <see cref="ContratoPlantillaCongelada"/>, en la misma transacción que la aprobación. Editar o
/// desactivar la plantilla después no cambia lo que dice un contrato ya aprobado, y el PDF sale de
/// esa copia.
/// </item>
/// <item>
/// Nada se borra. El historial va a <see cref="ContratoVersion"/> y las solicitudes a
/// <see cref="SolicitudAprobacion"/>.
/// </item>
/// </list>
/// </summary>
public sealed class ServicioContratos(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioDisponibilidad disponibilidad,
    ServicioReservas reservas,
    ServicioPlantillas plantillas,
    ServicioInventario inventario,
    IRegistroAlertas alertas,
    IPolicyService politica,
    TimeProvider reloj)
{
    /// <summary>
    /// Los únicos cambios de estado permitidos (§3.5). Lo que no esté en esta tabla se rechaza: el
    /// flujo es lineal y el rechazo es la única vuelta atrás que existe, porque vuelve a borrador
    /// para corregirlo y volver a enviarlo.
    /// </summary>
    private static readonly Dictionary<EstadoContrato, EstadoContrato[]> Transiciones = new()
    {
        [EstadoContrato.Borrador] = [EstadoContrato.PendienteAprobacion],
        [EstadoContrato.PendienteAprobacion] = [EstadoContrato.Aprobado, EstadoContrato.Borrador],
        [EstadoContrato.Aprobado] = [EstadoContrato.Activo],
        [EstadoContrato.Activo] = [EstadoContrato.BajaProgramada],
        // Anular la baja antes de que llegue la fecha es un botón normal de la ficha: si no existiera,
        // equivocarse al marcar la baja dejaría el contrato en un estado del que no se puede volver,
        // y la especificación dice que la baja es sin penalización.
        [EstadoContrato.BajaProgramada] = [EstadoContrato.Activo, EstadoContrato.Finalizado],
        [EstadoContrato.Finalizado] = [EstadoContrato.Liquidado],
        [EstadoContrato.Liquidado] = [],
    };

    private static readonly Dictionary<string, Func<IQueryable<Contrato>, bool, IOrderedQueryable<Contrato>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            ["inicio"] = (q, desc) => desc ? q.OrderByDescending(c => c.FechaInicio) : q.OrderBy(c => c.FechaInicio),
            ["renta"] = (q, desc) => desc ? q.OrderByDescending(c => c.Renta) : q.OrderBy(c => c.Renta),
            ["estado"] = (q, desc) => desc ? q.OrderByDescending(c => c.Estado) : q.OrderBy(c => c.Estado),
        };

    // ---------------------------------------------------------------- lectura

    public Task<Pagina<ContratoResumen>> ListarAsync(
        ConsultaLista paginacion,
        string? busqueda,
        EstadoContrato? estado,
        Guid? clienteId,
        Guid? unidadId,
        CancellationToken ct)
    {
        IQueryable<Contrato> consulta = contexto.Contrato.AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Unidad);

        if (estado is { } filtro)
        {
            consulta = consulta.Where(c => c.Estado == filtro);
        }

        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(c => c.ClienteId == cliente);
        }

        if (unidadId is { } unidad)
        {
            consulta = consulta.Where(c => c.UnidadId == unidad);
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var termino = busqueda.Trim().ToLowerInvariant();
            consulta = consulta.Where(c =>
                (c.Cliente != null && c.Cliente.Nombre.ToLower().Contains(termino)) ||
                (c.Unidad != null && c.Unidad.Nombre.ToLower().Contains(termino)) ||
                c.PlantillaContratoId.ToLower().Contains(termino));
        }

        return ConsultaPaginada.PaginarAsync(consulta, Ordenes, "createdAt", paginacion, AResumen, ct);
    }

    public async Task<ContratoRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var contrato = await CargarAsync(id, ct);
        var versiones = await contexto.ContratoVersion.AsNoTracking()
            .Where(v => v.ContratoId == id)
            .OrderBy(v => v.Numero)
            .ToListAsync(ct);
        var solicitudes = await contexto.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.ContratoId == id)
            .OrderBy(s => s.SolicitadoAt)
            .ToListAsync(ct);

        return new ContratoRespuesta(
            contrato.Id,
            contrato.ClienteId,
            contrato.Cliente?.Nombre,
            contrato.UnidadId,
            contrato.Unidad?.Nombre,
            contrato.FacturadoraId,
            contrato.Facturadora?.Nombre,
            contrato.FechaInicio,
            contrato.DuracionInicialMeses,
            contrato.FechaFinInicial,
            contrato.FechaFinVigente,
            contrato.Renta,
            contrato.Moneda,
            contrato.DiaPago,
            contrato.Fianza,
            contrato.ReglaRenovacion,
            contrato.RenovacionMeses,
            contrato.PlantillaContratoId,
            contrato.PlantillaContratoCongeladaId,
            contrato.PlantillaCongeladaAt,
            contrato.Estado,
            contrato.EstadoActualDesde,
            contrato.EstadoActualComentario,
            contrato.FechaBajaProgramada,
            contrato.FechaFinReal,
            contrato.Observaciones,
            versiones.Select(AVersion).ToList(),
            solicitudes.Select(ASolicitud).ToList(),
            contrato.CreatedAt);
    }

    // ---------------------------------------------------------------- alta y edición

    /// <summary>
    /// Alta en borrador. Los obligatorios de §3.5 se comprueban aquí, no en el modelo, para poder
    /// decir cuál falta en el mensaje.
    /// </summary>
    public async Task<ContratoResumen> CrearAsync(
        ContratoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        Validar(peticion, null);
        await ExigirReferenciasExistentesAsync(peticion, ct);

        var contrato = new Contrato
        {
            Estado = EstadoContrato.Borrador,
            EstadoActualDesde = TiempoNegocio.Hoy(),
            EstadoActualPorId = usuario,
        };

        Aplicar(contrato, peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: true,
            accion: "contrato.creado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Edición de un borrador o de un contrato pendiente de aprobación. Es el camino del empleado
    /// (<c>contrato.borrador</c>, docs/ESPECIFICACION.md §2).
    /// <para>
    /// Editar un contrato pendiente es legal y es justo el caso que obliga a que la solicitud
    /// caduque: el empleado puede corregir algo mientras el gerente lo mira, pero entonces el gerente
    /// tiene que volver a mirarlo. Si la edición no existiera, el hueco se taparía prohibiéndola, y
    /// entonces corregir un error obligaría a rechazar el contrato entero.
    /// </para>
    /// <para>
    /// Un contrato ya aprobado o vigente no se edita por aquí: el activo se modifica por
    /// <see cref="AplicarCambiosAsync"/>, que crea versión.
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> ActualizarAsync(
        Guid id,
        ContratoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var contrato = await CargarAsync(id, ct);
        if (contrato.Estado is not (EstadoContrato.Borrador or EstadoContrato.PendienteAprobacion))
        {
            throw new InvalidOperationException(
                $"Un contrato en estado {Texto(contrato.Estado)} ya no se edita. " +
                "Un contrato activo se modifica por su cambio en activo, que genera versión nueva.");
        }

        Validar(peticion, contrato);

        var huellaAntes = HuellaContrato.Calcular(contrato);
        Aplicar(contrato, peticion);
        var cambia = HuellaContrato.Calcular(contrato) != huellaAntes;

        // Editar un contrato pendiente es lo que caduca la solicitud: el contenido que se mandó a
        // aprobar ya no es el que hay. Si no cambiara nada, la solicitud sigue viva.
        Func<Task>? caducar = cambia && contrato.Estado == EstadoContrato.PendienteAprobacion
            ? () => CaducarSolicitudAsync(contrato, usuario, ct)
            : null;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.editado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: caducar,
            ct: ct);
    }

    /// <summary>
    /// Envía el contrato a aprobación: deja el estado en pendiente y abre una solicitud con la huella
    /// del contenido que se manda.
    /// <para>
    /// El estado cambia <b>después</b> de calcular la huella y la huella no incluye el estado, así que
    /// el propio envío no caduca su propia solicitud: aprobar lo recién enviado funciona.
    /// </para>
    /// <para>
    /// Si ya había una solicitud viva se cancela antes de abrir la nueva, porque el índice único
    /// parcial de <c>solicitud_aprobacion</c> solo admite una por contrato.
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> EnviarAprobacionAsync(
        Guid id,
        ContratoEnvioPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.Borrador, EstadoContrato.PendienteAprobacion);

        var solicitud = new SolicitudAprobacion
        {
            Id = Guid.NewGuid(),
            CreatedAt = reloj.GetUtcNow(),
            CreatedBy = usuario,
            ContratoId = contrato.Id,
            Resultado = ResultadoSolicitud.Pendiente,
            HuellaContenido = HuellaContrato.Calcular(contrato),
            SolicitadoPorId = usuario,
            SolicitadoAt = reloj.GetUtcNow(),
            Comentario = Limpiar(peticion.Comentario),
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.enviado_aprobacion",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: async () =>
            {
                await CaducarSolicitudAsync(contrato, usuario, ct, "sustituida por un envío nuevo");
                // Reenviar un contrato que ya estaba pendiente (porque su solicitud caducó) no es una
                // transición: el estado no se mueve, solo se abre una solicitud nueva con el
                // contenido de ahora.
                if (contrato.Estado == EstadoContrato.Borrador)
                {
                    CambiarEstado(contrato, EstadoContrato.PendienteAprobacion, usuario, solicitud.Comentario);
                }

                contexto.SolicitudAprobacion.Add(solicitud);
            },
            ct: ct);
    }

    /// <summary>
    /// Aprobación. Exige <c>contrato.estado</c> (docs/ESPECIFICACION.md §2: solo gerente y
    /// administrador) y la comprueba <b>aquí</b>, no solo en el atributo del endpoint: el atributo
    /// protege la entrada por HTTP y el criterio de F2-05 es que no exista ningún camino por el que un
    /// empleado apruebe. La denegación se audita con el mismo mecanismo que el filtro global.
    /// <para>
    /// Además exige que la solicitud siga viva y que la huella guardada sea la del contrato: si
    /// cambió, 409 y hay que volver a enviar. Al aprobar se congela la plantilla (§3.5).
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> AprobarAsync(
        Guid id,
        Guid aprobador,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, aprobador, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.PendienteAprobacion);

        var solicitud = await CargarSolicitudVivaAsync(id, ct);

        // El contrato tiene que seguir siendo el mismo que se mandó a aprobar. Normalmente la
        // caducidad ya lo dejó escrito al editar; esta comparación es la red para un cambio que
        // llegara por fuera del servicio.
        if (!string.Equals(solicitud.HuellaContenido, HuellaContrato.Calcular(contrato), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"La solicitud de aprobación caducó: el contrato cambió después de enviarse y lo que hay " +
                "ahora no es lo que se pidió aprobar. Vuelve a enviarlo a aprobación.");
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.aprobado",
            entidad: "contrato",
            aprobador,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: async () =>
            {
                ResolverSolicitud(solicitud, ResultadoSolicitud.Aprobada, aprobador);
                CambiarEstado(contrato, EstadoContrato.Aprobado, aprobador, "Aprobado.");
                // La plantilla se congela al aprobar (§3.5): a partir de aquí, editar la plantilla del
                // contrato ya no cambia la que usa este contrato. Copia el texto y los anexos a la fila
                // congelada en esta misma transacción, porque una aprobación sin copia es un contrato
                // aprobado que no sabe qué texto firmó.
                contrato.PlantillaContratoCongeladaId = contrato.PlantillaContratoId;
                contrato.PlantillaCongeladaAt = reloj.GetUtcNow();
                await plantillas.CongelarAsync(contrato, ct);
            },
            ct: ct);
    }

    /// <summary>
    /// Rechazo: vuelve el contrato a borrador y <b>exige comentario</b> (§3.5). Sin comentario no hay
    /// rechazo, porque si no el empleado no sabe qué corregir.
    /// </summary>
    public async Task<ContratoResumen> RechazarAsync(
        Guid id,
        ContratoRechazoPeticion peticion,
        Guid rechazante,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, rechazante, ct);

        var comentario = Limpiar(peticion.Comentario);
        if (comentario is null)
        {
            throw new InvalidOperationException(
                "El rechazo necesita un comentario: el contrato vuelve a borrador y hay que saber qué corregir.");
        }

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.PendienteAprobacion);

        var solicitud = await CargarSolicitudVivaAsync(id, ct);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.rechazado",
            entidad: "contrato",
            rechazante,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                ResolverSolicitud(solicitud, ResultadoSolicitud.Rechazada, rechazante, comentario);
                CambiarEstado(contrato, EstadoContrato.Borrador, rechazante, comentario);
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>
    /// Activación: aquí el contrato toma la unidad.
    /// <para>
    /// <b>Por qué en activar y no en aprobar.</b> §3.5 dice que el inventario del contrato se copia y
    /// congela al activar, y el candado de la unidad es la misma cosa: el contrato pasa a ser lo que
    /// ocupa la unidad cuando empieza a estar vigente. Bloquearla al aprobar dejaría la unidad
    /// bloqueada desde la aprobación, que es un estado en el que aún se puede rechazar, y
    /// rechazar con la unidad bloqueada obligaría a limpiar a mano un candado que el sistema escribió.
    /// </para>
    /// <para>
    /// El candado es un rango abierto desde la fecha de inicio (<c>fecha_fin</c> nulo): la renovación
    /// automática de F2-06 es la que alarga el contrato, y un rango cerrado obligaría a tocar el
    /// candado en cada renovación, con la ventana en la que la unidad queda libre sin querer.
    /// </para>
    /// <para>
    /// Las reservas activas que se solapan pasan a <c>EnConflicto</c> y sueltan su candado, porque
    /// dos candados vivos que se solapan no pueden existir (es lo que impone la base de F2-01) y
    /// porque, con el contrato ya vigente, quien manda sobre la unidad es el contrato. Todo va en la
    /// misma transacción que el cambio de estado: si algo falla, la unidad no queda bloqueada sin
    /// contrato.
    /// </para>
    /// <para>
    /// Aquí se congela también el inventario de la unidad (F2-08, §3.5: "el inventario se copia y
    /// congela al activar"), por la misma razón: un contrato activo sin inventario congelado ya no
    /// podría demostrar qué había cuando entró el cliente. Una unidad sin inventario base se activa
    /// igual y sale sin inventario, que es el caso normal (§3.1).
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> ActivarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.Aprobado);

        if (contrato.PlantillaContratoCongeladaId is null)
        {
            throw new InvalidOperationException(
                "Un contrato sin plantilla congelada no se activa: hay que aprobarlo antes.");
        }

        var version = ConstruirVersion(contrato, 1, "Alta del contrato activo.", null, usuario);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.activado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: async () =>
            {
                await ResolverReservasSolapadasAsync(contrato, usuario, ip, ct);
                await disponibilidad.OcuparRangoAsync(
                    contrato.UnidadId, OrigenOcupacion.Contrato, contrato.Id, contrato.FechaInicio, null, ct);
                CambiarEstado(contrato, EstadoContrato.Activo, usuario, "Activado.");
                contexto.ContratoVersion.Add(version);
                // F2-08 (§3.5): el inventario de la unidad se copia y se congela aquí, no antes. Va
                // en el mismo antesDeGuardar que el cambio de estado y el candado de la unidad, así que
                // el inventario congelado, el estado activo y el candado salen en un solo commit o no
                // sale ninguno. Una unidad sin inventario base no tiene nada que congelar y activa
                // igual (§3.1 lo hace opcional). Mismo patrón que la plantilla congelada de F2-07.
                await inventario.CongelarAsync(contrato, ct);
            },
            ct: ct);
    }

    /// <summary>
    /// Cambio en un contrato vigente: genera versión nueva (§3.5). Exige <c>contrato.estado</c>, que
    /// en la matriz §2 no le viene al empleado.
    /// <para>
    /// El cambio de renta <b>solo afecta a facturas futuras</b> (§3.5): lo ya facturado se queda
    /// con lo que se facturó y, como las facturas son de F3-01, aquí lo que se deja escrito es el
    /// alcance en la versión y la renta anterior en el historial. Quien genere las facturas lee el
    /// alcance y no toca lo emitido.
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> AplicarCambiosAsync(
        Guid id,
        ContratoCambioPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.Activo);

        var motivo = Limpiar(peticion.Motivo);
        if (motivo is null)
        {
            throw new InvalidOperationException(
                "Un cambio en contrato vigente necesita un motivo: va escrito en la versión nueva.");
        }

        var rentaAnterior = contrato.Renta;
        var plantillaAnterior = contrato.PlantillaContratoId;
        var huellaAntes = HuellaContrato.Calcular(contrato);

        var cambio = AComoPeticion(peticion, contrato);
        Validar(cambio, contrato);
        Aplicar(contrato, cambio);

        // Un cambio que no toca nada de lo que va a la huella no merece versión: se rechaza para que
        // el historial signifique algo.
        if (HuellaContrato.Calcular(contrato) == huellaAntes)
        {
            throw new InvalidOperationException(
                "El cambio no modifica nada del contrato: no hay nada que versionar.");
        }

        var descripcion = new List<string>();
        if (rentaAnterior != contrato.Renta)
        {
            descripcion.Add($"renta {Importe(rentaAnterior)} -> {Importe(contrato.Renta)} {contrato.Moneda}");
        }

        if (plantillaAnterior != contrato.PlantillaContratoId)
        {
            descripcion.Add($"plantilla {plantillaAnterior} -> {contrato.PlantillaContratoId}");
        }

        var texto = motivo
            + (descripcion.Count > 0 ? $" ({string.Join("; ", descripcion)})" : string.Empty);

        var version = ConstruirVersion(contrato, await NumeroSiguienteAsync(id, ct), texto, rentaAnterior, usuario);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.cambio_aplicado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                contexto.ContratoVersion.Add(version);
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>
    /// Baja programada con su fecha efectiva de fin (§3.5: preaviso recomendado de 30 días, sin
    /// penalización). El contrato sigue vigente y bloqueando la unidad hasta que se finalice.
    /// </summary>
    public async Task<ContratoResumen> DarBajaAsync(
        Guid id,
        ContratoBajaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        if (peticion.FechaFin is not { } fechaFin)
        {
            throw new InvalidOperationException(
                "La baja necesita su fecha efectiva de fin: sin fecha no hay baja, hay una intención.");
        }

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.Activo);

        // §3.5: baja en cualquier fecha, también antes del fin del bloque en curso (decisión del
        // responsable, BUG-024). Solo se pide que no sea pasada ni anterior al inicio del contrato.
        if (fechaFin < TiempoNegocio.Hoy() || fechaFin < contrato.FechaInicio)
        {
            throw new InvalidOperationException(
                $"La baja no puede ser anterior a hoy ni al inicio del contrato ({contrato.FechaInicio:yyyy-MM-dd}).");
        }

        contrato.FechaBajaProgramada = fechaFin;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.baja_programada",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                CambiarEstado(contrato, EstadoContrato.BajaProgramada, usuario, Limpiar(peticion.Comentario));
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>Anula una baja que aún no ha llegado. Devuelve el contrato a activo.</summary>
    public async Task<ContratoResumen> AnularBajaAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.BajaProgramada);

        contrato.FechaBajaProgramada = null;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.baja_anulada",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () =>
            {
                CambiarEstado(contrato, EstadoContrato.Activo, usuario, "Baja anulada.");
                return Task.CompletedTask;
            },
            ct: ct);
    }

    /// <summary>
    /// Fin de la relación: <b>libera la unidad</b> (§3.5: "el fin libera la unidad"). El candado se
    /// apaga, no se borra: la fila se queda con <c>bloquea = false</c> para que se pueda auditar por
    /// qué estuvo ocupada (AGENTS.md §6).
    /// </summary>
    public async Task<ContratoResumen> FinalizarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.BajaProgramada);

        contrato.FechaFinReal = contrato.FechaBajaProgramada ?? TiempoNegocio.Hoy();

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.finalizado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: async () =>
            {
                await disponibilidad.LiberarRangoAsync(OrigenOcupacion.Contrato, contrato.Id, ct);
                CambiarEstado(contrato, EstadoContrato.Finalizado, usuario, "Finalizado.");

                // §3.5: "alerta persistente hasta liquidar". Fin ≠ liquidación: liberar la unidad no
                // cierra el contrato, así que desde este mismo instante y en la misma transacción
                // nace la alerta que avisa de que falta la liquidación. Va aquí, y no solo en el
                // trabajo de las 07:00, porque finalizar a mano es igual de válido que finalizar por
                // fecha y dejaría el contrato sin aviso hasta el día siguiente.
                await ServicioFines.RefrescarAlertaFirmezaAsync(
                    alertas,
                    contrato.Id,
                    contrato.FechaFinReal!.Value,
                    TiempoNegocio.Hoy(),
                    ct);
            },
            ct: ct);
    }

    /// <summary>
    /// Cierre: el contrato queda liquidado y no se toca más. Y ya no es solo una etiqueta (§3.5: "la
    /// liquidación cierra inventario de salida, fianza y saldos").
    /// <para>
    /// Hace tres cosas, y las tres en la misma transacción que el cambio de estado:
    /// <list type="number">
    /// <item>exige la revisión de salida firmada si el contrato tiene inventario (§3.6);</item>
    /// <item>deja escrito en <see cref="ContratoLiquidacion"/> la fecha, quién liquida, el importe a
    /// cobrar por faltantes que propuso la revisión y la decisión sobre la fianza;</item>
    /// <item>cierra la alerta persistente de §3.5, porque el problema que vigilaba ya no existe.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Lo que NO hace, y no por descuido.</b> No emite cobros ni mueve la fianza: no existen las
    /// tablas (<c>cobro</c> es de F3-01 y las de fianza también), y escribir una fila de cobro inventando
    /// la tabla sería dejar en la base una deuda que nadie puede ver ni saldar. Se escribe lo que sí
    /// es un dato de la liquidación y el movimiento queda enlazado con el <c>TODO(A#)</c> que está
    /// dentro del <c>antesDeGuardar</c>, con la firma exacta que necesitará F3-01.
    /// </para>
    /// </summary>
    public async Task<ContratoResumen> LiquidarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId: id, usuario, ct);

        var contrato = await CargarAsync(id, ct);
        Exigir(contrato, EstadoContrato.Finalizado);

        // Lo que la revisión de salida dejó escrito. Se lee aquí, y no después, porque el importe de
        // los faltantes y la decisión sobre la fianza tienen que salir de lo que firmó el cliente y no
        // de lo que calcula este servicio. Sin revisión no hay nada que liquidar de inventario: §3.6
        // dice "si el contrato tiene inventario" y §3.1 deja el inventario base como opcional.
        var revision = await contexto.RevisionInventario
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContratoId == id, ct);

        // El importe ya viene redondeado por Redondeo al firmarse la revisión (F2-08); aquí no se
        // recalcula ni se vuelve a redondear, que un redondeo aplicado dos veces no es un redondeo.
        var importeFaltantes = revision?.ImportePropuesto ?? 0m;

        // La decisión se deriva, no se pregunta: hay faltante -> la fianza se retiene (§3.6, "los
        // descuentos se aplican contra la fianza solo con gerente/admin", y liquidar ya exige ese
        // permiso); no hay faltante -> se devuelve entera. Lo que el faltante supera a la fianza no es
        // una retención, es un cobro pendiente, y el cobro es de F3-01.
        var decision = importeFaltantes > 0m
            ? DecisionFianza.RetenidaPorFaltantes
            : DecisionFianza.Devuelta;

        var retenida = decision is DecisionFianza.RetenidaPorFaltantes
            ? Math.Min(importeFaltantes, contrato.Fianza)
            : 0m;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Contrato, contrato,
            esAlta: false,
            accion: "contrato.liquidado",
            entidad: "contrato",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: async () =>
            {
                // Primero la condición de F2-08, dentro de la transacción y no antes: si el contrato
                // tiene inventario y no hay revisión firmada, el 409 tiene que dejar el contrato
                // exactamente igual que estaba, y no con el inventario cerrado a medias.
                await inventario.ExigirRevisionFirmadaAsync(id, ct);

                CambiarEstado(contrato, EstadoContrato.Liquidado, usuario, "Liquidado.");

                // TODO(A#) F3-01: emitir el cobro por el importe de faltantes registrado aquí y mover
                // la fianza según esta decisión. La firma que hará falta cuando existan las tablas
                // `cobro` y `fianza` es, dentro de este mismo `antesDeGuardar` y en esta misma
                // transacción para que un cobro no exista sin liquidación ni al revés:
                //
                //     public Task EmitirLiquidacionAsync(
                //         Guid contratoId,
                //         Guid usuario,
                //         Guid contratoLiquidacionId,
                //         CancellationToken ct)
                //
                // y dentro, enlazada con el fallo que se acaba de registrar:
                //
                //     INSERT INTO cobro (contrato_liquidacion_id, importe, concepto, ...)
                //     VALUES ({contratoLiquidacionId}, {contratoLiquidacion.ImporteFaltantes},
                //             'Faltantes de inventario a la salida', ...);
                //
                //     INSERT INTO fianza_devolucion (fianza_id, importe_retenido, importe_devuelto,
                //                                   decision, ...)
                //     VALUES ({fianzaId}, {contratoLiquidacion.FianzaRetenida},
                //             {contratoLiquidacion.FianzaContrato - contratoLiquidacion.FianzaRetenida},
                //             {contratoLiquidacion.DecisionFianza}, ...);
                //
                // Los tres importes ya están calculados y redondeados en ContratoLiquidacion: F3-01 no
                // tiene que volver a la revisión de salida ni al contrato, solo leer esta fila.
                // Mientras esas tablas no existan, esto es un dato y no un movimiento de dinero: nadie
                // ha cobrado el faltante y la fianza sigue donde estaba.

                var liquidacion = new ContratoLiquidacion
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = reloj.GetUtcNow(),
                    CreatedBy = usuario,
                    ContratoId = contrato.Id,
                    RevisionInventarioId = revision?.Id,
                    FechaLiquidacion = TiempoNegocio.Hoy(),
                    LiquidadoPorId = usuario,
                    ImporteFaltantes = importeFaltantes,
                    DecisionFianza = decision,
                    FianzaContrato = contrato.Fianza,
                    FianzaRetenida = retenida,
                    Observacion = null,
                };

                contexto.ContratoLiquidacion.Add(liquidacion);

                // §3.5 cierra el círculo: la alerta persistente estaba viva porque faltaba la
                // liquidación, así que se resuelve en la misma transacción que la escribe. Si el
                // fallo de la liquidación se deshace, la alerta sigue viva, que es lo que tiene que
                // pasar.
                await alertas.ResolverAsync(ServicioFines.ClaveFirmezaPendiente(contrato.Id), ct);
            },
            ct: ct);
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Segunda puerta del permiso de estado. El endpoint lo pide con <c>[Accion(ContratoEstado)]</c>,
    /// pero el atributo protege la entrada por HTTP y nada más: si mañana un trabajo, un endpoint mal
    /// cableado o una llamada directa al servicio saltan el filtro, esta comprobación sigue en pie.
    /// Por eso el servicio recibe al usuario que va a aprobar y mira su techo de permisos en
    /// <c>MatrizPermisos</c>, en vez de fiarse del atributo.
    /// </summary>
    private async Task ExigirEstadoAsync(Guid contratoId, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.ContratoEstado, usuario, ct))
        {
            return;
        }

        var recurso = $"contrato/{contratoId:D}";
        await politica.AuditarDenegadoAsync(Acciones.ContratoEstado, recurso, ct);

        throw new PermisoDenegadoException(Acciones.ContratoEstado, recurso);
    }

    /// <summary>
    /// Deja en conflicto las reservas activas de la unidad que se solapan con el rango del contrato
    /// y sueltan su candado. Va en la misma transacción que la activación.
    /// <para>
    /// El candado se suelta aquí, y no en <c>MarcarEnConflictoAsync</c>, a propósito: una reserva en
    /// conflicto <b>sigue</b> bloqueando mientras el contrato no esté activo (§3.4: "sigue bloqueando
    /// hasta que el gerente decida"), y eso es lo que comprueba F2-03. Pero en cuanto hay un contrato
    /// vigente encima, los dos candados no pueden coexistir (la base de F2-01 no lo permite) y quien
    /// manda sobre la unidad es el contrato: la reserva en conflicto deja de bloquear y el cliente
    /// busca otra unidad, que es lo que §3.4 le dice que haga.
    /// </para>
    /// </summary>
    private async Task ResolverReservasSolapadasAsync(
        Contrato contrato,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        var activas = await contexto.Reserva.AsNoTracking()
            .Where(r => r.UnidadId == contrato.UnidadId
                && r.Estado == EstadoReserva.Activa
                && r.Activo)
            .Select(r => new { r.Id, r.Tipo })
            .ToListAsync(ct);

        foreach (var reserva in activas)
        {
            var solapa = reserva.Tipo == TipoReserva.Mensual
                // La mensual bloquea un rango abierto (fecha_fin nulo), así que siempre se solapa con
                // el del contrato, que también es abierto.
                || await contexto.OcupacionDia.AnyAsync(
                    d => d.Origen == OrigenOcupacion.ReservaCorta
                        && d.OrigenId == reserva.Id
                        && d.Bloquea
                        && d.Fecha >= contrato.FechaInicio,
                    ct);

            if (!solapa)
            {
                continue;
            }

            await reservas.MarcarEnConflictoAsync(reserva.Id, usuario, ip, ct);

            // La base no admite dos candados vivos que se solapan, así que el de la reserva se apaga
            // antes de escribir el del contrato.
            switch (reserva.Tipo)
            {
                case TipoReserva.Mensual:
                    await disponibilidad.LiberarRangoAsync(OrigenOcupacion.ReservaMensual, reserva.Id, ct);
                    break;
                case TipoReserva.Corta:
                    await disponibilidad.LiberarDiasAsync(OrigenOcupacion.ReservaCorta, reserva.Id, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Caduca la solicitud viva del contrato. La fila no se borra: queda con
    /// <see cref="ResultadoSolicitud.Caducada"/> y su motivo, que es lo que la bandeja de F2-10
    /// necesita para no enseñar un contrato viejo como si estuviera esperando decisión.
    /// </summary>
    private async Task CaducarSolicitudAsync(
        Contrato contrato,
        Guid usuario,
        CancellationToken ct,
        string? motivo = null)
    {
        var solicitud = await contexto.SolicitudAprobacion
            .Where(s => s.ContratoId == contrato.Id && s.Resultado == ResultadoSolicitud.Pendiente)
            .OrderByDescending(s => s.SolicitadoAt)
            .FirstOrDefaultAsync(ct);

        if (solicitud is null)
        {
            return;
        }

        ResolverSolicitud(
            solicitud,
            ResultadoSolicitud.Caducada,
            usuario,
            comentario: null,
            motivo: motivo ?? "El contrato cambió después de enviarse a aprobación.");
    }

    private static void ResolverSolicitud(
        SolicitudAprobacion solicitud,
        ResultadoSolicitud resultado,
        Guid usuario,
        string? comentario = null,
        string? motivo = null)
    {
        solicitud.Resultado = resultado;
        solicitud.ResueltoPorId = usuario;
        solicitud.ResueltoAt = DateTimeOffset.UtcNow;
        if (comentario is not null)
        {
            solicitud.Comentario = comentario;
        }

        if (motivo is not null)
        {
            solicitud.MotivoCaducidad = motivo;
        }

        solicitud.UpdatedAt = DateTimeOffset.UtcNow;
        solicitud.UpdatedBy = usuario;
    }

    private async Task<SolicitudAprobacion> CargarSolicitudVivaAsync(Guid contratoId, CancellationToken ct) =>
        await contexto.SolicitudAprobacion
            .Where(s => s.ContratoId == contratoId && s.Resultado == ResultadoSolicitud.Pendiente)
            .OrderByDescending(s => s.SolicitadoAt)
            .FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            "Este contrato no tiene ninguna solicitud de aprobación viva. Vuelve a enviarlo a aprobación.");

    /// <summary>
    /// Siguiente número de versión. Se lee el máximo dentro de la transacción del cambio y el
    /// índice único (contrato_id, numero) es la red: si dos cambios a la vez cogieran el mismo
    /// número, uno falla y no se cuelga ni queda un historial con huecos sin avisar.
    /// </summary>
    private async Task<int> NumeroSiguienteAsync(Guid contratoId, CancellationToken ct)
    {
        var maximo = await contexto.ContratoVersion
            .Where(v => v.ContratoId == contratoId)
            .MaxAsync(v => (int?)v.Numero, ct);

        return maximo is { } valor ? valor + 1 : 1;
    }

    private ContratoVersion ConstruirVersion(
        Contrato contrato,
        int numero,
        string cambio,
        decimal? rentaAnterior,
        Guid usuario)
    {
        var ahora = reloj.GetUtcNow();

        return new ContratoVersion
        {
            Id = Guid.NewGuid(),
            CreatedAt = ahora,
            CreatedBy = usuario,
            ContratoId = contrato.Id,
            Numero = numero,
            Cambio = cambio,
            RentaAnterior = rentaAnterior,
            RentaNueva = contrato.Renta,
            Moneda = contrato.Moneda,
            // El alcance va escrito en la versión porque es lo que F3-01 leerá para no tocar lo ya
            // facturado: "el cambio de renta afecta solo a facturas futuras" (§3.5).
            Alcance = "Afecta solo a facturas futuras. Lo ya facturado no cambia.",
        };
    }

    /// <summary>
    /// Los obligatorios de §3.5, antes de tocar la base de datos. Todas devuelven 409: no es un error
    /// de formato, es un contrato que no se puede guardar.
    /// </summary>
    private static void Validar(ContratoPeticion peticion, Contrato? existente)
    {
        if (peticion.ClienteId is not { } clienteId || clienteId == Guid.Empty)
        {
            throw new InvalidOperationException("El contrato necesita un cliente.");
        }

        if (peticion.UnidadId is not { } unidadId || unidadId == Guid.Empty)
        {
            throw new InvalidOperationException("El contrato necesita una unidad.");
        }

        if (peticion.FacturadoraId is not { } facturadoraId || facturadoraId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "El contrato necesita la subempresa que factura: la entidad que factura es la " +
                "subempresa, nunca el hotel.");
        }

        if (peticion.FechaInicio is not { } fechaInicio)
        {
            throw new InvalidOperationException("El contrato necesita su fecha de inicio.");
        }

        if (peticion.DuracionInicialMeses is not { } duracion || duracion <= 0)
        {
            throw new InvalidOperationException(
                "El contrato necesita su duración inicial en meses (un mes o más).");
        }

        if (peticion.Renta is not { } renta)
        {
            throw new InvalidOperationException("El contrato necesita su renta.");
        }

        if (renta <= 0)
        {
            throw new InvalidOperationException("La renta no puede ser cero ni negativa.");
        }

        // El enum llega del JSON: si el cliente manda un valor que no existe, o ninguno, se rechaza
        // aquí como regla de negocio y no como error de formato.
        if (peticion.Moneda is not (Moneda.USD or Moneda.DOP))
        {
            throw new InvalidOperationException(
                "El contrato necesita su moneda: los contratos van en USD o en DOP.");
        }

        if (peticion.DiaPago is not { } diaPago || diaPago is < 1 or > 31)
        {
            throw new InvalidOperationException(
                "El contrato necesita su día de pago, entre 1 y 31.");
        }

        if (peticion.Fianza is not { } fianza)
        {
            throw new InvalidOperationException("El contrato necesita el importe de la fianza.");
        }

        if (fianza < 0)
        {
            throw new InvalidOperationException("La fianza no puede ser negativa.");
        }

        ValidarRenovacion(peticion.ReglaRenovacion, peticion.RenovacionMeses);

        if (string.IsNullOrWhiteSpace(peticion.PlantillaContratoId))
        {
            throw new InvalidOperationException(
                "El contrato necesita su plantilla: es la que se congela al aprobar.");
        }

        // Un contrato ya aprobado tiene el inicio pactado con el cliente: cambiarlo por el mismo camino que
        // edita un borrador dejaría el bloque sin recalcular por la mitad.
        if (existente is { Estado: EstadoContrato.Aprobado } && fechaInicio != existente.FechaInicio)
        {
            throw new InvalidOperationException(
                "Un contrato aprobado no cambia sus fechas: el inicio ya está pactado con el cliente.");
        }
    }

    /// <summary>
    /// Regla y duración de las dos cosas que §3.5 pone como obligatorias. Con una regla fija la
    /// duración tiene que ser la de la regla; con <see cref="ReglaRenovacion.Personalizada"/> vale lo
    /// pactado, que es el "u otro" de la especificación.
    /// </summary>
    private static void ValidarRenovacion(ReglaRenovacion regla, int? meses)
    {
        if (regla is not (ReglaRenovacion.Mensual or ReglaRenovacion.Trimestral
            or ReglaRenovacion.Semestral or ReglaRenovacion.Anual or ReglaRenovacion.Personalizada))
        {
            throw new InvalidOperationException($"La regla de renovación '{regla}' no existe.");
        }

        if (meses is not { } duracion || duracion <= 0)
        {
            throw new InvalidOperationException(
                "El contrato necesita la duración de la renovación, en meses (un mes o más).");
        }

        var esperada = regla switch
        {
            ReglaRenovacion.Mensual => 1,
            ReglaRenovacion.Trimestral => 3,
            ReglaRenovacion.Semestral => 6,
            ReglaRenovacion.Anual => 12,
            _ => 0,
        };

        if (esperada > 0 && duracion != esperada)
        {
            throw new InvalidOperationException(
                $"La renovación {regla.ToString().ToLowerInvariant()} dura {esperada} meses, no {duracion}.");
        }
    }

    private async Task ExigirReferenciasExistentesAsync(ContratoPeticion peticion, CancellationToken ct)
    {
        if (!await contexto.Cliente.AnyAsync(c => c.Id == peticion.ClienteId, ct))
        {
            throw new KeyNotFoundException($"No existe el cliente '{peticion.ClienteId:D}'.");
        }

        if (!await contexto.Unidad.AnyAsync(u => u.Id == peticion.UnidadId, ct))
        {
            throw new KeyNotFoundException($"No existe la unidad '{peticion.UnidadId:D}'.");
        }

        if (!await contexto.Subempresa.AnyAsync(s => s.Id == peticion.FacturadoraId, ct))
        {
            throw new KeyNotFoundException($"No existe la subempresa '{peticion.FacturadoraId:D}'.");
        }
    }

    private static void Exigir(Contrato contrato, params EstadoContrato[] permitidos)
    {
        if (!permitidos.Contains(contrato.Estado))
        {
            throw new InvalidOperationException(
                $"Un contrato en estado {Texto(contrato.Estado)} no puede hacer eso. El flujo es " +
                "borrador → pendiente de aprobación → aprobado → activo → baja programada → " +
                "finalizado → liquidado.");
        }
    }

    private static void Aplicar(Contrato contrato, ContratoPeticion peticion)
    {
        contrato.ClienteId = peticion.ClienteId!.Value;
        contrato.UnidadId = peticion.UnidadId!.Value;
        contrato.FacturadoraId = peticion.FacturadoraId!.Value;
        contrato.FechaInicio = peticion.FechaInicio!.Value;
        contrato.DuracionInicialMeses = peticion.DuracionInicialMeses!.Value;
        contrato.FechaFinInicial = contrato.FechaInicio.AddMonths(contrato.DuracionInicialMeses);
        // El fin del bloque en curso solo lo mueve la renovación automática (F2-06) o una baja
        // anteriorizada; al editar, se respeta lo que ya estaba pactado.
        if (contrato.FechaFinVigente < contrato.FechaFinInicial)
        {
            contrato.FechaFinVigente = contrato.FechaFinInicial;
        }

        // La renta y la fianza son dinero: half-up a dos decimales, nunca el redondeo de la base.
        contrato.Renta = Redondeo.Importe(peticion.Renta!.Value);
        contrato.Moneda = peticion.Moneda!.Value;
        contrato.DiaPago = peticion.DiaPago!.Value;
        contrato.Fianza = Redondeo.Importe(peticion.Fianza!.Value);
        contrato.ReglaRenovacion = peticion.ReglaRenovacion;
        contrato.RenovacionMeses = peticion.RenovacionMeses!.Value;
        contrato.PlantillaContratoId = peticion.PlantillaContratoId!.Trim();
        contrato.Observaciones = Limpiar(peticion.Observaciones);
    }

    /// <summary>
    /// Los cambios de un contrato vigente, traducidos a la misma forma que el alta. Lo que no se
    /// toca (cliente, unidad, facturadora, inicio y duración inicial) viene del contrato que ya está
    /// escrito: ver <see cref="ContratoCambioPeticion"/>.
    /// </summary>
    private static ContratoPeticion AComoPeticion(ContratoCambioPeticion peticion, Contrato contrato) =>
        new()
        {
            ClienteId = contrato.ClienteId,
            UnidadId = contrato.UnidadId,
            FacturadoraId = contrato.FacturadoraId,
            FechaInicio = contrato.FechaInicio,
            DuracionInicialMeses = contrato.DuracionInicialMeses,
            Renta = peticion.Renta,
            Moneda = peticion.Moneda,
            DiaPago = peticion.DiaPago,
            Fianza = peticion.Fianza,
            ReglaRenovacion = peticion.ReglaRenovacion,
            RenovacionMeses = peticion.RenovacionMeses,
            PlantillaContratoId = peticion.PlantillaContratoId ?? contrato.PlantillaContratoId,
            Observaciones = peticion.Observaciones ?? contrato.Observaciones,
        };

    /// <summary>
    /// Aplica la tabla de transiciones y escribe de quién y por qué viene el cambio.
    /// <para>
    /// Es <c>internal</c> y no <c>private</c> porque <see cref="ServicioFines"/> tiene que finalizar
    /// contratos sin llamar a <see cref="FinalizarAsync"/> —ese pide <c>contrato.estado</c> y un trabajo
    /// no tiene sesión— y no por reimplementar el flujo: la tabla de estados es una sola y dos copias
    /// de ella divergen sin que nadie se entere.
    /// </para>
    /// <para>
    /// <paramref name="usuario"/> es nulo solo cuando quien cambia el estado no es una persona: el
    /// trabajo de las 07:00. <c>contrato.estado_actual_por_id</c> queda entonces nulo, que es lo que
    /// significa "lo pasó el sistema", y la fila de auditoría lleva <c>origen = "job"</c> para decirlo
    /// igual desde el historial.
    /// </para>
    /// </summary>
    internal static void CambiarEstado(
        Contrato contrato,
        EstadoContrato destino,
        Guid? usuario,
        string? comentario)
    {
        if (!Transiciones[contrato.Estado].Contains(destino))
        {
            throw new InvalidOperationException(
                $"Un contrato en estado {Texto(contrato.Estado)} no puede pasar a {Texto(destino)}.");
        }

        contrato.Estado = destino;
        contrato.EstadoActualDesde = TiempoNegocio.Hoy();
        contrato.EstadoActualPorId = usuario;
        contrato.EstadoActualComentario = Limpiar(comentario);
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string Importe(decimal importe) => importe.ToString("0.00");

    private async Task<Contrato> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Contrato
            .Include(c => c.Cliente)
            .Include(c => c.Unidad)
            .Include(c => c.Facturadora)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe el contrato '{id:D}'.");

    private static ContratoResumen AResumen(Contrato contrato) =>
        new(
            contrato.Id,
            contrato.ClienteId,
            contrato.Cliente?.Nombre,
            contrato.UnidadId,
            contrato.Unidad?.Nombre,
            contrato.FacturadoraId,
            contrato.FechaInicio,
            contrato.FechaFinVigente,
            contrato.Renta,
            contrato.Moneda,
            contrato.Estado,
            contrato.EstadoActualDesde,
            contrato.FechaBajaProgramada,
            contrato.CreatedAt);

    private static object DatosAuditoria(Contrato contrato) => new
    {
        contrato.ClienteId,
        contrato.UnidadId,
        contrato.FacturadoraId,
        contrato.FechaInicio,
        contrato.Renta,
        Moneda = contrato.Moneda.ToString(),
        contrato.DiaPago,
        contrato.Fianza,
        Regla = contrato.ReglaRenovacion.ToString(),
        contrato.RenovacionMeses,
        contrato.PlantillaContratoId,
        Estado = contrato.Estado.ToString(),
        contrato.FechaBajaProgramada,
    };

    private static ContratoVersionRespuesta AVersion(ContratoVersion version) =>
        new(
            version.Numero,
            version.Cambio,
            version.RentaAnterior,
            version.RentaNueva,
            version.Moneda,
            version.Alcance,
            version.CreatedAt);

    private static SolicitudAprobacionRespuesta ASolicitud(SolicitudAprobacion solicitud) =>
        new(
            solicitud.Id,
            solicitud.Resultado,
            solicitud.HuellaContenido,
            solicitud.SolicitadoAt,
            solicitud.ResueltoPorId,
            solicitud.ResueltoAt,
            solicitud.Comentario,
            solicitud.MotivoCaducidad);

    /// <summary>El nombre del estado para los mensajes de la API, en el idioma de quien la lee.</summary>
    private static string Texto(EstadoContrato estado) => estado switch
    {
        EstadoContrato.Borrador => "borrador",
        EstadoContrato.PendienteAprobacion => "pendiente de aprobación",
        EstadoContrato.Aprobado => "aprobado",
        EstadoContrato.Activo => "activo",
        EstadoContrato.BajaProgramada => "con baja programada",
        EstadoContrato.Finalizado => "finalizado",
        EstadoContrato.Liquidado => "liquidado",
        _ => estado.ToString(),
    };
}