using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// El envío masivo de §3.14: <b>resumen, confirmación, cola, progreso y reintento</b> (F4-03).
/// <para>
/// <b>Tres pasos, no uno.</b> Preparar dice lo que saldría sin que salga nada, confirmar encola lo que
/// alguien confirmó, y el trabajo de la cola es lo único que llama al servidor de correo. El resumen
/// y la confirmación calculan el lote por separado y a propósito: el resumen es una respuesta que un
/// cliente puede alterar entre que la pide y la manda, y si la confirmación cogiera de ahí los
/// documentos que hay que mandar, en medio no habría nadie mirando.
/// </para>
/// <para>
/// <b>Los documentos salen de la vista única</b> (<c>ServicioCuentas.ListarAsync</c>, F3-10) con los
/// mismos filtros de <c>GET /api/v1/cuentas</c>. No hay una consulta de documentos propia: un
/// segundo «qué facturas hay» es el sitio donde una pantalla y un correo dejan de hablar de lo mismo.
/// </para>
/// <para>
/// <b>Un lote no tiene tabla.</b> Es el conjunto de las filas de <c>envio</c> que comparten
/// <c>lote_id</c>; el progreso, el recuento y el reintento son agregaciones sobre esa columna. Lo que
/// hay que guardar del lote —quién lo pidió, con qué filtros y cuándo lo confirmó— lo escribe la
/// auditoría <c>envio.masivo.confirmado</c>, que es append-only y ya existe (AGENTS.md §6). Una
/// tabla de lotes sería una segunda fuente de lo mismo, y el repo ya pagó por eso en BUG-007.
/// </para>
/// </summary>
public sealed class ServicioEnviosMasivos(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCuentas cuentas,
    ServicioFacturas facturas,
    IPolicyService politica,
    IColaEnvios cola,
    TimeProvider reloj)
{
    /// <summary>
    /// Tope de documentos de un lote.
    /// <para>
    /// Los que se quedan fuera <b>se cuentan y se dicen en el resumen</b>, nunca se recortan en
    /// silencio: un filtro sin criterios que casara con veinte mil facturas y los mandara todos es
    /// un botón que se puede apretar por error, y el daño no se ve hasta que los correos han salido.
    /// <c>TODO(A#)</c>: si el responsable quiere lotes de más, la salida es partir por periodo o por
    /// cliente y que el lote lo diga, no subir el número sin más.
    /// </para>
    /// </summary>
    public const int TopeDocumentosPorLote = 1000;

    private const string IdiomaPorDefecto = "es";

    /// <summary>
    /// Lo que devuelve una pantalla de resumen, escrito como advertencia dentro de la respuesta. No
    /// es decoración: quien llama por código no ve el nombre del endpoint, y preparar sin querer es
    /// inocuo mientras que quien lo da por hecho manda correo a clientes de verdad.
    /// </summary>
    public const string AvisoPrepara =
        "Preparar no manda nada. Esto es solo lo que se mandaría; para que salga hay que confirmar.";

    /// <summary>Lo que responde la confirmación: encolar no es mandar.</summary>
    public const string AvisoConfirma =
        "Lote encolado. Los envíos salen uno a uno en segundo plano; todavía no se ha mandado ninguno.";

    // ---------------------------------------------------------------- resumen

    /// <summary>
    /// Cuenta qué entraría en el lote, enseña la vista previa de lo que saldría y por qué se queda
    /// fuera lo que se queda fuera. <b>No escribe nada en <c>envio</c> y no encola nada</b>.
    /// <para>
    /// Acepta la <c>Idempotency-Key</c> como cualquier mutación, aunque no mute: la respuesta es
    /// larga y volver a pedirla sin querer no tiene por qué costar otro recorrido entero de la vista.
    /// </para>
    /// </summary>
    public async Task<EnvioMasivoResumen> PrepararAsync(
        EnvioMasivoPeticion peticion,
        Guid usuario,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(usuario, ct);

        var calculo = await CalcularAsync(peticion, ct);

        var resumen = new EnvioMasivoResumen(
            calculo.Documentos.Count,
            calculo.Excluidos.Count,
            calculo.Documentos,
            calculo.Excluidos,
            peticion.Filtro,
            IdiomaDe(peticion),
            AvisoPrepara);

        await idempotencia.RegistrarAsync(usuario, mutacion, resumen, ct);

        return resumen;
    }

    // ---------------------------------------------------------------- confirmación y cola

    /// <summary>
    /// Vuelve a calcular el lote con lo que llega en <b>esta</b> petición, escribe una fila
    /// <c>Pendiente</c> por documento y encola un trabajo por fila.
    /// <para>
    /// <b>No reutiliza el resumen anterior</b> aunque el cliente lo mande de vuelta: el resumen es
    /// para leerlo, y lo que manda es lo que se calcula aquí. Un lote que se confirmara sobre datos
    /// que alguien pudo editar entre las dos llamadas sería un lote que nadie miró.
    /// </para>
    /// <para>
    /// <b>La cola se crea después del commit</b>, no antes: un trabajo que arranque antes de que
    /// existan sus filas las encontraría a medio escribir, y un trabajo encolado en una transacción
    /// que luego se deshace mandaría correo que ya no está en ninguna parte.
    /// <c>TODO(A#)</c>: si el proceso se cae entre el commit y el encolado quedan filas
    /// <c>Pendiente</c> que nadie manda. Se cierran con un trabajo de barrido que re-encole lo
    /// <c>Pendiente</c> más viejo de una hora, cuando el responsable decida que es momento.
    /// </para>
    /// </summary>
    public async Task<LoteEnvio> ConfirmarAsync(
        EnvioMasivoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(usuario, ct);

        var calculo = await CalcularAsync(peticion, ct);
        var idioma = IdiomaDe(peticion);
        var loteId = Guid.NewGuid();
        var ahora = reloj.GetUtcNow();

        var filas = new List<Envio>(calculo.Documentos.Count);

        foreach (var documento in calculo.Documentos)
        {
            filas.Add(new Envio
            {
                Id = Guid.NewGuid(),
                TipoDocumento = TipoDocumentoEnvio.Factura,
                DocumentoId = documento.DocumentoId,
                DocumentoAnio = null,
                ClienteId = documento.ClienteId,
                DestinatarioEmail = documento.DestinatarioEmail,
                DestinatarioNombre = documento.DestinatarioNombre,
                Asunto = documento.Asunto,
                Cuerpo = documento.Cuerpo,
                Idioma = documento.Idioma,
                Estado = EstadoEnvio.Pendiente,

                // El nombre del archivo se sabe ya, pero la copia no: los bytes los compone el
                // trabajo, que es quien compone el PDF (Envio.PdfCopia).
                NombreArchivo = documento.NombreArchivo,

                // Cero intentos hasta que el trabajo llame al servidor, que es lo que cuenta los
                // intentos. Poner uno aquí sería decir que ya se intentó algo que nadie intentó.
                Intentos = 0,
                LoteId = loteId,
                CreatedAt = ahora,
                CreatedBy = usuario,
            });
        }

        var respuesta = new LoteEnvio(loteId, filas.Count, calculo.Excluidos.Count, idioma, AvisoConfirma);

        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            contexto.Envio.AddRange(filas);
            await contexto.SaveChangesAsync(ct);

            // Quién pidió el envío, con qué filtros y cuándo lo confirmó: en la auditoría, que es
            // append-only y no se puede reescribir. La entidad es `lote` y no una tabla: el lote no es
            // una tabla, es el identificador que comparten las filas de `envio`.
            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = "envio.masivo.confirmado",
                Ip = ip,
                UsuarioId = usuario,
                Entidad = "lote",
                EntidadId = loteId.ToString("D"),
                DatosJson = JsonSerializer.Serialize(new
                {
                    loteId,
                    documentos = filas.Count,
                    excluidos = calculo.Excluidos,
                    idioma,
                    forzar = peticion.Forzar,
                    filtro = peticion.Filtro,
                }),
                Resultado = "permitido",
                Origen = "api",
            }, ct);

            await idempotencia.RegistrarAsync(usuario, mutacion, respuesta, ct);

            await transaccion.CommitAsync(ct);
        }

        foreach (var fila in filas)
        {
            cola.EncolarEnvio(fila.Id, usuario, ip);
        }

        return respuesta;
    }

    // ---------------------------------------------------------------- progreso

    /// <summary>
    /// Cuánto ha salido del lote, cuánto se ha quedado sin salir y qué falló (F4-03, §3.14).
    /// </summary>
    public async Task<ProgresoLote> ProgresoAsync(Guid loteId, Guid usuario, CancellationToken ct)
    {
        await ExigirAsync(usuario, ct);

        var filas = await contexto.Envio.AsNoTracking()
            .Where(e => e.LoteId == loteId)
            .ToListAsync(ct);

        if (filas.Count == 0)
        {
            throw new KeyNotFoundException($"No hay ningún lote de envíos con identificador '{loteId:D}'.");
        }

        var nombres = await NombresDeClienteAsync(filas.Select(e => e.ClienteId), ct);

        return new ProgresoLote(
            loteId,
            filas.Count,
            filas.Count(e => e.Estado is EstadoEnvio.Enviado or EstadoEnvio.Entregado or EstadoEnvio.Leido),
            filas.Count(e => e.Estado == EstadoEnvio.Fallido),
            filas.Count(e => e.Estado == EstadoEnvio.Pendiente),
            [.. filas
                .Where(e => e.Estado == EstadoEnvio.Fallido)
                .OrderBy(e => e.DocumentoId)
                .Select(e => new EnvioMasivoFallo(
                    e.Id,
                    e.DocumentoId,
                    e.TipoDocumento,
                    e.ClienteId is { } id ? nombres.GetValueOrDefault(id) : null,
                    e.DestinatarioEmail,
                    e.Intentos,
                    e.UltimoError))]);
    }

    // ---------------------------------------------------------------- reintento

    /// <summary>
    /// Vuelve a encolar <b>solo los fallidos</b> del lote, que es el criterio de aceptación de §3.14.
    /// <para>
    /// <b>Sobre la misma fila, sin crear otra.</b> El reintento no vuelve a mandar el documento
    /// desde cero: sube el contador de intentos de la fila que falló y la vuelve a encolar. Una fila
    /// nueva por reintento dejaría dos intentos del mismo correo en el mismo lote y el recuento del
    /// lote —«tres documentos, dos han salido»— mentiría.
    /// </para>
    /// <para>
    /// <b>Un envío que ya salió no se toca.</b> Ni se cuenta ni se encola: el filtro es
    /// <c>Estado == Fallido</c> y solo entra en él lo que falló, así que los que salieron no ven subir
    /// su contador ni aparecen en la respuesta.
    /// </para>
    /// </summary>
    public async Task<ReintentoLote> ReintentarAsync(
        Guid loteId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(usuario, ct);

        var fallidos = await contexto.Envio
            .Where(e => e.LoteId == loteId && e.Estado == EstadoEnvio.Fallido)
            .ToListAsync(ct);

        if (fallidos.Count == 0 && !await ExisteLoteAsync(loteId, ct))
        {
            throw new KeyNotFoundException($"No hay ningún lote de envíos con identificador '{loteId:D}'.");
        }

        // Vuelven a `Pendiente` porque ahora es lo que son: están en la cola. El último error se
        // queda, que es verdad hasta que el trabajo la mande o vuelva a fallar con otro motivo.
        foreach (var envio in fallidos)
        {
            envio.Estado = EstadoEnvio.Pendiente;
        }

        var respuesta = new ReintentoLote(
            loteId,
            fallidos.Count,
            fallidos.Count,
            fallidos.Count == 0
                ? "No hay envíos fallidos en este lote: no se ha reencolado ninguno."
                : $"{fallidos.Count} envío(s) fallido(s) reencolado(s). Los que ya salieron no se tocan.");

        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            await contexto.SaveChangesAsync(ct);

            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = "envio.masivo.reintentado",
                Ip = ip,
                UsuarioId = usuario,
                Entidad = "lote",
                EntidadId = loteId.ToString("D"),
                DatosJson = JsonSerializer.Serialize(new
                {
                    loteId,
                    reintentados = fallidos.Count,
                    envios = fallidos.Select(e => e.Id),
                }),
                Resultado = "permitido",
                Origen = "api",
            }, ct);

            await idempotencia.RegistrarAsync(usuario, mutacion, respuesta, ct);

            await transaccion.CommitAsync(ct);
        }

        foreach (var envio in fallidos)
        {
            cola.EncolarEnvio(envio.Id, usuario, ip);
        }

        return respuesta;
    }

    private async Task<bool> ExisteLoteAsync(Guid loteId, CancellationToken ct) =>
        await contexto.Envio.AsNoTracking().AnyAsync(e => e.LoteId == loteId, ct);

    // ---------------------------------------------------------------- cálculo del lote

    /// <summary>
    /// Qué entra, a quién y con qué texto; y qué no entra, con el motivo. Es el cálculo de §3.14 y lo
    /// usan los dos extremos: el resumen lo enseña y la confirmación lo convierte en filas.
    /// </summary>
    private async Task<CalculoLote> CalcularAsync(EnvioMasivoPeticion peticion, CancellationToken ct)
    {
        var idioma = IdiomaDe(peticion);
        var filas = await FilasDelFiltroAsync(peticion.Filtro, ct);

        // Las listas se materializan antes de meterse en las consultas: una Contains sobre una
        // enumeración capturada no la traduce el proveedor y acaba trayéndose la tabla entera.
        var clienteIds = filas.Select(f => f.ClienteId).Distinct().ToList();
        var documentoIds = filas
            .Where(f => f.Tipo != TipoFilaCuenta.Fianza)
            .Select(f => f.Id)
            .Distinct()
            .ToList();

        var plantillas = await contexto.PlantillaMensaje.AsNoTracking()
            .Where(p => p.Tipo == TipoMensajeEnvio.Factura && p.Activa)
            .ToListAsync(ct);

        // La plantilla del idioma pedido, o nada. Aquí no hay «si no existe, manda en español»: en un
        // lote eso mandaría a cientos de clientes un idioma que nadie pidió. Ver
        // MotivoExclusionEnvio.SinPlantilla.
        var plantilla = plantillas.FirstOrDefault(
            p => string.Equals(p.Idioma, idioma, StringComparison.OrdinalIgnoreCase));

        var clientes = clienteIds.Count == 0
            ? []
            : await contexto.Cliente.AsNoTracking()
                .Include(c => c.Contactos)
                .Where(c => clienteIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        var yaEnviados = await DestinatariosYaEnviadosAsync(documentoIds, peticion.Forzar, ct);

        var documentos = new List<EnvioMasivoDocumento>();
        var excluidos = new List<EnvioMasivoExcluido>();

        foreach (var fila in filas)
        {
            var referencia = new ReferenciaDocumento(fila.Id, fila.Tipo, fila.Numero?.ToString(), fila.Cliente);

            // El tope se comprueba antes que nada más: una vez lleno, el resto de filas del filtro
            // está fuera por eso y no tiene sentido inventarles otro motivo.
            if (documentos.Count >= TopeDocumentosPorLote)
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.SuperaElTopeDelLote,
                    $"El filtro trae más de {TopeDocumentosPorLote} documentos y el lote está lleno. "
                    + "Acota el filtro (por año, por cliente o por unidad) y confirma el resto."));
                continue;
            }

            // Una fianza es una fila de dinero en la vista (§3.13), no un papel: no hay PDF que mandar.
            if (fila.Tipo == TipoFilaCuenta.Fianza)
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.NoGeneraDocumento,
                    "Una fianza no genera documento: es un movimiento de dinero, no un papel que enviar."));
                continue;
            }

            if (fila.Estado == nameof(EstadoFactura.Anulada))
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.FacturaAnulada,
                    "La factura está anulada: mandar una factura anulada es mandar papel muerto."));
                continue;
            }

            if (!clientes.TryGetValue(fila.ClienteId, out var cliente))
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.SinDireccionCorreo,
                    "El cliente de la fila ya no existe, así que no hay a quién mandarle."));
                continue;
            }

            var eleccion = DestinatarioDe(cliente);

            if (eleccion.Motivo != MotivoExclusionEnvio.Ninguno)
            {
                excluidos.Add(Excluido(referencia, eleccion.Motivo, eleccion.Detalle));
                continue;
            }

            if (yaEnviados.Contains((fila.Id, eleccion.Email!)))
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.YaEnviado,
                    $"Ya se mandó a {eleccion.Email}. Con «forzar» entra igualmente."));
                continue;
            }

            if (plantilla is null)
            {
                excluidos.Add(Excluido(referencia, MotivoExclusionEnvio.SinPlantilla,
                    $"No hay plantilla activa del mensaje de factura en «{idioma}», así que no se sabe "
                    + "qué texto saldría. Si quieres que salga en «es», pídesela en ese idioma."));
                continue;
            }

            // El modelo del PDF (F4-01) es la misma fuente de la que sale el texto que se mandará, y
            // no se compone aquí: encolar mil PDFs en la pantalla de confirmación costaría el doble
            // que mandarlos. Lo compone el trabajo, en el mismo orden, con el mismo modelo.
            var modelo = await facturas.ModeloPdfAsync(fila.Id, ct);
            var (asunto, cuerpo) = VariablesMensaje.DeFactura(modelo).Sustituir(plantilla);

            documentos.Add(new EnvioMasivoDocumento(
                fila.Id,
                TipoDocumentoEnvio.Factura,
                modelo.Numero,
                fila.ClienteId,
                fila.Cliente,
                eleccion.Email!,
                eleccion.Nombre,
                asunto,
                cuerpo,
                $"{modelo.TipoDocumento.ToLowerInvariant()}-{ServicioEnvios.ParteDeArchivo(modelo.Numero)}.pdf",
                plantilla.Idioma));
        }

        return new CalculoLote(documentos, excluidos);
    }

    /// <summary>
    /// Las filas del filtro, todas. Se recorre la vista por páginas porque el filtro es el de §3.13 y
    /// esa vista está paginada: traer solo la primera página sería un lote que se calla lo que no
    /// vio.
    /// </summary>
    private async Task<IReadOnlyList<CuentaFila>> FilasDelFiltroAsync(FiltroCuentas filtro, CancellationToken ct)
    {
        var filas = new List<CuentaFila>();

        for (var pagina = 1; ; pagina++)
        {
            var respuesta = await cuentas.ListarAsync(
                new ConsultaLista(pagina, ConsultaLista.PageSizeMaximo, SortBy: null, ConsultaLista.OrdenAscendente),
                filtro.Anio,
                filtro.SerieId,
                filtro.Moneda,
                filtro.Estado,
                filtro.EstadoCobro,
                filtro.ClienteId,
                filtro.UnidadId,
                filtro.Vencidas,
                filtro.Modificadas,
                filtro.NoEnviadas,
                filtro.EnvioFallido,
                filtro.ModificadasTrasEnvio,
                filtro.IncluirProformas,
                filtro.IncluirFianzas,
                ct);

            filas.AddRange(respuesta.Data);

            if (filas.Count >= respuesta.Total || respuesta.Data.Count == 0)
            {
                return filas;
            }
        }
    }

    /// <summary>
    /// Los documentos del filtro que ya salieron a alguien, como pares de documento y destinatario.
    /// <para>
    /// El mismo antidiplicado de §3.14 que aplica el envío suelto (F4-02), con la misma excepción: un
    /// envío <c>Fallido</c> no bloquea nada, porque si bloqueara, reintentar un correo que falló por
    /// un problema del servidor obligaría a forzarlo cada vez. Se comparan los correos en minúsculas
    /// porque es como los guarda la columna, y es lo que evita que <c>Cliente@x.com</c> y
    /// <c>cliente@x.com</c> cuenten como dos destinatarios distintos.
    /// </para>
    /// </summary>
    private async Task<HashSet<(Guid DocumentoId, string Destinatario)>> DestinatariosYaEnviadosAsync(
        List<Guid> documentoIds,
        bool forzar,
        CancellationToken ct)
    {
        var yaEnviados = new HashSet<(Guid, string)>();

        if (forzar || documentoIds.Count == 0)
        {
            return yaEnviados;
        }

        var pares = await contexto.Envio.AsNoTracking()
            .Where(e => e.TipoDocumento == TipoDocumentoEnvio.Factura
                && e.DocumentoAnio == null
                && e.Estado != EstadoEnvio.Fallido
                && documentoIds.Contains(e.DocumentoId))
            .Select(e => new { e.DocumentoId, e.DestinatarioEmail })
            .ToListAsync(ct);

        foreach (var par in pares)
        {
            yaEnviados.Add((par.DocumentoId, par.DestinatarioEmail.Trim().ToLowerInvariant()));
        }

        return yaEnviados;
    }

    /// <summary>
    /// A quién sale el envío en masa, y por qué a veces no sale a nadie. La regla vive en
    /// <see cref="DestinatarioEnvio"/> porque los recordatorios de F4-05 eligen igual.
    /// </summary>
    private static DestinatarioEnvio.Eleccion DestinatarioDe(Cliente cliente) =>
        DestinatarioEnvio.Elegir(cliente);

    private static EnvioMasivoExcluido Excluido(
        ReferenciaDocumento referencia, MotivoExclusionEnvio motivo, string? detalle) =>
        new(
            referencia.Id,
            referencia.Tipo,
            referencia.Numero,
            referencia.Cliente,
            motivo,
            detalle ?? MotivoDe(motivo));

    private async Task<Dictionary<Guid, string>> NombresDeClienteAsync(
        IEnumerable<Guid?> clienteIds,
        CancellationToken ct)
    {
        var ids = clienteIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var nombres = await contexto.Cliente.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);

        return nombres;
    }

    private static string IdiomaDe(EnvioMasivoPeticion peticion) =>
        string.IsNullOrWhiteSpace(peticion.Idioma)
            ? IdiomaPorDefecto
            : peticion.Idioma.Trim().ToLowerInvariant();

    /// <summary>
    /// Segunda puerta del permiso, como en el resto de servicios: el endpoint lleva
    /// <c>[Accion]</c> y esto comprueba contra el usuario, que es lo único que hay detrás de una
    /// llamada sin sesión HTTP.
    /// </summary>
    private async Task ExigirAsync(Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.MensajeriaMasivo, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(Acciones.MensajeriaMasivo, "envio.masivo", ct);

        throw new PermisoDenegadoException(Acciones.MensajeriaMasivo, "envio.masivo");
    }

    // ---------------------------------------------------------------- modelos internos

    private sealed record CalculoLote(
        IReadOnlyList<EnvioMasivoDocumento> Documentos,
        IReadOnlyList<EnvioMasivoExcluido> Excluidos);

    /// <summary>Lo que identifica al documento excluido: lo mismo que se ve en la vista.</summary>
    private sealed record ReferenciaDocumento(Guid Id, TipoFilaCuenta Tipo, string? Numero, string? Cliente);

    /// <summary>
    /// El texto de un motivo cuando el caso no trae un texto propio. Existe para que ningún motivo
    /// devuelva un <c>detalle</c> vacío, que en pantalla sería un motivo sin explicación.
    /// </summary>
    private static string MotivoDe(MotivoExclusionEnvio motivo) => motivo switch
    {
        MotivoExclusionEnvio.YaEnviado => "Este documento ya se mandó a ese destinatario.",
        MotivoExclusionEnvio.SinDireccionCorreo => "No hay ninguna dirección de correo a la que mandarlo.",
        MotivoExclusionEnvio.SinContactoElegible => "El cliente no tiene un contacto elegible.",
        MotivoExclusionEnvio.FacturaAnulada => "La factura está anulada.",
        MotivoExclusionEnvio.SinPlantilla => "No hay plantilla del mensaje en ese idioma.",
        MotivoExclusionEnvio.NoGeneraDocumento => "Esta fila no genera un documento que se pueda mandar.",
        MotivoExclusionEnvio.SuperaElTopeDelLote => $"El lote está lleno: el tope es {TopeDocumentosPorLote} documentos.",
        _ => "No entra en el lote.",
    };
}