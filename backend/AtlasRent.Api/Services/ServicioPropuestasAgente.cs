using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// F6-03: lo que el asistente propone y un humano aprueba. Es el camino de escritura que faltaba,
/// y por eso es <b>el mismo</b> que el de los contratos: <see cref="SolicitudAprobacion"/>, huella,
/// caducidad, solicitante y resolutor.
/// <para>
/// <b>La IA no ejecuta nada.</b> Aquí no llega ni una palabra del modelo: la propuesta se arma a
/// partir del aviso que el trabajo de recordatorios ya dejó preparado y del saldo que ya estaba
/// calculado, y aprobar ejecuta <see cref="ServicioRecordatorios.EnviarAsync"/>, que es la misma
/// llamada que hace el botón de la pantalla de revisión. El texto del modelo se guarda como
/// <see cref="SolicitudAprobacion.Comentario"/>, que es donde sirve: explicar al aprobador.
/// </para>
/// <para>
/// <b>Aprobar no da permiso.</b> Quien aprueba tiene que poder hacer la acción que aprueba —para el
/// recordatorio, <c>mensajeria.masivo</c>— y no vale con haber aprobado contratos antes. La
/// comprobación está en el servicio y no solo en el atributo del endpoint, y la denegación se
/// audita.
/// </para>
/// <para>
/// <b>La caducidad es la de siempre.</b> Al proponer se guarda la huella del aviso y de su saldo; al
/// aprobar se vuelve a calcular y, si ya no coincide, la propuesta queda <c>Caducada</c> con su motivo
/// y no se manda nada. El correo que saldría lleva las cifras con las que se compuso, así que
/// aprobar en esas condiciones sería hacer aprobar un texto que ya no se corresponde con la deuda.
/// </para>
/// <para>
/// <b>El nombre del cliente no sale del proceso.</b> En el payload y en la auditoría va
/// <c>[[cliente_1]]</c>, como en las consultas del asistente. La bandeja sí enseña el nombre real
/// porque es una pantalla interna para personas, y lo lee de <c>cliente</c> por identificador.
/// </para>
/// </summary>
public sealed class ServicioPropuestasAgente(
    AtlasRentDbContext contexto,
    ServicioRecordatorios recordatorios,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    TimeProvider reloj)
{
    /// <summary>Único tipo de propuesta de esta fase: mandar el aviso de saldo ya preparado (§3.14).</summary>
    public const string TipoRecordatorioSaldo = "recordatorio_saldo";

    private const string Entidad = "solicitud_aprobacion";

    private const string AccionCrear = "ia.propuesta_creada";

    private const string AccionAprobar = "ia.propuesta_aprobada";

    private const string AccionRechazar = "ia.propuesta_rechazada";

    private const string AccionCaducar = "ia.propuesta_caducada";

    /// <summary>
    /// Por qué no se manda nada y hay que proponer de nuevo. El mismo texto para todo lo que cambió:
    /// <b>dice qué pasó, no por qué</b>. La huella avisa de que el aviso ya no es el mismo, y puede
    /// haber sido por saldo, por el día —el texto dice cuántos días lleva de atraso— o por el estado;
    /// nombrar uno sería afirmar algo que el sistema no sabe.
    /// </summary>
    private const string MotivoCaducidad = "El aviso de este cliente cambió después de proponerla: lo que se iba a recordar ya no es lo que dice el aviso, así que hay que proponerlo de nuevo.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, Func<IQueryable<SolicitudAprobacion>, bool, IOrderedQueryable<SolicitudAprobacion>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["solicitadoAt"] = (q, desc) => desc
                ? q.OrderByDescending(s => s.SolicitadoAt).ThenByDescending(s => s.Id)
                : q.OrderBy(s => s.SolicitadoAt).ThenBy(s => s.Id),
        };

    /// <summary>
    /// Propone mandar el aviso de saldo de un cliente. <b>Solo escribe la solicitud</b>: no compone
    /// correos, no toca el aviso y no llama al proveedor de correo.
    /// <para>
    /// Exige <c>ia.proponer</c> por dentro además de por el atributo del endpoint, como todos los
    /// servicios: el atributo protege la entrada por HTTP y este método protege la operación. Con un
    /// token de agente, la lista del token se comprueba antes, en el filtro.
    /// </para>
    /// </summary>
    public async Task<PropuestaIaRespuesta> CrearAsync(
        PropuestaIaPeticion peticion,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.IaProponer, "propuesta-del-agente", usuario, ct);

        if (!string.Equals(peticion.Tipo, TipoRecordatorioSaldo, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"No se puede proponer '{peticion.Tipo}'. El único tipo de propuesta es '{TipoRecordatorioSaldo}'.");
        }

        var nombre = await NombreDelClienteAsync(peticion.ClienteId, ct)
            ?? throw new KeyNotFoundException($"No hay ningún cliente con identificador '{peticion.ClienteId:D}'.");

        var aviso = await AvisoPendienteAsync(peticion.ClienteId, ct)
            ?? throw new InvalidOperationException(
                "Este cliente no tiene ningún aviso de saldo preparado y pendiente de envío. Sin aviso "
                + "preparado no hay nada que proponer: el trabajo de recordatorios deja la fila "
                + "`Pendiente` en modo manual.");

        var estado = await recordatorios.HuellaDeAvisoAsync(aviso.Id, ct)
            ?? throw new InvalidOperationException(
                $"El aviso '{aviso.Id:D}' ya no existe y esta propuesta no se puede dejar a medias.");

        // Un solo mapa para el nombre y para la justificación: la justificación es texto libre y la
        // escribe quien propone, así que sin esto un «llama a Ana» dejaría el nombre real en el
        // payload y en lo que lee el aprobador. El nombre real sale de la tabla `cliente` al armar la
        // bandeja, que es donde tiene que estar.
        var mapa = new MapaSeudonimos();
        var seudonimo = SeudonimoDe(mapa, nombre);
        var justificacion = mapa.AnonimizarTexto(peticion.Justificacion ?? string.Empty);

        var solicitud = new SolicitudAprobacion
        {
            Tipo = TipoSolicitud.AccionAgente,
            ContratoId = null,
            // Explícito y no por omisión: el enum empieza en 1 y una propuesta guardada con 0 sería
            // invisible para la bandeja y para la caducidad.
            Resultado = ResultadoSolicitud.Pendiente,
            HuellaContenido = estado.Huella,
            SolicitadoPorId = usuario,
            SolicitadoAt = reloj.GetUtcNow(),
            Comentario = justificacion,
            PayloadJson = Serializar(seudonimo, peticion.ClienteId, aviso.Id, estado.Pendiente, justificacion),
        };

        var solicitante = await NombresDeUsuariosAsync([(Guid?)usuario], ct);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.SolicitudAprobacion, solicitud,
            esAlta: true,
            accion: AccionCrear,
            entidad: Entidad,
            usuario,
            ip,
            mutacion: null,
            proyectar: s => Respuesta(s, nombre, Nombre(solicitante, usuario), null),
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Las propuestas que esperan decisión. Es la parte de la bandeja que corresponde al asistente, y
    /// sale solo si quien pregunta puede proponer: una lista vacía para quien no puede aprobar es
    /// «no hay nada que hacer», y eso es un error de permisos mal escondido.
    /// </summary>
    public async Task<Pagina<PropuestaIaRespuesta>> ListarAsync(
        ConsultaLista paginacion,
        Guid usuario,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.IaProponer, "bandeja-propuestas-del-agente", usuario, ct);

        var consulta = contexto.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.Tipo == TipoSolicitud.AccionAgente && s.Resultado == ResultadoSolicitud.Pendiente);

        // Los nombres salen de `cliente` y `usuario`, no del payload: el payload lleva el seudónimo a
        // propósito y esta pantalla es interna, así que junta por identificador igual que la bandeja
        // de contratos con la renta vigente.
        var pendientes = await consulta
            .Select(s => new { s.Id, s.PayloadJson, s.SolicitadoPorId })
            .ToListAsync(ct);

        var payloads = pendientes.ToDictionary(p => p.Id, p => Leer(p.PayloadJson));
        Guid[] ids = [.. payloads.Values.Select(p => p.ClienteId).Distinct()];

        var nombres = await contexto.Cliente.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Nombre })
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);

        var solicitantes = await NombresDeUsuariosAsync(
            [.. pendientes.Select(p => p.SolicitadoPorId)], ct);

        return await ConsultaPaginada.PaginarAsync(
            consulta,
            Ordenes,
            "solicitadoAt",
            paginacion,
            s => Respuesta(s, nombres.GetValueOrDefault(payloads[s.Id].ClienteId) ?? string.Empty,
                Nombre(solicitantes, s.SolicitadoPorId), null),
            ct);
    }

    /// <summary>
    /// Aprueba la propuesta y ejecuta lo que se aprobó: el aviso, con su servicio. Si la huella ya no
    /// cuadra, la propuesta caduca y no se manda nada —no es un error de quien aprueba, es que lo
    /// que iba a aprobar ya no existe— y la respuesta lo dice con el resultado, no con un 409.
    /// </summary>
    public async Task<PropuestaIaRespuesta> AprobarAsync(
        Guid id,
        Guid aprobador,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.MensajeriaMasivo, $"{Entidad}/{id:D}", aprobador, ct);

        var solicitud = await CargarAsync(id, ct);
        ExigirPendiente(solicitud);
        var payload = Leer(solicitud.PayloadJson);

        var estado = await recordatorios.HuellaDeAvisoAsync(payload.RecordatorioId, ct);

        if (estado is null || !string.Equals(estado.Huella, solicitud.HuellaContenido, StringComparison.Ordinal))
        {
            return await ResolverAsync(
                solicitud,
                ResultadoSolicitud.Caducada,
                AccionCaducar,
                MotivoCaducidad,
                aprobador,
                ip,
                mutacion,
                ct);
        }

        // El envío va sin `Idempotency-Key`: la clave de esta petición responde por la propuesta, y la
        // que se guarda es la respuesta de aprobar. El aviso, además, no se puede mandar dos veces
        // porque `EnviarAsync` solo entra si sigue `Pendiente`.
        await recordatorios.EnviarAsync(payload.RecordatorioId, aprobador, ip, mutacion: null, ct);

        solicitud.Resultado = ResultadoSolicitud.Aprobada;
        solicitud.MotivoCaducidad = null;

        return await GuardarResolucionAsync(solicitud, AccionAprobar, aprobador, ip, mutacion, ct);
    }

    /// <summary>
    /// Rechaza la propuesta. No ejecuta nada y deja el comentario, que es lo que después permite
    /// distinguir un rechazo de una caducidad.
    /// </summary>
    public async Task<PropuestaIaRespuesta> RechazarAsync(
        Guid id,
        string comentario,
        Guid rechazante,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.MensajeriaMasivo, $"{Entidad}/{id:D}", rechazante, ct);

        var solicitud = await CargarAsync(id, ct);
        ExigirPendiente(solicitud);

        solicitud.Resultado = ResultadoSolicitud.Rechazada;
        solicitud.Comentario = comentario;

        return await GuardarResolucionAsync(solicitud, AccionRechazar, rechazante, ip, mutacion, ct);
    }

    // ---------------------------------------------------------------- interno

    private async Task<PropuestaIaRespuesta> ResolverAsync(
        SolicitudAprobacion solicitud,
        ResultadoSolicitud resultado,
        string accion,
        string motivo,
        Guid resolutor,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        solicitud.Resultado = resultado;
        solicitud.MotivoCaducidad = motivo;

        return await GuardarResolucionAsync(solicitud, accion, resolutor, ip, mutacion, ct);
    }

    private async Task<PropuestaIaRespuesta> GuardarResolucionAsync(
        SolicitudAprobacion solicitud,
        string accion,
        Guid resolutor,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var payload = Leer(solicitud.PayloadJson);
        var nombre = await NombreDelClienteAsync(payload.ClienteId, ct) ?? string.Empty;
        var solicitantes = await NombresDeUsuariosAsync([solicitud.SolicitadoPorId, resolutor], ct);

        solicitud.ResueltoPorId = resolutor;
        solicitud.ResueltoAt = reloj.GetUtcNow();

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.SolicitudAprobacion, solicitud,
            esAlta: false,
            accion: accion,
            entidad: Entidad,
            resolutor,
            ip,
            mutacion,
            proyectar: s => Respuesta(s, nombre, Nombre(solicitantes, s.SolicitadoPorId),
                Nombre(solicitantes, resolutor)),
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// El aviso preparado que está esperando a que alguien lo mande. Se pregunta con el listado del
    /// propio servicio de recordatorios y no con una consulta nueva: es la misma pregunta que hace la
    /// pantalla de revisión, con el mismo orden.
    /// </summary>
    private async Task<RecordatorioFila?> AvisoPendienteAsync(Guid clienteId, CancellationToken ct)
    {
        var pagina = await recordatorios.ListarAsync(
            new ConsultaLista(1, 1, null, ConsultaLista.OrdenDescendente),
            fecha: null,
            clienteId,
            unidadId: null,
            estado: EstadoRecordatorio.Pendiente,
            ct);

        return pagina.Data.FirstOrDefault();
    }

    private async Task<SolicitudAprobacion> CargarAsync(Guid id, CancellationToken ct)
    {
        var solicitud = await contexto.SolicitudAprobacion.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException($"No hay ninguna solicitud de aprobación con identificador '{id:D}'.");

        if (solicitud.Tipo != TipoSolicitud.AccionAgente)
        {
            throw new InvalidOperationException(
                $"La solicitud '{id:D}' no es una propuesta del asistente: es una aprobación de contrato.");
        }

        return solicitud;
    }

    private static void ExigirPendiente(SolicitudAprobacion solicitud)
    {
        if (solicitud.Resultado == ResultadoSolicitud.Pendiente)
        {
            return;
        }

        throw new InvalidOperationException(
            $"La propuesta ya está en {solicitud.Resultado} y no se puede volver a decidir.");
    }

    /// <summary>Segunda puerta del permiso: el atributo protege la ruta y esto protege la operación.</summary>
    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    private async Task<string?> NombreDelClienteAsync(Guid clienteId, CancellationToken ct) =>
        await contexto.Cliente.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => c.Nombre)
            .SingleOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, string>> NombresDeUsuariosAsync(
        IEnumerable<Guid?> ids,
        CancellationToken ct)
    {
        Guid[] buscables = [.. ids.Where(id => id != null).Select(id => id!.Value).Distinct()];

        return await contexto.Usuario.AsNoTracking()
            .Where(u => buscables.Contains(u.Id))
            .Select(u => new { u.Id, u.Nombre })
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
    }

    /// <summary>Nombre de un usuario o <c>null</c>, sin distinguir «no está» de «no hay».</summary>
    private static string? Nombre(IReadOnlyDictionary<Guid, string> nombres, Guid? usuarioId) =>
        usuarioId is { } id && nombres.TryGetValue(id, out var nombre) ? nombre : null;

    /// <summary>
    /// El seudónimo va con el mismo mecanismo que las consultas del asistente (F6-02): un mapa nuevo
    /// por propuesta, y de un solo cliente sale siempre <c>[[cliente_1]]</c>. Lo que no hace falta es
    /// guardarlo: el mapa vive en memoria y aquí solo interesa el texto.
    /// </summary>
    private static string SeudonimoDe(MapaSeudonimos mapa, string nombre)
    {
        var anonimo = mapa.Anonimizar(JsonSerializer.SerializeToNode(new { cliente = nombre }, Json));

        return JsonDocument.Parse(anonimo).RootElement.GetProperty("cliente").GetString() ?? "[[cliente_1]]";
    }

    private static string Serializar(string seudonimo, Guid clienteId, Guid recordatorioId, string pendiente, string? justificacion) =>
        JsonSerializer.Serialize(new
        {
            tipo = TipoRecordatorioSaldo,
            seudonimo,
            clienteId,
            recordatorioId,
            pendiente,
            justificacion,
        }, Json);

    private static Payload Leer(string? payloadJson) =>
        string.IsNullOrWhiteSpace(payloadJson)
            ? throw new InvalidOperationException("La propuesta no tiene payload: no se sabe qué se propone.")
            : JsonSerializer.Deserialize<Payload>(payloadJson, Json)
                ?? throw new InvalidOperationException("El payload de la propuesta no se puede leer.");

    /// <summary>
    /// Lo que va al <c>datos</c> de la auditoría: identificadores y huella. El nombre del cliente no
    /// sale ni de aquí ni del payload, que ya va seudonimizado.
    /// </summary>
    private static object DatosAuditoria(SolicitudAprobacion solicitud)
    {
        var payload = Leer(solicitud.PayloadJson);

        return new
        {
            solicitud.Tipo,
            payload.ClienteId,
            payload.RecordatorioId,
            solicitud.Resultado,
            solicitud.HuellaContenido,
        };
    }

    private static PropuestaIaRespuesta Respuesta(
        SolicitudAprobacion solicitud,
        string nombreCliente,
        string? nombreSolicitante,
        string? nombreResolutor)
    {
        var payload = Leer(solicitud.PayloadJson);

        return new PropuestaIaRespuesta(
            solicitud.Id,
            TipoRecordatorioSaldo,
            payload.ClienteId,
            nombreCliente,
            payload.RecordatorioId,
            payload.Seudonimo,
            payload.Pendiente,
            solicitud.HuellaContenido,
            solicitud.Resultado,
            solicitud.MotivoCaducidad,
            solicitud.Comentario,
            solicitud.SolicitadoAt,
            nombreSolicitante,
            solicitud.ResueltoAt,
            nombreResolutor);
    }

    /// <summary>Lo que dice el payload. El <c>Seudonimo</c> es lo que se guarda, nunca el nombre.</summary>
    private sealed record Payload(
        Guid ClienteId,
        Guid RecordatorioId,
        string Seudonimo,
        string Pendiente,
        string? Justificacion);
}