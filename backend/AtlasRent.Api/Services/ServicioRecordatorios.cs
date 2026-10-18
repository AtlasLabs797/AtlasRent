using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Los avisos de pago de §3.14 (D12): quién está en atraso, desde cuándo y qué se le dice.
/// <para>
/// <b>El trabajo escribe la fila y el aviso va en la fila.</b> Correr el trabajo dos veces el mismo día
/// no manda dos correos porque el segundo intento choca con el único de
/// <c>(cliente_id, unidad_id, fecha)</c> de <see cref="Recordatorio"/>, que es la fila del aviso, no
/// el correo. Por eso esa tabla existe y no es <c>envio</c>: un recordatorio no es un documento, y su
/// clave de deduplicación no es la de un documento.
/// </para>
/// <para>
/// <b>El saldo se vuelve a mirar cuando se manda, no solo cuando se prepara.</b> Una factura vencida
/// que alguien paga entre el resumen y el clic no puede recibir un recordatorio: se comprueba en el
/// camino que confirma el envío —el manual y también el automático— y si ya está pagada el aviso
/// queda <c>Omitido</c> con su motivo. Comprobarlo solo al preparar deja una ventana en la que un
/// cliente al que acaban de cobrarle recibe un correo pidiéndole el mismo dinero.
/// </para>
/// <para>
/// <b>La pausa del cliente manda sobre todo lo demás</b>, incluido el envío manual uno a uno: quien la
/// activa está diciendo que a ese cliente no se le avisa, y un clic en «enviar» no es una excepción.
/// </para>
/// <para>
/// <b>El PDF se compone una vez.</b> Lo compone el camino que deja la fila <c>Pendiente</c> y queda
/// en <c>envio.pdf_copia</c>; mandar es mandar esa copia, como en F4-03. Si se compusiera al enviar,
/// dos generaciones del mismo estado de cuenta —la que se revisó y la que sale— podrían diferir, y
/// entonces la copia que se le manda al cliente no sería la que se miró.
/// </para>
/// </summary>
public sealed class ServicioRecordatorios(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCuentas cuentas,
    ServicioEnvios envios,
    IPolicyService politica,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>Origen de la auditoría del trabajo: no hay ninguna petición detrás (docs/DOC_TECNICA.md §8).</summary>
    private const string Origen = "job";

    /// <summary>IP que se audita: un trabajo no tiene cliente, y la que corresponde es la del servidor.</summary>
    private const string IpDelTrabajo = "127.0.0.1";

    private const string AccionPreparar = "recordatorio.preparado";

    private const string AccionEnviar = "recordatorio.enviado";

    private const string AccionIntentoFallido = "recordatorio.envio_fallido";

    private const string AccionOmitir = "recordatorio.omitido";

    /// <summary>Tipo de los avisos de §3.16 que levanta este trabajo.</summary>
    public const string TipoAlerta = "recordatorio";

    /// <summary>
    /// Clave del aviso de que no se puede decidir a quién avisar. Sin fecha a propósito: no es «hoy no
    /// se avisó», es «nadie ha dicho cada cuánto se avisa», que sigue siendo verdad mañana igual que
    /// hoy y no debería llenar la bandeja.
    /// </summary>
    public const string ClaveSinPlazos = "recordatorio:plazos-sin-configurar";

    /// <summary>Clave del aviso de que el modo está activo y no hay quién firme los avisos.</summary>
    public const string ClaveSinFirmante = "recordatorio:sin-usuario-que-firme";

    private const string DetalleSinPlazos =
        "El trabajo de recordatorios no ha avisado a nadie porque los plazos no están configurados. "
        + "En Configuración → Ajustes hay que escribir cuántos días desde el vencimiento sale el primer "
        + "aviso (dias_primer_recordatorio) y cada cuántos días se repite mientras haya saldo "
        + "(dias_entre_recordatorios). Mientras estén vacíos no sale ningún recordatorio, porque un "
        + "aviso con plazos sin decidir es ruido para el cliente (criterio de BUG-007).";

    // ---------------------------------------------------------------- el trabajo diario

    /// <summary>Lo que hizo el trabajo de hoy, para el log y para las pruebas.</summary>
    public sealed record Resumen(
        DateOnly Fecha,
        ModoRecordatorios Modo,
        int Candidatos,
        int Preparados,
        int Enviados,
        int Omitidos,
        int Fallidos);

    /// <summary>
    /// El barrido del día (§3.14). Modo <c>Apagado</c>: no hace nada. <c>Manual</c>: deja las filas
    /// <c>Pendiente</c> y no manda nada. <c>Automatico</c>: prepara y manda.
    /// <para>
    /// <b>Una transacción por cliente y unidad</b>, y que un fallo de una no tire las demás, como en
    /// <see cref="ServicioRenovaciones"/> y <see cref="ServicioFines"/>: veinte clientes a los que
    /// avisar y uno sin casilla de correo no pueden convertir los otros diecinueve avisos en nada.
    /// </para>
    /// </summary>
    public async Task<Resumen> EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());
        var ajustes = await AjustesAsync(ct);

        if (ajustes is null || ajustes.ModoRecordatorios == ModoRecordatorios.Apagado)
        {
            return new Resumen(hoy, ModoRecordatorios.Apagado, 0, 0, 0, 0, 0);
        }

        var modo = ajustes.ModoRecordatorios;

        // Los plazos a nula son una decisión, no un olvido: se puede querer «de momento que no avise
        // nadie». Lo que no puede ser es avisar con plazos inventados en el código, así que el trabajo
        // no dice nada y deja una alerta que diga qué escribir.
        if (ajustes.DiasPrimerRecordatorio is not { } primero || ajustes.DiasEntreRecordatorios is not { } entre)
        {
            await alertas.RegistrarAsync(TipoAlerta, ClaveSinPlazos, DetalleSinPlazos, ct);

            return new Resumen(hoy, modo, 0, 0, 0, 0, 0);
        }

        // Ya hay plazos, así que el aviso anterior —si lo hubo— deja de ser cierto y se cierra solo.
        await alertas.ResolverAsync(ClaveSinPlazos, ct);

        // El aviso sale firmado por alguien, y el permiso del envío y el del PDF se le comprueban a
        // ese alguien y no al trabajo (docs/DOC_TECNICA.md §6.7.1). Es el último usuario que puso el
        // modo, que es quien decidió que estos correos salieran solos.
        // TODO(A#): si la casa decide que el aviso lo firma una persona concreta y no «la última que
        // tocó los ajustes», la salida es una columna más en `ajuste_general` con ese identificador,
        // que se escribe al poner el modo en automático.
        if (ajustes.UpdatedBy is not { } firmante)
        {
            await alertas.RegistrarAsync(
                TipoAlerta,
                ClaveSinFirmante,
                "El modo de recordatorios está activo pero no hay ningún usuario que firme los avisos. "
                    + "Guárdalo otra vez desde Configuración → Ajustes para que quede quién lo puso.",
                ct);

            return new Resumen(hoy, modo, 0, 0, 0, 0, 0);
        }

        var grupos = await GruposAsync(hoy, primero, entre, ct);

        var preparados = 0;
        var enviados = 0;
        var omitidos = 0;
        var fallidos = 0;

        foreach (var grupo in grupos)
        {
            try
            {
                // La idempotencia se pregunta antes de escribir (§8.0). Además del único, que es la
                // red, esto evita la transacción que falla por un aviso que ya está: el trabajo diario
                // es acotado y no necesita el coste de writes que se van a rechazar.
                if (await YaAvisadoAsync(grupo, hoy, ct))
                {
                    continue;
                }

                Guid? recordatorioId;
                await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
                {
                    recordatorioId = await PrepararAsync(grupo, hoy, firmante, ct);

                    await transaccion.CommitAsync(ct);
                }

                if (recordatorioId is null)
                {
                    // Se escribió el aviso como omitido y con su motivo: no es un fallo, es la
                    // respuesta correcta.
                    omitidos++;
                    continue;
                }

                preparados++;

                if (modo == ModoRecordatorios.Automatico)
                {
                    enviados += await ConfirmarDelTrabajoAsync(recordatorioId.Value, firmante, ct);
                }
            }
            catch (DbUpdateException e) when (EsUnico(e))
            {
                // Choca el único de (cliente, unidad, fecha): otro intento del mismo día ya lo
                // escribió. No es un fallo, es la idempotencia funcionando (§8.0).
                //
                // Y el contexto se vacía, porque EF deja en `Added` la fila que no pudo guardar y el
                // siguiente `SaveChanges` del mismo barrido la volvería a intentar —y a fallar— una y
                // otra vez, se llevara por delante a los clientes que sí tobcan avisar.
                contexto.ChangeTracker.Clear();

                Log.Debug(
                    "Recordatorio de {ClienteId}/{UnidadId} ya escrito para {Fecha}: {Motivo}",
                    grupo.ClienteId,
                    grupo.UnidadId,
                    hoy,
                    e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                fallidos++;
                Log.Warning(
                    e,
                    "No se pudo avisar a {ClienteId}/{UnidadId} de {Fecha}",
                    grupo.ClienteId,
                    grupo.UnidadId,
                    hoy);
            }
        }

        Log.Information(
            "Recordatorios de {Fecha}: {Candidatos} grupo(s), {Preparados} preparado(s), "
                + "{Enviados} enviado(s), {Omitidos} omitido(s), {Fallidos} con fallo",
            hoy, grupos.Count, preparados, enviados, omitidos, fallidos);

        return new Resumen(hoy, modo, grupos.Count, preparados, enviados, omitidos, fallidos);
    }

    /// <summary>
    /// Si a este cliente y esta unidad ya se le escribió hoy. Es la misma terna del único de
    /// <c>recordatorio</c>, preguntada antes de escribir: correr el trabajo dos veces no puede
    /// producir dos avisos, y la pregunta evita además la transacción que el único rechazaría.
    /// </summary>
    private async Task<bool> YaAvisadoAsync(Grupo grupo, DateOnly hoy, CancellationToken ct) =>
        await contexto.Recordatorio.AsNoTracking()
            .AnyAsync(
                r => r.ClienteId == grupo.ClienteId
                    && r.Fecha == hoy
                    && (!grupo.UnidadId.HasValue || r.UnidadId == grupo.UnidadId),
                ct);

    // ---------------------------------------------------------------- preparación

    /// <summary>
    /// Escribe la fila <c>recordatorio</c> y el envío <c>Pendiente</c> de un cliente y una unidad, y
    /// devuelve el identificador del aviso; devuelve <c>null</c> si quedó <c>Omitido</c>.
    /// La transacción es la de quien llama.
    /// <para>
    /// El saldo se vuelve a mirar aquí y no se da por bueno lo que dio el barrido: entre que se
    /// eligió el cliente y que se escribió la fila alguien pudo pagar. Si ya no hay nada pendiente no
    /// se prepara nada y se escribe el aviso <c>Omitido</c> con su motivo.
    /// </para>
    /// </summary>
    private async Task<Guid?> PrepararAsync(Grupo grupo, DateOnly hoy, Guid firmante, CancellationToken ct)
    {
        var cliente = await contexto.Cliente
            .Include(c => c.Contactos)
            .FirstOrDefaultAsync(c => c.Id == grupo.ClienteId, ct)
            ?? throw new InvalidOperationException($"El cliente '{grupo.ClienteId:D}' ya no existe.");

        if (cliente.RecordatoriosPausados)
        {
            await OmitirAsync(grupo, hoy, "El cliente tiene los recordatorios pausados.", ct);
            return null;
        }

        var pendiente = await PendienteAsync(grupo, hoy, ct);

        if (pendiente.Count == 0)
        {
            await OmitirAsync(grupo, hoy, "Ya no tiene saldo pendiente que recordar.", ct);
            return null;
        }

        var destinatario = DestinatarioEnvio.Elegir(cliente);

        if (destinatario.Motivo != MotivoExclusionEnvio.Ninguno)
        {
            await OmitirAsync(grupo, hoy, $"No hay a quién mandarlo: {destinatario.Detalle}", ct);
            return null;
        }

        var unidad = grupo.UnidadId is { } unidadId
            ? await contexto.Unidad.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unidadId, ct)
            : null;

        var plantilla = await envios.PlantillaAsync(TipoMensajeEnvio.Recordatorio, "es", ct);
        var (asunto, cuerpo) = VariablesMensaje
            .DeRecordatorio(
                cliente.Nombre,
                TextoDelPendiente(pendiente),
                FormatoDocumentoPdf.Fecha(grupo.Vencimiento),
                unidad?.Nombre)
            .Sustituir(plantilla);

        // El PDF se compone aquí y no al mandar: es el camino que deja la fila Pendiente, y desde
        // aquí en adelante lo que sale es exactamente lo que se compuso (mismo criterio que F4-03 con
        // el envío masivo).
        var pdf = await cuentas.PdfAsync(cliente.Id, hoy.Year, firmante, ct);

        var envio = new Envio
        {
            Id = Guid.NewGuid(),
            TipoDocumento = TipoDocumentoEnvio.EstadoCuenta,
            DocumentoId = cliente.Id,
            DocumentoAnio = hoy.Year,
            ClienteId = cliente.Id,
            DestinatarioEmail = destinatario.Email!,
            DestinatarioNombre = destinatario.Nombre,
            Asunto = asunto,
            Cuerpo = cuerpo,
            Idioma = plantilla.Idioma,
            Estado = EstadoEnvio.Pendiente,
            PdfCopia = pdf,
            NombreArchivo = $"estado-de-cuenta-{hoy.Year}-{cliente.Id:D}.pdf",
            Intentos = 0,
        };

        var recordatorio = new Recordatorio
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            UnidadId = grupo.UnidadId,
            Fecha = hoy,
            Estado = EstadoRecordatorio.Pendiente,
            EnvioId = envio.Id,
        };

        contexto.Envio.Add(envio);
        contexto.Recordatorio.Add(recordatorio);

        await GuardarDelTrabajoAsync(recordatorio, envio.Id, AccionPreparar, ct);

        return recordatorio.Id;
    }

    /// <summary>
    /// Escribe el aviso como <c>Omitido</c> con su motivo. Es la fila que explica en la pantalla de
    /// revisión por qué a ese cliente no se le escribió hoy, en vez de un hueco sin explicación.
    /// </summary>
    private async Task OmitirAsync(Grupo grupo, DateOnly fecha, string motivo, CancellationToken ct)
    {
        var recordatorio = new Recordatorio
        {
            Id = Guid.NewGuid(),
            ClienteId = grupo.ClienteId,
            UnidadId = grupo.UnidadId,
            Fecha = fecha,
            Estado = EstadoRecordatorio.Omitido,
            MotivoOmision = motivo,
        };

        contexto.Recordatorio.Add(recordatorio);

        await GuardarDelTrabajoAsync(recordatorio, null, AccionOmitir, ct);
    }

    // ---------------------------------------------------------------- envío

    /// <summary>
    /// Confirma el envío de un aviso desde la pantalla: <c>POST /api/v1/recordatorios/{id}/enviar</c>.
    /// <para>
    /// Las dos comprobaciones van aquí y no al preparar: la pausa del cliente, que sigue valiendo
    /// aunque alguien pulse el botón, y el saldo. Las dos devuelven el aviso <c>Omitido</c> con su
    /// motivo, que no es un fallo: lo que se quería —no molestar a quien ya pagó— se cumplió.
    /// </para>
    /// </summary>
    public async Task<RecordatorioFila> EnviarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(id, usuario, ct);

        var recordatorio = await CargarAsync(id, ct);

        if (recordatorio.Estado != EstadoRecordatorio.Pendiente)
        {
            throw new InvalidOperationException(
                $"El recordatorio '{id:D}' no está pendiente de envío: está en {recordatorio.Estado}.");
        }

        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        if (recordatorio.Cliente?.RecordatoriosPausados == true)
        {
            return await OmitirAsync(
                recordatorio, "El cliente tiene los recordatorios pausados.", usuario, ip, mutacion, ct);
        }

        if (!await TieneAlgoPendienteAsync(recordatorio.ClienteId, recordatorio.UnidadId, hoy, ct))
        {
            return await OmitirAsync(
                recordatorio,
                "Ya no tiene saldo pendiente que recordar: se pagó antes de confirmar el envío.",
                usuario,
                ip,
                mutacion,
                ct);
        }

        if (recordatorio.EnvioId is not { } envioId)
        {
            throw new InvalidOperationException($"El recordatorio '{id:D}' no tiene envío al que mandar.");
        }

        var estado = await envios.EnviarPendienteAsync(envioId, usuario, ip, ct);

        // Si el envío falla, el aviso se queda pendiente: lo que se puede reintentar es el envío, y su
        // error está en la fila de `envio` con su propia alerta de §3.16.
        recordatorio.Estado = estado == EstadoEnvio.Enviado
            ? EstadoRecordatorio.Enviado
            : EstadoRecordatorio.Pendiente;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Recordatorio, recordatorio,
            esAlta: false,
            accion: estado == EstadoEnvio.Enviado ? AccionEnviar : AccionIntentoFallido,
            entidad: "recordatorio",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    private async Task<RecordatorioFila> OmitirAsync(
        Recordatorio recordatorio,
        string motivo,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        recordatorio.Estado = EstadoRecordatorio.Omitido;
        recordatorio.MotivoOmision = motivo;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Recordatorio, recordatorio,
            esAlta: false,
            accion: AccionOmitir,
            entidad: "recordatorio",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// El camino automático: el mismo envío con el mismo aviso de antes, firmado por el usuario que
    /// puso el modo. Devuelve uno si el aviso salió y cero si no.
    /// <para>
    /// Se comprueba el saldo otra vez aquí, igual que en el manual. Entre que se escribió la fila y
    /// que se manda hay una llamada a un servidor de correo de por medio, y en ese hueco se cobra.
    /// </para>
    /// </summary>
    private async Task<int> ConfirmarDelTrabajoAsync(Guid recordatorioId, Guid firmante, CancellationToken ct)
    {
        var recordatorio = await CargarAsync(recordatorioId, ct);

        if (recordatorio.Estado != EstadoRecordatorio.Pendiente || recordatorio.EnvioId is not { } envioId)
        {
            return 0;
        }

        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        if (recordatorio.Cliente?.RecordatoriosPausados == true)
        {
            return await OmitirDelTrabajoAsync(
                recordatorio, "El cliente tiene los recordatorios pausados.", ct);
        }

        if (!await TieneAlgoPendienteAsync(recordatorio.ClienteId, recordatorio.UnidadId, hoy, ct))
        {
            return await OmitirDelTrabajoAsync(
                recordatorio,
                "Ya no tiene saldo pendiente que recordar: se pagó antes de confirmar el envío.",
                ct);
        }

        var estado = await envios.EnviarPendienteAsync(envioId, firmante, IpDelTrabajo, ct);

        if (estado != EstadoEnvio.Enviado)
        {
            return 0;
        }

        recordatorio.Estado = EstadoRecordatorio.Enviado;

        await GuardarDelTrabajoAsync(recordatorio, envioId, AccionEnviar, ct);

        return 1;
    }

    private async Task<int> OmitirDelTrabajoAsync(Recordatorio recordatorio, string motivo, CancellationToken ct)
    {
        recordatorio.Estado = EstadoRecordatorio.Omitido;
        recordatorio.MotivoOmision = motivo;

        await GuardarDelTrabajoAsync(recordatorio, recordatorio.EnvioId, AccionOmitir, ct);

        return 0;
    }

    /// <summary>
    /// Si a este cliente y esta unidad le queda algo vencido por pagar ahora mismo. La misma pregunta
    /// que hace el barrido, hecha en el momento de mandar.
    /// </summary>
    private async Task<bool> TieneAlgoPendienteAsync(
        Guid clienteId,
        Guid? unidadId,
        DateOnly hoy,
        CancellationToken ct) =>
        await contexto.Factura.AsNoTracking()
            .Where(f => f.ClienteId == clienteId
                && (!unidadId.HasValue || f.UnidadId == unidadId)
                && f.Estado == EstadoFactura.Emitida
                && f.FechaVencimiento < hoy)
            .Where(PendienteDeFactura.ConPendiente(contexto))
            .AnyAsync(ct);

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Los avisos, filtrables por fecha, por cliente, por unidad y por estado: lo que hace falta
    /// para responder en la pantalla de revisión «¿qué se mandó a quién y a cuál no?».
    /// </summary>
    public async Task<Pagina<RecordatorioFila>> ListarAsync(
        ConsultaLista paginacion,
        DateOnly? fecha,
        Guid? clienteId,
        Guid? unidadId,
        EstadoRecordatorio? estado,
        CancellationToken ct)
    {
        var consulta = contexto.Recordatorio.AsNoTracking()
            .Include(r => r.Cliente)
            .Include(r => r.Unidad)
            .Include(r => r.Envio)
            .Where(r => !fecha.HasValue || r.Fecha == fecha)
            .Where(r => !clienteId.HasValue || r.ClienteId == clienteId)
            .Where(r => !unidadId.HasValue || r.UnidadId == unidadId)
            .Where(r => !estado.HasValue || r.Estado == estado);

        return await ConsultaPaginada.PaginarAsync(consulta, Ordenes, "fecha", paginacion, ARespuesta, ct);
    }

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<Recordatorio>, bool, IOrderedQueryable<Recordatorio>>> Ordenes =
        new Dictionary<string, Func<IQueryable<Recordatorio>, bool, IOrderedQueryable<Recordatorio>>>
        {
            ["fecha"] = (q, desc) => desc
                ? q.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id)
                : q.OrderBy(r => r.Fecha).ThenBy(r => r.Id),
            ["estado"] = (q, desc) => desc
                ? q.OrderByDescending(r => r.Estado).ThenBy(r => r.Id)
                : q.OrderBy(r => r.Estado).ThenBy(r => r.Id),
            ["createdAt"] = (q, desc) => desc
                ? q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
                : q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
        };

    /// <summary>Un aviso suelto, con el envío al que dio lugar.</summary>
    public async Task<RecordatorioFila> LeerAsync(Guid id, CancellationToken ct) =>
        ARespuesta(await CargarAsync(id, ct));

    private async Task<Recordatorio> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Recordatorio
            .Include(r => r.Cliente)
            .Include(r => r.Unidad)
            .Include(r => r.Envio)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new KeyNotFoundException($"No hay ningún recordatorio con identificador '{id:D}'.");

    // ---------------------------------------------------------------- cifras y candidatos

    /// <summary>
    /// Los clientes y las unidades a los que hay que avisar hoy, cada uno con el saldo pendiente por
    /// moneda y el vencimiento más antiguo que lo produce.
    /// <para>
    /// La cadencia es la de §3.14: el primer aviso a los <paramref name="primero"/> días del
    /// vencimiento y a partir de ahí cada <paramref name="entre"/>. Con los valores sembrados salen a
    /// los 5, 20 y 35 días, y no a los 6 ni a los 21: un aviso diario en vez de uno cada quince no es
    /// «recordar más», es el correo que enseña a ignorar la bandeja.
    /// </para>
    /// <para>
    /// El filtro de la cadencia se hace en memoria y no en SQL porque es aritmética sobre días; lo
    /// que sí se pregunta a la base es lo caro: emitidas, vencidas y con algo pendiente.
    /// </para>
    /// </summary>
    private async Task<List<Grupo>> GruposAsync(DateOnly hoy, int primero, int entre, CancellationToken ct)
    {
        var limite = hoy.AddDays(-primero);

        var vencidas = await contexto.Factura.AsNoTracking()
            .Where(f => f.Estado == EstadoFactura.Emitida && f.FechaVencimiento <= limite)
            .Where(PendienteDeFactura.ConPendiente(contexto))
            .Where(f => f.Cliente != null && !f.Cliente.RecordatoriosPausados)
            .Select(f => new Vencida(f.Id, f.ClienteId, f.UnidadId, f.Moneda, f.FechaVencimiento))
            .ToListAsync(ct);

        var grupos = new List<Grupo>();

        foreach (var grupo in vencidas.GroupBy(f => new { f.ClienteId, f.UnidadId }))
        {
            var enPlazo = grupo
                .Where(f => (hoy.DayNumber - f.Vencimiento.DayNumber - primero) % entre == 0)
                .ToList();

            if (enPlazo.Count == 0)
            {
                continue;
            }

            grupos.Add(new Grupo(
                grupo.Key.ClienteId,
                grupo.Key.UnidadId,
                enPlazo.Min(f => f.Vencimiento),
                [.. enPlazo.Select(f => f.Id)]));
        }

        return grupos;
    }

    /// <summary>
    /// El pendiente del grupo en el momento de mirarlo, por moneda, o vacío si ya no debe nada.
    /// <para>
    /// <b>Por moneda y no en una sola cifra</b>: un cliente y una unidad pueden tener facturas en USD
    /// y en DOP, y sumarlas sin convertir sería inventar un tipo de cambio (§3.12). El texto dice las
    /// dos, cada una con la suya.
    /// </para>
    /// <para>
    /// La cuenta es <see cref="CalculoCobro"/>, la misma que leen la vista de cuentas y el aging: la
    /// suma se trae de la base —que es lo que se traduce— y el resto se calcula en C#, porque para un
    /// grupo de facturas concreto no hace falta ni conviene una subconsulta por cada una.
    /// </para>
    /// </summary>
    private async Task<List<ImporteMoneda>> PendienteAsync(Grupo grupo, DateOnly hoy, CancellationToken ct)
    {
        var ids = grupo.Facturas;

        var filas = await contexto.Factura.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new Cobranza(
                f.Moneda,
                f.Total,
                f.Retencion,
                f.FechaVencimiento,
                f.Estado,
                contexto.CobroAsignacion
                    .Where(a => a.FacturaId == f.Id && a.Vigente && a.Cobro!.Estado == EstadoCobro.Registrado)
                    .Select(a => (decimal?)a.Importe)
                    .Sum() ?? 0m,
                contexto.AjusteDiferenciaCambio
                    .Where(a => a.FacturaId == f.Id && a.AnuladoAt == null)
                    .Select(a => (decimal?)a.Importe)
                    .Sum() ?? 0m))
            .ToListAsync(ct);

        return filas
            .Select(f => new ImporteMoneda(
                f.Moneda,
                CalculoCobro.Calcular(f.Total, f.Retencion, f.Cobrado, f.Ajustado, f.Vencimiento, hoy, f.Estado).Pendiente))
            .Where(i => i.Importe > 0m)
            .GroupBy(i => i.Moneda)
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .Select(g => new ImporteMoneda(g.Key, g.Sum(i => i.Importe)))
            .ToList();
    }

    // ---------------------------------------------------------------- interno

    /// <summary>
    /// Los ajustes de la casa, enteros y sin seguimiento: el trabajo no los cambia y no quiere que un
    /// guardado suyo los vuelva a escribir por accidente. <c>null</c> si la fila no existe todavía.
    /// </summary>
    private async Task<AjusteGeneral?> AjustesAsync(CancellationToken ct) =>
        await contexto.AjusteGeneral.AsNoTracking().FirstOrDefaultAsync(ct);

    /// <summary>
    /// Guarda la fila del aviso y su auditoría en la transacción de quien llama, con la que se abre
    /// una por cliente y unidad (§8.0: la auditoría va dentro, nunca después).
    /// </summary>
    private async Task GuardarDelTrabajoAsync(Recordatorio recordatorio, Guid? envioId, string accion, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();

        recordatorio.CreatedAt = ahora;
        recordatorio.CreatedBy = null;

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = accion,
            Ip = IpDelTrabajo,
            UsuarioId = null,
            Entidad = "recordatorio",
            EntidadId = recordatorio.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new
            {
                recordatorio.Fecha,
                recordatorio.ClienteId,
                recordatorio.UnidadId,
                recordatorio.Estado,
                EnvioId = envioId,
                recordatorio.MotivoOmision,
            }),
            Resultado = "permitido",
            Origen = Origen,
        }, ct);
    }

    /// <summary>El texto del pendiente: una cantidad por moneda, cada una con la suya.</summary>
    private static string TextoDelPendiente(List<ImporteMoneda> pendiente) =>
        string.Join(" y ", pendiente.Select(p => FormatoDocumentoPdf.Importe(p.Importe, p.Moneda)));

    /// <summary>
    /// Si el identificador es el del único de <c>(cliente, unidad, fecha)</c>: la señal de que otro
    /// intento del mismo día ya escribió la fila.
    /// </summary>
    private static bool EsUnico(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Segunda puerta del permiso: la protege el endpoint, y el trabajo entra por debajo.</summary>
    private async Task ExigirAsync(Guid id, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.MensajeriaMasivo, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(Acciones.MensajeriaMasivo, $"recordatorio/{id:D}", ct);

        throw new PermisoDenegadoException(Acciones.MensajeriaMasivo, $"recordatorio/{id:D}");
    }

    private static RecordatorioFila ARespuesta(Recordatorio r) =>
        new(
            r.Id,
            r.ClienteId,
            r.Cliente?.Nombre ?? string.Empty,
            r.UnidadId,
            r.Unidad?.Nombre,
            r.Fecha,
            r.Estado,
            r.EnvioId,
            r.Envio?.DestinatarioEmail,
            r.MotivoOmision,
            r.CreatedAt);

    /// <summary>Lo que va al campo <c>datos</c> de la auditoría: ni el PDF ni el cuerpo del correo.</summary>
    private static object DatosAuditoria(Recordatorio r) =>
        new { r.ClienteId, r.UnidadId, r.Fecha, r.Estado, r.EnvioId, r.MotivoOmision };

    /// <summary>Una factura vencida con saldo, tal como la trae el barrido.</summary>
    private sealed record Vencida(Guid Id, Guid ClienteId, Guid? UnidadId, Moneda Moneda, DateOnly Vencimiento);

    /// <summary>Lo que hace falta para aplicar <see cref="CalculoCobro"/> a una factura.</summary>
    private sealed record Cobranza(
        Moneda Moneda,
        decimal Total,
        decimal Retencion,
        DateOnly Vencimiento,
        EstadoFactura Estado,
        decimal Cobrado,
        decimal Ajustado);

    private sealed record ImporteMoneda(Moneda Moneda, decimal Importe);

    /// <summary>
    /// Un cliente y una unidad a los que hay que avisar hoy. Se agrupan en memoria porque el filtro de
    /// la cadencia es aritmética sobre días y no se escribe en SQL.
    /// </summary>
    private sealed record Grupo(Guid ClienteId, Guid? UnidadId, DateOnly Vencimiento, List<Guid> Facturas);
}