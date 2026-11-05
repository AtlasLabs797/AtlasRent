using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// El envío de un documento por correo y la copia de lo que salió (F4-02,
/// docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>Una fila de <c>envio</c> por intento</b>, escrita después de hablar con el servidor de correo:
/// si salió, en <c>Enviado</c>; si no, en <c>Fallido</c> con el motivo. La llamada al servidor va
/// <em>fuera</em> de la transacción a propósito: una transacción abierta mientras se espera a un
/// servidor ajeno mantiene bloqueadas las filas del documento que ya se está mandando. Lo que sí
/// va en la misma transacción es la fila y su auditoría, como en el resto del repo (AGENTS.md §6).
/// </para>
/// <para>
/// <b>Los bytes que se guardan son los que salieron</b>, los mismos que se adjuntaron, no una
/// regeneración. <c>GET /api/v1/envios/{id}/pdf</c> devuelve esa copia y no vuelve a generar el
/// documento: si lo regenerara, dejaría de ser la copia exacta en cuanto el documento cambiara, que
/// es justo lo que la marca «modificado tras envío» tiene que poder afirmar.
/// </para>
/// <para>
/// <b>El servicio comprueba el permiso, no solo el atributo del endpoint.</b> Los trabajos de
/// Hangfire de W4 y W5 entran por aquí y no tienen sesión HTTP, así que el permiso se comprueba con
/// <see cref="IPolicyService"/> contra el usuario que se le pase.
/// </para>
/// <para>
/// <b>Y el permiso depende del contenido del adjunto, no solo del tipo de documento.</b> El mapa es
/// <see cref="AccionesDeContenido"/> y se usa igual en los tres caminos: mandar, mandar una fila
/// encolada y descargar la copia. El atributo del endpoint no puede mirar eso, porque solo ve el
/// verbo y la ruta; el tipo de documento está en el cuerpo y el contenido del archivo no está en
/// ninguna parte de la petición.
/// </para>
/// </summary>
public sealed class ServicioEnvios(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioFacturas facturas,
    ServicioCobros cobros,
    ServicioCotizaciones cotizaciones,
    ServicioCuentas cuentas,
    ServicioPlantillas plantillas,
    IPolicyService politica,
    IServicioCorreo correo,
    OpcionesCorreo opciones,
    IRegistroAlertas alertas,
    ServicioCifrado cifrado,
    TimeProvider reloj)
{
    /// <summary>Por qué se puede mandar cada tipo de documento (§2, docs/ESPECIFICACION.md).</summary>
    private static string AccionDe(TipoDocumentoEnvio tipo) => tipo switch
    {
        // Una cotización es un documento comercial que se manda para que alguien lo acepte; una
        // factura es un documento que ya está emitido y se manda para que lo pague. Son acciones
        // distintas y por eso permisos distintos: quien puede mandar una factura no puede, solo por
        // eso, mandar una cotización.
        TipoDocumentoEnvio.Cotizacion => Acciones.CotizacionEnviar,
        _ => Acciones.DocumentoEnviar,
    };

    /// <summary>
    /// Qué permiso exige el <b>contenido</b> de cada tipo de envío, y no el envío. Está en un solo
    /// sitio porque es la respuesta a una sola pregunta: <i>¿qué puede ver quien recibe o recupera
    /// este archivo?</i>
    /// <para>
    /// <b>El permiso de envío y el de lectura no son el mismo, y por eso no basta con el primero.</b>
    /// <c>documento.enviar</c> lo tienen gerente y administrador; quien lo tiene puede volver a
    /// mandar el archivo a quien quiera, y ese archivo vuelve a entrar por un camino donde solo se
    /// comprueba la lectura. Si «puede mandarse» y «puede leerse» fueran la misma pregunta, un
    /// permiso de envío acabaría siendo un permiso de informe para todo el que lo tenga.
    /// </para>
    /// <para>
    /// El criterio es el de §2 y el de AGENTS.md §6: lo que es un documento individual se lee con la
    /// acción de lectura de su propio recurso, y <b>lo que lleva totales agregados exige
    /// <c>informe.ver</c></b>, porque es un informe y no un documento. El estado de cuenta del
    /// cliente —el único que hay hoy— lleva filas, totales por moneda y saldo pendiente: es
    /// exactamente el papel de <c>GET /api/v1/clientes/{id}/estado-de-cuenta.pdf</c>, y por eso usa el
    /// mismo permiso que ese endpoint. Los recordatorios (§3.14) llevan ese mismo estado de cuenta
    /// adjunto, así que caen aquí igual.
    /// </para>
    /// </summary>
    private static string[] AccionesDeContenido(TipoDocumentoEnvio tipo) => tipo switch
    {
        TipoDocumentoEnvio.EstadoCuenta => [Acciones.InformeVer],

        // Un contrato se lee con `cliente.ver`, que es la fila de §2 que cubre «ver clientes,
        // unidades, reservas y contratos» y la misma que usa ContratosController.
        TipoDocumentoEnvio.Contrato => [Acciones.ClienteVer],

        // Factura, proforma, recibo y cotización son documentos individuales: `factura.ver`, que es
        // «ver facturas individuales» y también la lectura de cotizaciones (CotizacionesController).
        // Ninguno lleva totales agregados, así que a ninguno hay que pedirle `informe.ver`.
        _ => [Acciones.FacturaVer],
    };

    /// <summary>Qué clase de mensaje compone el correo de cada tipo de documento (§3.14).</summary>
    private static TipoMensajeEnvio MensajeDe(TipoDocumentoEnvio tipo) => tipo switch
    {
        TipoDocumentoEnvio.Cotizacion => TipoMensajeEnvio.Cotizacion,
        TipoDocumentoEnvio.EstadoCuenta => TipoMensajeEnvio.EstadoCuenta,
        _ => TipoMensajeEnvio.Factura,
    };

    // ---------------------------------------------------------------- envío

    /// <summary>
    /// Manda un documento a un destinatario y deja la fila con la copia exacta y con el
    /// <c>modificada_at</c> que tenía el documento al salir.
    /// <para>
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve el envío ya registrado en vez de
    /// mandar el mismo documento dos veces.
    /// </para>
    /// </summary>
    public async Task<EnvioFila> EnviarAsync(
        EnvioPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var accion = AccionDe(peticion.TipoDocumento);
        var recurso = $"{peticion.TipoDocumento}/{peticion.DocumentoId:D}";

        await ExigirAsync(accion, recurso, usuario, ct);
        await ExigirContenidoAsync(peticion.TipoDocumento, recurso, usuario, ct);

        // Sin correo configurado no se genera ni el PDF: el 409 tiene que salir antes de gastar una
        // composición de documento para nada.
        if (!opciones.Configurado)
        {
            throw new CorreoNoConfiguradoException(
                "Falta escribir la configuración del servidor de correo del despliegue, en variables de "
                + "entorno o en «dotnet user-secrets» y nunca en la base de datos (Correo__Host, "
                + "Correo__Puerto, Correo__Usuario, Correo__Clave, Correo__Ssl, Correo__Remitente y "
                + "Correo__NombreRemitente; el orden de los nombres está en config/variables-entorno.example). "
                + "Es configuración de despliegue y a propósito no hay ninguna que valga por defecto: mandar "
                + "sin que nadie haya dicho a dónde, es peor que no mandar.");
        }

        var documento = await DocumentoAsync(peticion, usuario, ct);
        var destinatario = await DestinatarioAsync(peticion, documento.ClienteId, ct);

        await ExigirSinDuplicadoAsync(peticion, destinatario.Email, ct);

        var idioma = IdiomaDe(peticion);
        var plantilla = await PlantillaAsync(MensajeDe(peticion.TipoDocumento), idioma, ct);

        // La fila guarda el idioma de la plantilla que se usó de verdad, no el que se pidió. Si se
        // pidió «fr» y no la hay, sale la de «es» y la fila dice «es»: mandar en el idioma que no está
        // es un fallo, y decir lo contrario sería una afirmación falsa sobre lo que recibió el
        // cliente.
        idioma = plantilla.Idioma;

        var texto = documento.De();
        var (asunto, cuerpo) = texto.Sustituir(plantilla);

        var mensaje = new MensajeCorreo(
            destinatario.Email,
            destinatario.Nombre,
            asunto,
            cuerpo,
            documento.NombreArchivo,
            documento.Pdf);

        // El intento se cuenta antes de llamar al servidor: si esto se cae a mitad, el contador
        // refleja lo que pasó de verdad, que es lo que se quiere contar.
        var intentos = await intentosPreviamenteAsync(peticion, destinatario.Email, ct);

        EstadoEnvio estado;
        string? error = null;

        try
        {
            await correo.EnviarAsync(mensaje, ct);
            estado = EstadoEnvio.Enviado;
        }
        catch (CorreoNoConfiguradoException)
        {
            // Esta comprobación ya está hecha arriba y no depende del transporte. Si aparece aquí es
            // que la configuración cambió a mitad, y se trata como cualquier otro fallo de envío:
            // fila fallida y el motivo, que no lleva ninguna credencial.
            estado = EstadoEnvio.Fallido;
            error = "El correo dejó de estar configurado mientras se mandaba.";
        }
        catch (Exception e)
        {
            estado = EstadoEnvio.Fallido;
            error = MotivoDe(e);
        }

        var fila = new Envio
        {
            TipoDocumento = peticion.TipoDocumento,
            DocumentoId = documento.Id,
            DocumentoAnio = documento.Anio,
            ClienteId = documento.ClienteId,
            ContactoClienteId = destinatario.ContactoClienteId,
            DestinatarioEmail = destinatario.Email,
            DestinatarioNombre = destinatario.Nombre,
            Asunto = asunto,
            Cuerpo = cuerpo,
            Idioma = idioma,
            Estado = estado,
            NombreArchivo = documento.NombreArchivo,
            PdfCopia = cifrado.CifrarBytes(documento.Pdf),
            DocumentoModificadoAt = documento.ModificadaAt,
            Intentos = intentos + 1,
            UltimoError = error,
            EnviadoAt = estado == EstadoEnvio.Enviado ? reloj.GetUtcNow() : null,
        };

        if (estado == EstadoEnvio.Fallido)
        {
            await AvisarFalloAsync(fila, error, ct);
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Envio, fila,
            esAlta: true,
            accion: "envio.registrado",
            entidad: "envio",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    // ---------------------------------------------------------------- envío de una fila encolada

    /// <summary>
    /// Manda un envío que ya está en la fila: es lo que hace el trabajo de la cola del envío masivo
    /// (F4-03, §3.14) y lo que hace su reintento.
    /// <para>
    /// <b>No crea una fila nueva: manda la que hay.</b> El reintento de §3.14 es «reintenta solo
    /// los fallidos», y eso se cumple sobre la fila que falló —subiendo su contador de intentos y
    /// guardando el resultado—, no abriendo otra fila igual. Una fila nueva dejaría dos intentos
    /// del mismo correo en el mismo lote y el recuento del lote mentiría.
    /// </para>
    /// <para>
    /// <b>El PDF sale de la fila si ya está.</b> En el primer intento la copia está vacía y se
    /// compone aquí, igual que en <see cref="EnviarAsync"/>; en el reintento se reutiliza la copia
    /// del intento anterior, porque es la que se mandó y la que queda como prueba: regenerarla daría
    /// bytes distintos en un archivo que se llama «la copia exacta de lo que salió».
    /// </para>
    /// <para>
    /// <b>Una fila ya enviada no se vuelve a mandar.</b> Es la puerta que hace idempotente el trabajo:
    /// si la cola entrega dos veces el mismo trabajo o alguien encola un reintento que ya salió, el
    /// segundo no hace nada en lugar de mandar el mismo correo dos veces.
    /// </para>
    /// </summary>
    /// <param name="usuario">
    /// Usuario al que se le comprueba el permiso. Un trabajo de Hangfire no tiene sesión HTTP, y el
    /// permiso se le comprueba a quien pidió el envío, que es quien lo va a firmar ante el cliente.
    /// </param>
    public async Task<EstadoEnvio> EnviarPendienteAsync(
        Guid envioId,
        Guid usuario,
        string ip,
        CancellationToken ct) =>
        (await EnviarFilaAsync(envioId, usuario, ip, null, "envio.registrado", ct)).Estado;

    private async Task<(EstadoEnvio Estado, bool Reclamada, EnvioFila Fila)> EnviarFilaAsync(
        Guid envioId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        string accionExito,
        CancellationToken ct)
    {
        var envio = await contexto.Envio.FirstOrDefaultAsync(e => e.Id == envioId, ct)
            ?? throw new KeyNotFoundException($"No hay ningún envío con identificador '{envioId:D}'.");

        var recurso = $"{envio.TipoDocumento}/{envio.DocumentoId:D}";

        await ExigirAsync(AccionDe(envio.TipoDocumento), recurso, usuario, ct);
        await ExigirContenidoAsync(envio.TipoDocumento, recurso, usuario, ct);

        // Salido, o ya reclamado por otro intento (BUG-038): no se manda de nuevo. `Enviando` es un
        // envío que alguien ya sacó hacia el servidor de correo; si ese intento murió antes de apuntar
        // el resultado no se sabe si el correo salió, y reenviar arriesga el duplicado. Se deja para
        // revisión manual (el barrido avisa pasado un tiempo, AvisarEnviandoAtascadoAsync).
        if (envio.Estado is EstadoEnvio.Enviado or EstadoEnvio.Entregado or EstadoEnvio.Leido
            or EstadoEnvio.Enviando)
        {
            return (envio.Estado, false, ARespuesta(envio));
        }

        var pdf = cifrado.DescifrarBytes(envio.PdfCopia);
        var nombreArchivo = envio.NombreArchivo;
        var modificadaAt = envio.DocumentoModificadoAt;

        if (pdf.Length == 0)
        {
            var documento = await DocumentoAsync(
                new EnvioPeticion
                {
                    TipoDocumento = envio.TipoDocumento,
                    DocumentoId = envio.DocumentoId,
                    DocumentoAnio = envio.DocumentoAnio,
                },
                usuario,
                ct);

            pdf = documento.Pdf;
            nombreArchivo = documento.NombreArchivo;
            modificadaAt ??= documento.ModificadaAt;
        }

        // BUG-038: la fila se reclama con un UPDATE condicional antes de llamar al servidor de correo.
        // Dos trabajos simultáneos (o un reintento de Hangfire tras un fallo al guardar) compiten por
        // este UPDATE y solo uno lo gana; el que pierde no manda nada. El intento se cuenta aquí, en
        // el mismo UPDATE, así que el contador refleja lo que pasó aunque el proceso muera a mitad.
        var ahora = reloj.GetUtcNow();
        var reclamada = await contexto.Envio
            .Where(e => e.Id == envioId
                && (e.Estado == EstadoEnvio.Pendiente || e.Estado == EstadoEnvio.Fallido))
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Estado, EstadoEnvio.Enviando)
                    .SetProperty(e => e.Intentos, e => e.Intentos + 1)
                    .SetProperty(e => e.UpdatedAt, ahora)
                    .SetProperty(e => e.UpdatedBy, usuario),
                ct);

        // La entidad ya estaba rastreada y el UPDATE fue directo a la BD: se refresca para no guardar
        // encima del estado nuevo con el viejo.
        await contexto.Entry(envio).ReloadAsync(ct);

        if (reclamada == 0)
        {
            return (envio.Estado, false, ARespuesta(envio));
        }

        envio.DocumentoModificadoAt = modificadaAt;

        var mensaje = new MensajeCorreo(
            envio.DestinatarioEmail,
            envio.DestinatarioNombre,
            envio.Asunto,
            envio.Cuerpo,
            nombreArchivo,
            pdf);

        EstadoEnvio estado;
        string? error = null;

        try
        {
            await correo.EnviarAsync(mensaje, ct);
            estado = EstadoEnvio.Enviado;
        }
        catch (CorreoNoConfiguradoException)
        {
            estado = EstadoEnvio.Fallido;
            error = "El correo dejó de estar configurado mientras se mandaba.";
        }
        catch (Exception e)
        {
            estado = EstadoEnvio.Fallido;
            error = MotivoDe(e);
        }

        envio.PdfCopia = cifrado.CifrarBytes(pdf);
        envio.NombreArchivo = nombreArchivo;
        envio.Estado = estado;
        envio.UltimoError = error;
        envio.EnviadoAt = estado == EstadoEnvio.Enviado ? reloj.GetUtcNow() : null;

        var fila = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Envio, envio,
            esAlta: false,
            accion: envio.Estado == EstadoEnvio.Enviado ? accionExito : "envio.fallido",
            entidad: "envio",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);

        if (estado == EstadoEnvio.Fallido)
        {
            await AvisarFalloAsync(envio, error, ct);
        }

        return (estado, true, fila);
    }

    /// <summary>
    /// Reintenta <b>un</b> envío suelto que falló. Mismo permiso que mandar ese tipo de documento
    /// (<see cref="AccionDe"/> más <see cref="AccionesDeContenido"/>), y solo desde <c>Fallido</c>.
    /// <para>
    /// Reutiliza la fila (sube el contador de intentos, no crea otra) y la copia del PDF del primer
    /// intento. La fila se reclama con el UPDATE condicional de <see cref="EnviarFilaAsync"/>: dos
    /// reintentos a la vez compiten por él y solo uno manda; el otro es 409. Con la misma
    /// <c>Idempotency-Key</c>, el segundo devuelve la respuesta ya guardada.
    /// </para>
    /// </summary>
    public async Task<EnvioFila> ReintentarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var envio = await contexto.Envio.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"No hay ningún envío con identificador '{id:D}'.");

        var recurso = $"{envio.TipoDocumento}/{envio.DocumentoId:D}";

        await ExigirAsync(AccionDe(envio.TipoDocumento), recurso, usuario, ct);
        await ExigirContenidoAsync(envio.TipoDocumento, recurso, usuario, ct);

        if (envio.Estado != EstadoEnvio.Fallido)
        {
            throw new InvalidOperationException(
                $"Solo se puede reintentar un envío fallido, y este está en estado '{envio.Estado}'.");
        }

        if (!opciones.Configurado)
        {
            throw new CorreoNoConfiguradoException(
                "Falta escribir la configuración del servidor de correo del despliegue (Correo__Host, "
                + "Correo__Puerto, Correo__Usuario, Correo__Clave, Correo__Ssl, Correo__Remitente y "
                + "Correo__NombreRemitente; los nombres están en config/variables-entorno.example).");
        }

        var (_, reclamada, fila) = await EnviarFilaAsync(id, usuario, ip, mutacion, "envio.reintentado", ct);

        return reclamada
            ? fila
            : throw new InvalidOperationException("Este envío ya se está reintentando o ya salió.");
    }

    /// <summary>
    /// Deja una fila encolada como <c>Fallido</c> sin llamar al servidor de correo (BUG-038): el
    /// trabajo lo usa cuando el usuario que pidió el envío ya no tiene permiso. Reintentar no lo va a
    /// arreglar, y dejarla <c>Pendiente</c> haría que el barrido la reencolara para siempre.
    /// </summary>
    public async Task MarcarFallidoAsync(Guid envioId, string motivo, Guid usuario, string ip, CancellationToken ct)
    {
        var envio = await contexto.Envio.FirstOrDefaultAsync(e => e.Id == envioId, ct);

        if (envio is null || envio.Estado != EstadoEnvio.Pendiente)
        {
            return;
        }

        envio.Estado = EstadoEnvio.Fallido;
        envio.UltimoError = motivo;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Envio, envio,
            esAlta: false,
            accion: "envio.fallido",
            entidad: "envio",
            usuario,
            ip,
            mutacion: null,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);

        await AvisarFalloAsync(envio, motivo, ct);
    }

    /// <summary>
    /// Alerta de un envío que lleva demasiado en <c>Enviando</c> (BUG-038): se reclamó y nadie apuntó el
    /// resultado, así que no se sabe si el correo salió. Hay que mirarlo a mano; no se reenvía solo.
    /// </summary>
    public async Task AvisarEnviandoAtascadoAsync(Envio envio, CancellationToken ct) =>
        await alertas.RegistrarAsync(
            TipoAlertaEnvioFallido,
            $"envio:{envio.Id:D}:atascado",
            $"El envío '{envio.Id:D}' ({envio.TipoDocumento} a {envio.DestinatarioEmail}) lleva mucho tiempo "
                + "en curso sin resultado: no se sabe si el correo salió. Compruébalo en el servidor de correo "
                + "antes de reenviarlo.",
            ct);

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Envíos, filtrables por tipo de documento, por estado y por documento (§3.13 y §3.14). Los tres
    /// filtros son los mismos que usa la vista única y por eso tienen un índice detrás.
    /// </summary>
    public async Task<Pagina<EnvioFila>> ListarAsync(
        ConsultaLista paginacion,
        TipoDocumentoEnvio? tipo,
        EstadoEnvio? estado,
        Guid? documentoId,
        Guid usuario,
        CancellationToken ct)
    {
        // SEC-030: el asunto ya lleva las variables sustituidas, y el de un estado de cuenta o un
        // recordatorio puede citar el saldo pendiente del cliente entero, que es un total agregado.
        // Quien no tiene el permiso del contenido (informe.ver) ve la fila, pero sin asunto.
        var ocultarAsuntoDeInforme = !await politica.PuedeAsync(Acciones.InformeVer, usuario, ct);

        var consulta = contexto.Envio.AsNoTracking()
            .Where(e => !tipo.HasValue || e.TipoDocumento == tipo)
            .Where(e => !estado.HasValue || e.Estado == estado)
            .Where(e => !documentoId.HasValue || e.DocumentoId == documentoId);

        return await ConsultaPaginada.PaginarAsync(
            consulta,
            Ordenes,
            "enviadoAt",
            paginacion,
            e => ocultarAsuntoDeInforme && AccionesDeContenido(e.TipoDocumento).Contains(Acciones.InformeVer)
                ? ARespuesta(e) with { Asunto = string.Empty }
                : ARespuesta(e),
            ct);
    }

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<Envio>, bool, IOrderedQueryable<Envio>>> Ordenes =
        new Dictionary<string, Func<IQueryable<Envio>, bool, IOrderedQueryable<Envio>>>
        {
            ["enviadoAt"] = (q, desc) => desc
                ? q.OrderByDescending(e => e.EnviadoAt ?? e.CreatedAt).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.EnviadoAt ?? e.CreatedAt).ThenBy(e => e.Id),
            ["createdAt"] = (q, desc) => desc
                ? q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            ["estado"] = (q, desc) => desc
                ? q.OrderByDescending(e => e.Estado).ThenByDescending(e => e.Id)
                : q.OrderBy(e => e.Estado).ThenBy(e => e.Id),
        };

    /// <summary>
    /// La copia exacta del PDF que salió. <b>No regenera el documento</b>: lee
    /// <c>envio.pdf_copia</c>, que es lo que se le mandó al cliente. Si se regenerara, el archivo
    /// cambiaría en cuanto cambiara el documento y dejaría de ser la copia.
    /// </summary>
    /// <param name="usuario">
    /// Usuario al que se le comprueba el permiso del <b>contenido</b> de lo que se va a descargar, que
    /// no siempre es el mismo que el de la puerta del endpoint: la copia de un estado de cuenta
    /// <i>es</i> el estado de cuenta y por eso pide <c>informe.ver</c>, aunque la puerta sea
    /// <c>factura.ver</c>.
    /// </param>
    public async Task<(byte[] Pdf, string Nombre)> CopiaAsync(
        Guid id,
        Guid usuario,
        CancellationToken ct)
    {
        var envio = await contexto.Envio.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (envio is null)
        {
            throw new KeyNotFoundException($"No hay ningún envío con identificador '{id:D}'.");
        }

        await ExigirContenidoAsync(
            envio.TipoDocumento, $"{envio.TipoDocumento}/{envio.DocumentoId:D}", usuario, ct);

        return (cifrado.DescifrarBytes(envio.PdfCopia), envio.NombreArchivo);
    }

    // ---------------------------------------------------------------- envío del documento

    /// <summary>
    /// El PDF y los datos del correo de un documento. El <c>switch</c> es el mapa de
    /// <see cref="TipoDocumentoEnvio"/> a los cinco generadores de F4-01: los seis tipos de documento
    /// de §3.14 son cinco generadores porque proforma y factura salen del mismo.
    /// </summary>
    private async Task<DocumentoParaEnvio> DocumentoAsync(EnvioPeticion peticion, Guid usuario, CancellationToken ct)
    {
        switch (peticion.TipoDocumento)
        {
            case TipoDocumentoEnvio.Factura:
            {
                // El modelo del PDF de F4-01 ya trae el cliente, el número, los importes y el
                // vencimiento formateados. Se reutiliza en vez de volver a formatearlos aquí: lo que
                // dice el correo y lo que dice el papel tienen que salir de la misma fuente. Y el PDF
                // se compone <b>desde ese mismo modelo</b>, en vez de llamar a <c>PdfAsync</c> y
                // perderlo: componerlo dos veces gastaría el doble y, en el peor caso, adjuntaría un
                // papel con unos números y pondría otros en el texto.
                var modelo = await facturas.ModeloPdfAsync(peticion.DocumentoId, ct);
                var variables = VariablesMensaje.DeFactura(modelo);

                return new DocumentoParaEnvio(
                    peticion.TipoDocumento,
                    peticion.DocumentoId,
                    null,
                    await ClienteIdDe(peticion.DocumentoId, ct),
                    variables.Cliente,
                    variables.Numero,
                    variables.Total,
                    variables.Pendiente,
                    variables.Vencimiento,
                    variables.Unidad,
                    $"{modelo.TipoDocumento.ToLowerInvariant()}-{ParteDeArchivo(variables.Numero)}.pdf",
                    await ModificadaAtDe(peticion.DocumentoId, ct),
                    DocumentoFacturaPdf.Generar(modelo));
            }

            case TipoDocumentoEnvio.Recibo:
            {
                var modelo = await cobros.ModeloPdfAsync(peticion.DocumentoId, ct);

                return new DocumentoParaEnvio(
                    peticion.TipoDocumento,
                    peticion.DocumentoId,
                    null,
                    await ClienteIdDeCobroAsync(peticion.DocumentoId, ct),
                    modelo.Cliente.Nombre,
                    modelo.Numero,
                    modelo.ImporteRecibido,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    $"{ParteDeArchivo(modelo.Numero)}.pdf",
                    null,
                    DocumentoReciboPdf.Generar(modelo));
            }

            case TipoDocumentoEnvio.EstadoCuenta:
            {
                var clienteId = peticion.DocumentoId;
                var anio = peticion.DocumentoAnio ?? reloj.GetUtcNow().Year;
                var modelo = await cuentas.ModeloEstadoCuentaPdfAsync(clienteId, anio, usuario, ct);

                // El pendiente del estado de cuenta solo sale si el cliente tiene una sola moneda en
                // el año. Con dos, hay dos pendientes y poner uno de los dos sería mentir; la
                // variable queda vacía y quien redacte la plantilla lo sabe porque el papel lo dice.
                var pendiente = modelo.Totales.Count == 1 ? modelo.Totales[0].Pendiente : null;

                return new DocumentoParaEnvio(
                    peticion.TipoDocumento,
                    clienteId,
                    anio,
                    clienteId,
                    modelo.Cliente.Nombre,
                    modelo.Periodo,
                    modelo.Totales.Count == 1 ? modelo.Totales[0].Total : string.Empty,
                    pendiente ?? string.Empty,
                    string.Empty,
                    modelo.Cliente.Unidad ?? string.Empty,
                    $"estado-de-cuenta-{anio}.pdf",
                    null,
                    DocumentoEstadoCuentaPdf.Generar(modelo));
            }

            case TipoDocumentoEnvio.Cotizacion:
            {
                var cotizacion = await cotizaciones.LeerAsync(peticion.DocumentoId, ct);
                var nombre = cotizacion.ClienteNombre ?? cotizacion.ProspectoNombre ?? string.Empty;

                return new DocumentoParaEnvio(
                    peticion.TipoDocumento,
                    peticion.DocumentoId,
                    null,
                    cotizacion.ClienteId,
                    nombre,
                    string.Empty,
                    FormatoDocumentoPdf.Importe(cotizacion.Total, cotizacion.Moneda),
                    string.Empty,
                    string.Empty,
                    cotizacion.UnidadNombre ?? string.Empty,
                    $"cotizacion-{peticion.DocumentoId:D}.pdf",
                    await ModificadaAtDe(peticion.DocumentoId, ct),
                    await cotizaciones.PdfAsync(peticion.DocumentoId, ct));
            }

            case TipoDocumentoEnvio.Contrato:
            {
                var contrato = await contexto.Contrato.AsNoTracking()
                    .Include(c => c.Cliente)
                    .Include(c => c.Unidad)
                    .FirstOrDefaultAsync(c => c.Id == peticion.DocumentoId, ct)
                    ?? throw new KeyNotFoundException($"No hay ningún contrato con identificador '{peticion.DocumentoId:D}'.");

                return new DocumentoParaEnvio(
                    peticion.TipoDocumento,
                    contrato.Id,
                    null,
                    contrato.ClienteId,
                    contrato.Cliente?.Nombre ?? string.Empty,
                    string.Empty,
                    FormatoDocumentoPdf.Importe(contrato.Renta, contrato.Moneda),
                    string.Empty,
                    string.Empty,
                    contrato.Unidad?.Nombre ?? string.Empty,
                    $"contrato-{contrato.Id:D}.pdf",
                    null,
                    await plantillas.PdfContratoAsync(contrato.Id, ct));
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(peticion), peticion.TipoDocumento, "Tipo de documento que no se sabe mandar.");
        }
    }

    /// <summary>
    /// El <c>modificada_at</c> del documento en el instante de mandar. Se lee aparte del PDF porque
    /// es el dato que decide si el documento se tocó después (§3.13): es lo que se guarda en
    /// <c>envio.documento_modificado_at</c> y contra eso se compara el de ahora.
    /// <para>
    /// Solo lo tienen las tablas que lo guardan, que son las de los documentos que se pueden
    /// reescribir: facturas (F3-08) y cotizaciones. Un recibo o un contrato no tienen esa columna y
    /// salen con <c>NULL</c>, que es lo que dice «no se puede saber».
    /// </para>
    /// </summary>
    private async Task<DateTimeOffset?> ModificadaAtDe(Guid documentoId, CancellationToken ct) =>
        await contexto.Factura.AsNoTracking()
            .Where(f => f.Id == documentoId)
            .Select(f => (DateTimeOffset?)f.ModificadaAt)
            .FirstOrDefaultAsync(ct)
        ?? await contexto.Cotizacion.AsNoTracking()
            .Where(c => c.Id == documentoId)
            .Select(c => (DateTimeOffset?)c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<Guid?> ClienteIdDe(Guid facturaId, CancellationToken ct) =>
        await contexto.Factura.AsNoTracking()
            .Where(f => f.Id == facturaId)
            .Select(f => (Guid?)f.ClienteId)
            .FirstOrDefaultAsync(ct);

    private async Task<Guid?> ClienteIdDeCobroAsync(Guid cobroId, CancellationToken ct) =>
        await contexto.Cobro.AsNoTracking()
            .Where(c => c.Id == cobroId)
            .Select(c => (Guid?)c.ClienteId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Convierte el número que ya viene formateado («B-00012» o «REC-1A2B3C4D») en la parte central de
    /// un nombre de archivo. No se inventa una secuencia: se toma lo que el documento ya dice y se
    /// quitan los caracteres que no pueden ir en un nombre de archivo.
    /// <para>
    /// La usa también el resumen del envío masivo (F4-03), que compone el mismo nombre de archivo
    /// antes de encolar. Que sea la misma función es lo que hace que el archivo que anuncia el
    /// resumen y el que se adjunta sean el mismo y no dos nombres parecidos.
    /// </para>
    /// </summary>
    internal static string ParteDeArchivo(string numero)
    {
        var limpio = new string(numero.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

        return limpio.Length == 0 ? "documento" : limpio.ToLowerInvariant();
    }

    // ---------------------------------------------------------------- destinatario

    /// <summary>
    /// A quién se manda. Tres caminos, en este orden: el contacto de la ficha, el correo escrito a
    /// mano, o el correo del cliente. El correo se pasa a minúsculas porque es la clave con la que
    /// §3.14 decide si es un reenvío: <c>Cliente@x.com</c> y <c>cliente@x.com</c> son la misma
    /// casilla y si no se normalizan el antidiplicado deja pasar el segundo.
    /// </summary>
    private async Task<DestinatarioResuelto> DestinatarioAsync(
        EnvioPeticion peticion, Guid? clienteDelDocumento, CancellationToken ct)
    {
        if (peticion.ContactoClienteId is { } contactoId)
        {
            var contacto = await contexto.ContactoCliente.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == contactoId, ct)
                ?? throw new KeyNotFoundException($"No hay ningún contacto con identificador '{contactoId:D}'.");

            if (string.IsNullOrWhiteSpace(contacto.Email))
            {
                throw new InvalidOperationException(
                    $"El contacto «{contacto.Nombre}» no tiene correo, así que no se le puede mandar nada. "
                    + "Escríbelo en el destinatario a mano o elige otro contacto.");
            }

            return new DestinatarioResuelto(Normalizar(contacto.Email), contacto.Nombre, contacto.Id);
        }

        if (!string.IsNullOrWhiteSpace(peticion.Email))
        {
            var nombre = await NombreDelClienteAsync(peticion.ClienteId ?? clienteDelDocumento, ct);

            return new DestinatarioResuelto(Normalizar(peticion.Email), nombre, null);
        }

        var clienteId = peticion.ClienteId ?? clienteDelDocumento
            ?? throw new InvalidOperationException(
                "No hay a quién mandarlo: el documento no trae cliente, y la petición no dice ni contacto "
                + "ni correo. Hay que mandar a una dirección concreta.");

        var cliente = await contexto.Cliente.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clienteId, ct)
            ?? throw new KeyNotFoundException($"No hay ningún cliente con identificador '{clienteId:D}'.");

        if (string.IsNullOrWhiteSpace(cliente.Email))
        {
            throw new InvalidOperationException(
                $"El cliente «{cliente.Nombre}» no tiene correo en su ficha, así que no se le puede mandar "
                + "nada. Ponle el correo al cliente o escribe el destinatario en el envío.");
        }

        return new DestinatarioResuelto(Normalizar(cliente.Email), cliente.Nombre, null);
    }

    private static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    private async Task<string?> NombreDelClienteAsync(Guid? clienteId, CancellationToken ct)
    {
        if (clienteId is not { } id)
        {
            return null;
        }

        return await contexto.Cliente.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.Nombre)
            .FirstOrDefaultAsync(ct);
    }

    // ---------------------------------------------------------------- reglas

    /// <summary>
    /// Antidiplicados de §3.14: el mismo documento al mismo destinatario ya enviado se excluye,
    /// salvo que el llamante fuerce.
    /// <para>
    /// <b>Solo cuentan los envíos que salieron.</b> Un envío <c>Fallido</c> no bloquea nada: si
    /// bloqueara, reintentar un correo que falló por un problema del servidor obligaría a forzarlo
    /// cada vez, y forzar es exactamente lo que hay que reservar para el reenvío que alguien ha
    /// pedido dos veces a propósito.
    /// </para>
    /// </summary>
    private async Task ExigirSinDuplicadoAsync(EnvioPeticion peticion, string email, CancellationToken ct)
    {
        if (peticion.Forzar)
        {
            return;
        }

        var yaSalio = await contexto.Envio.AsNoTracking()
            .AnyAsync(
                e => e.TipoDocumento == peticion.TipoDocumento
                    && e.DocumentoId == peticion.DocumentoId
                    && e.DocumentoAnio == peticion.DocumentoAnio
                    && e.DestinatarioEmail == email
                    && e.Estado != EstadoEnvio.Fallido,
                ct);

        if (!yaSalio)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Este documento ya se mandó a {email}. Si el reenvío es a propósito, vuelve a pedirlo con "
            + "«forzar» en la petición.");
    }

    /// <summary>
    /// Intentos que ya lleva este documento a este destinatario. Cuenta también los fallidos, que es
    /// justo lo que dice el número: cuántos intentos lleva, no cuántos salieron.
    /// </summary>
    private async Task<int> intentosPreviamenteAsync(EnvioPeticion peticion, string email, CancellationToken ct) =>
        await contexto.Envio.AsNoTracking()
            .Where(e => e.TipoDocumento == peticion.TipoDocumento
                && e.DocumentoId == peticion.DocumentoId
                && e.DocumentoAnio == peticion.DocumentoAnio
                && e.DestinatarioEmail == email)
            .SumAsync(e => e.Intentos, ct);

    /// <summary>
    /// La plantilla del tipo de mensaje en el idioma pedido, o la de «es» si ese idioma no existe.
    /// <para>
    /// No es un error que un idioma no tenga plantilla: §3.14 no dice que el envío tenga que
    /// rechazarse porque nadie escribió la versión en francés, y bloquear el correo por eso deja al
    /// cliente sin factura. Se manda en español y la fila guarda el idioma real, que es el que se usó.
    /// </para>
    /// </summary>
    internal async Task<PlantillaMensaje> PlantillaAsync(TipoMensajeEnvio tipo, string idioma, CancellationToken ct)
    {
        var pedida = await contexto.PlantillaMensaje.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Idioma == idioma && p.Activa, ct);

        if (pedida is not null)
        {
            return pedida;
        }

        var respaldo = await contexto.PlantillaMensaje.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tipo == tipo && p.Idioma == IdiomaPorDefecto && p.Activa, ct)
            ?? throw new InvalidOperationException(
                $"No hay plantilla activa para el mensaje de tipo '{tipo}' en «{IdiomaPorDefecto}». La siembra "
                + "de la migración «EnviosYPlantillasMensaje» trae las cinco; si se desactivaron todas, hay "
                + "que volver a activar una.");

        return respaldo;
    }

    private const string IdiomaPorDefecto = "es";

    /// <summary>
    /// Levanta la alerta de §3.16 de un envío que no salió. Existe desde F4-05 y antes no había
    /// ninguna: un envío fallido se quedaba en su fila con su <c>ultimo_error</c> y nadie se enteraba
    /// salvo quien abriera esa fila.
    /// <para>
    /// La clave lleva el envío y no la fecha, porque el asunto es «este envío falló» y no «hoy falló
    /// un envío»: reintentar el mismo documento mañana suma repeticiones en la misma fila en vez de
    /// llenar la bandeja de copias. Y el motivo es el que ya se guarda en <c>ultimo_error</c>, que
    /// por F3-01 es el mensaje de una excepción escrita para leerse o el tipo de cualquier otra, nunca
    /// el texto de una excepción de SMTP con la URL o la cadena de conexión dentro.
    /// </para>
    /// </summary>
    private async Task AvisarFalloAsync(Envio envio, string? error, CancellationToken ct) =>
        await alertas.RegistrarAsync(
            TipoAlertaEnvioFallido,
            ClaveAlertaEnvioFallido(envio.Id),
            $"No salió el envío '{envio.Id:D}' ({envio.TipoDocumento} a {envio.DestinatarioEmail}): "
                + $"{error}. Sigue marcado como {EstadoEnvio.Fallido} y se puede volver a mandar.",
            ct);

    /// <summary>
    /// Tipo de la alerta de envío fallido (§3.16). Es el mismo para todos los documentos: el
    /// asunto es que un correo no salió, no qué documento era.
    /// </summary>
    public const string TipoAlertaEnvioFallido = "envio";

    /// <summary>
    /// Clave de deduplicación de la alerta de un envío fallido. Lleva el envío y no la fecha: es un
    /// hecho sobre ese envío, no un hecho sobre el día.
    /// </summary>
    public static string ClaveAlertaEnvioFallido(Guid envioId) => $"envio:{envioId:D}:fallido";

    /// <summary>El idioma que se pide, normalizado, o «es» si no se pidió ninguno.</summary>
    private static string IdiomaDe(EnvioPeticion peticion) =>
        string.IsNullOrWhiteSpace(peticion.Idioma)
            ? IdiomaPorDefecto
            : peticion.Idioma.Trim().ToLowerInvariant();

    /// <summary>
    /// El motivo que se guarda en <c>ultimo_error</c>. Mismo criterio que F3-01 (DOC_TECNICA §8.0.1):
    /// solo el mensaje de una excepción escrita para que alguien lo lea; de cualquier otra, solo el
    /// tipo. El mensaje de una excepción de SMTP puede traer el host, la respuesta del servidor o la
    /// cadena de conexión, y esta columna se lee en la lista de envíos, fuera.
    /// </summary>
    private static string MotivoDe(Exception e) => e switch
    {
        CorreoNoConfiguradoException => e.Message,
        _ => $"{e.GetType().Name} sin detalle.",
    };

    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    /// <summary>
    /// Exige los permisos que pide el <b>contenido</b> del adjunto (ver
    /// <see cref="AccionesDeContenido"/>), sea lo que sea el tipo de envío. Va aparte de
    /// <see cref="AccionDe"/> porque son dos preguntas distintas: <i>¿puede mandar este documento?</i>
    /// y <i>¿puede ver lo que lleva dentro?</i>
    /// </summary>
    private async Task ExigirContenidoAsync(
        TipoDocumentoEnvio tipo,
        string recurso,
        Guid usuario,
        CancellationToken ct)
    {
        foreach (var accion in AccionesDeContenido(tipo))
        {
            await ExigirAsync(accion, recurso, usuario, ct);
        }
    }

    // ---------------------------------------------------------------- proyección

    private static EnvioFila ARespuesta(Envio e) => new(
        e.Id,
        e.TipoDocumento,
        e.DocumentoId,
        e.DocumentoAnio,
        e.ClienteId,
        e.DestinatarioEmail,
        e.DestinatarioNombre,
        e.Asunto,
        e.Idioma,
        e.Estado,
        e.NombreArchivo,
        e.DocumentoModificadoAt,
        e.Intentos,
        e.UltimoError,
        e.EnviadoAt,
        e.CreatedAt);

    /// <summary>
    /// Lo que va al campo <c>datos</c> de la auditoría. <b>Nada del cuerpo del correo ni del
    /// adjunto</b>: el texto es largo y los bytes no caben, y el destinatario ya está en la fila.
    /// El motivo de un fallo sí entra, y es seguro por cómo se construye en <see cref="MotivoDe"/>.
    /// </summary>
    private static object DatosAuditoria(Envio e) => new
    {
        tipoDocumento = e.TipoDocumento.ToString(),
        documentoId = e.DocumentoId,
        documentoAnio = e.DocumentoAnio,
        destino = e.DestinatarioEmail,
        estado = e.Estado.ToString(),
        idioma = e.Idioma,
        nombreArchivo = e.NombreArchivo,
        bytes = e.PdfCopia.Length,
        intentos = e.Intentos,
        documentoModificadoAt = e.DocumentoModificadoAt,
        ultimoError = e.UltimoError,
    };

    // ---------------------------------------------------------------- modelos internos

    /// <summary>
    /// Los valores de la plantilla para un documento ya resuelto. Es el puente entre la fila que
    /// compone <see cref="DocumentoAsync"/> y la sustitución de <see cref="VariablesMensaje"/>, que
    /// también usa el envío masivo (F4-03) sin tener el PDF delante.
    /// </summary>
    private sealed record DocumentoParaEnvio(
        TipoDocumentoEnvio Tipo,
        Guid Id,
        int? Anio,
        Guid? ClienteId,
        string Cliente,
        string Numero,
        string Total,
        string Pendiente,
        string Vencimiento,
        string Unidad,
        string NombreArchivo,
        DateTimeOffset? ModificadaAt,
        byte[] Pdf)
    {
        public VariablesMensaje De() => new(Cliente, Numero, Total, Pendiente, Vencimiento, Unidad);
    }

    /// <summary>A quién sale, y de la ficha de qué cliente.</summary>
    private sealed record DestinatarioResuelto(string Email, string? Nombre, Guid? ContactoClienteId);
}