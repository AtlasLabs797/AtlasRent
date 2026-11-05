using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Catálogo de plantillas de contrato y de anexos, y congelación de la plantilla al aprobar
/// (docs/ESPECIFICACION.md §3.5).
/// <list type="bullet">
/// <item>
/// <b>Plantillas.</b> Se identifican por código, que es lo que guarda el contrato y lo que trae por
/// defecto el inmueble (F1-10). El código es único y no se reutiliza. Editar una plantilla no toca
/// los contratos ya aprobados: el texto de esos vive en su copia congelada.
/// </item>
/// <item>
/// <b>Anexos.</b> Catálogo con <b>uno activo por tipo</b>: mientras haya un anexo activo de un
/// tipo no entra otro, y para meter uno nuevo se desactiva el anterior. Desactivar no borra.
/// </item>
/// <item>
/// <b>La congelación.</b> <see cref="CongelarAsync"/> copia el texto de la plantilla y el de los
/// anexos que esta declara al contrato que se está aprobando, en la misma transacción que la
/// aprobación. A partir de ahí el PDF se compone desde la copia y el catálogo deja de importar para
/// ese contrato.
/// </item>
/// </list>
/// <para>
/// Nada se borra nunca (AGENTS.md §6): ni plantillas, ni anexos, ni copias congeladas. Desactivar es
/// poner un estado.
/// </para>
/// </summary>
public sealed class ServicioPlantillas(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCifrado cifrado,
    TimeProvider reloj)
{
    // ---------------------------------------------------------------- plantillas de contrato

    /// <summary>Catálogo de plantillas, para el selector del front.</summary>
    public Task<Pagina<PlantillaContratoRespuesta>> ListarContratosAsync(
        ConsultaLista paginacion,
        CancellationToken ct)
    {
        var consulta = contexto.PlantillaContrato.AsNoTracking();
        var ordenes = new Dictionary<string, Func<IQueryable<PlantillaContrato>, bool, IOrderedQueryable<PlantillaContrato>>>
        {
            ["codigo"] = (q, desc) => desc ? q.OrderByDescending(p => p.Codigo) : q.OrderBy(p => p.Codigo),
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(p => p.Nombre) : q.OrderBy(p => p.Nombre),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(p => p.CreatedAt) : q.OrderBy(p => p.CreatedAt),
        };

        return ConsultaPaginada.PaginarAsync(
            consulta, ordenes, "nombre", paginacion, ARespuestaContrato, ct);
    }

    public Task<PlantillaContratoRespuesta> LeerContratoAsync(Guid id, CancellationToken ct) =>
        RespuestaContratoAsync(contexto.PlantillaContrato.AsNoTracking(), id, ct);

    public async Task<PlantillaContratoRespuesta> CrearContratoAsync(
        PlantillaContratoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var codigo = ExigirTexto(peticion.Codigo, "Una plantilla necesita un código: es con el que la citan los contratos y el inmueble.");

        if (await contexto.PlantillaContrato.AnyAsync(p => p.Codigo == codigo, ct))
        {
            throw new InvalidOperationException(
                $"Ya hay una plantilla con el código '{codigo}'. El código identifica a la plantilla y "
                + "los contratos ya firmados lo citan, así que no se reutiliza: crea otra con otro código.");
        }

        var plantilla = new PlantillaContrato
        {
            Codigo = codigo,
            Nombre = ExigirTexto(peticion.Nombre, "Una plantilla necesita un nombre: es lo que ve el usuario al elegirla."),
            Secciones = Secciones(peticion.Secciones),
            AnexosTipos = AnexosTipos(peticion.AnexosTipos),
            Activa = true,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaContrato, plantilla,
            esAlta: true,
            accion: "plantilla_contrato.creada",
            entidad: "plantilla_contrato",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaContrato,
            datosAuditoria: DatosAuditoriaContrato,
            ct: ct);
    }

    /// <summary>
    /// Edita el texto de una plantilla. Un contrato aprobado no se entera: su texto está copiado.
    /// Un contrato en borrador tampoco cambia de golpe: sigue apuntando a este código, así que lo
    /// que sí le afecta es al aprobarse, que es cuando copia.
    /// </summary>
    public async Task<PlantillaContratoRespuesta> EditarContratoAsync(
        Guid id,
        PlantillaContratoEdicionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var plantilla = await CargarContratoAsync(id, ct);

        plantilla.Nombre = ExigirTexto(peticion.Nombre, "Una plantilla necesita un nombre: es lo que ve el usuario al elegirla.");
        if (peticion.Secciones is not null)
        {
            plantilla.Secciones = Secciones(peticion.Secciones);
        }

        if (peticion.AnexosTipos is not null)
        {
            plantilla.AnexosTipos = AnexosTipos(peticion.AnexosTipos);
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaContrato, plantilla,
            esAlta: false,
            accion: "plantilla_contrato.editada",
            entidad: "plantilla_contrato",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaContrato,
            datosAuditoria: DatosAuditoriaContrato,
            ct: ct);
    }

    /// <summary>
    /// Retira una plantilla del catálogo. Los contratos ya aprobados no cambian (tienen su copia) y
    /// los que se aprueben después no la podrán elegir.
    /// </summary>
    public async Task<PlantillaContratoRespuesta> DesactivarContratoAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var plantilla = await CargarContratoAsync(id, ct);
        ExigirActiva(plantilla.Activa, $"La plantilla '{plantilla.Codigo}' ya está desactivada.");

        plantilla.Activa = false;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaContrato, plantilla,
            esAlta: false,
            accion: "plantilla_contrato.desactivada",
            entidad: "plantilla_contrato",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaContrato,
            datosAuditoria: DatosAuditoriaContrato,
            ct: ct);
    }

    // ---------------------------------------------------------------- anexos

    /// <summary>Catálogo de anexos. Por defecto solo los activos, que es lo que se puede elegir.</summary>
    public Task<Pagina<PlantillaAnexoRespuesta>> ListarAnexosAsync(
        ConsultaLista paginacion,
        bool incluirInactivos,
        CancellationToken ct)
    {
        var consulta = contexto.PlantillaAnexo.AsNoTracking();
        if (!incluirInactivos)
        {
            consulta = consulta.Where(a => a.Activa);
        }

        var ordenes = new Dictionary<string, Func<IQueryable<PlantillaAnexo>, bool, IOrderedQueryable<PlantillaAnexo>>>
        {
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(a => a.Tipo) : q.OrderBy(a => a.Tipo),
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(a => a.Nombre) : q.OrderBy(a => a.Nombre),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt),
        };

        return ConsultaPaginada.PaginarAsync(consulta, ordenes, "tipo", paginacion, ARespuestaAnexo, ct);
    }

    public Task<PlantillaAnexoRespuesta> LeerAnexoAsync(Guid id, CancellationToken ct) =>
        RespuestaAnexoAsync(contexto.PlantillaAnexo.AsNoTracking(), id, ct);

    /// <summary>
    /// Alta de un anexo. Solo puede entrar uno activo por tipo (§3.5): el servicio avisa con un 409
    /// y, si aun así se colara por otro camino, el índice único parcial de <c>plantilla_anexo</c> lo
    /// rechaza igual.
    /// </summary>
    public async Task<PlantillaAnexoRespuesta> CrearAnexoAsync(
        PlantillaAnexoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        if (peticion.Tipo is not { } tipo)
        {
            throw new InvalidOperationException(
                "Un anexo necesita un tipo: el catálogo es de uno activo por tipo y sin tipo no se sabe a qué sustituye.");
        }

        if (await contexto.PlantillaAnexo.AnyAsync(a => a.Activa && a.Tipo == tipo, ct))
        {
            throw new InvalidOperationException(
                $"Ya hay un anexo activo de tipo '{tipo}'. El catálogo es de uno activo por tipo: "
                + "desactiva el anterior y crea este.");
        }

        var anexo = new PlantillaAnexo
        {
            Nombre = ExigirTexto(peticion.Nombre, "Un anexo necesita un nombre: es lo que lo separa de los demás."),
            Tipo = tipo,
            Secciones = Secciones(peticion.Secciones),
            Activa = true,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaAnexo, anexo,
            esAlta: true,
            accion: "plantilla_anexo.creado",
            entidad: "plantilla_anexo",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaAnexo,
            datosAuditoria: DatosAuditoriaAnexo,
            ct: ct);
    }

    /// <summary>
    /// Edita el texto de un anexo. Los contratos que ya lo tienen congelado no cambian, que es el
    /// motivo de que la congelación sea una copia y no una referencia.
    /// </summary>
    public async Task<PlantillaAnexoRespuesta> EditarAnexoAsync(
        Guid id,
        PlantillaAnexoEdicionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var anexo = await CargarAnexoAsync(id, ct);

        anexo.Nombre = ExigirTexto(peticion.Nombre, "Un anexo necesita un nombre: es lo que lo separa de los demás.");
        if (peticion.Secciones is not null)
        {
            anexo.Secciones = Secciones(peticion.Secciones);
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaAnexo, anexo,
            esAlta: false,
            accion: "plantilla_anexo.editado",
            entidad: "plantilla_anexo",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaAnexo,
            datosAuditoria: DatosAuditoriaAnexo,
            ct: ct);
    }

    /// <summary>
    /// Retira el anexo activo de su tipo. No lo borra: el catálogo queda con el histórico y, si
    /// hace falta, el siguiente anexo del mismo tipo entra sin más.
    /// </summary>
    public async Task<PlantillaAnexoRespuesta> DesactivarAnexoAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var anexo = await CargarAnexoAsync(id, ct);
        ExigirActiva(anexo.Activa, $"El anexo '{anexo.Nombre}' ya está desactivado.");

        anexo.Activa = false;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaAnexo, anexo,
            esAlta: false,
            accion: "plantilla_anexo.desactivado",
            entidad: "plantilla_anexo",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuestaAnexo,
            datosAuditoria: DatosAuditoriaAnexo,
            ct: ct);
    }

    // ---------------------------------------------------------------- congelación y PDF

    /// <summary>
    /// Copia la plantilla y sus anexos al contrato que se está aprobando (§3.5). La copia entra en
    /// el contexto y se guarda con la misma transacción que la aprobación, porque una aprobación sin
    /// copia congelada es un contrato aprobado que no sabe qué texto firmó.
    /// <para>
    /// Se llama desde <c>antesDeGuardar</c> de la aprobación, no desde el endpoint: la congelación
    /// no es un paso aparte del aprobar, es parte de él.
    /// </para>
    /// </summary>
    public async Task CongelarAsync(Contrato contrato, CancellationToken ct)
    {
        var plantilla = await contexto.PlantillaContrato.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Codigo == contrato.PlantillaContratoId, ct);

        if (plantilla is null)
        {
            throw new InvalidOperationException(
                $"La plantilla '{contrato.PlantillaContratoId}' no existe en el catálogo, así que el contrato "
                + "no se puede aprobar: hay que elegir una plantilla real.");
        }

        if (!plantilla.Activa)
        {
            throw new InvalidOperationException(
                $"La plantilla '{plantilla.Codigo}' está desactivada. Reactívala o elige otra: un contrato no "
                + "se aprueba con una plantilla que ya no está en el catálogo.");
        }

        // Solo se copian los anexos que la plantilla declara y que están activos. Los que no
        // existen o están desactivados no se inventan: el contrato sale con los que hay.
        var anexos = new List<PlantillaAnexo>();
        if (plantilla.AnexosTipos.Count > 0)
        {
            anexos = await contexto.PlantillaAnexo.AsNoTracking()
                .Where(a => a.Activa && plantilla.AnexosTipos.Contains(a.Tipo))
                .OrderBy(a => a.Tipo)
                .ToListAsync(ct);
        }

        contexto.ContratoPlantillaCongelada.Add(new ContratoPlantillaCongelada
        {
            ContratoId = contrato.Id,
            PlantillaId = plantilla.Id,
            Codigo = plantilla.Codigo,
            Nombre = plantilla.Nombre,
            Secciones = new List<SeccionTexto>(plantilla.Secciones),
            Anexos = AnexosCongelados(anexos),
            CongeladaAt = contrato.PlantillaCongeladaAt ?? reloj.GetUtcNow(),
        });
    }

    /// <summary>
    /// PDF del contrato (<c>docs/DESIGN.md §7</c>). Sale de la copia congelada, no del catálogo: si la
    /// plantilla se editó después de aprobar, el documento sigue con el texto con el que se firmó.
    /// </summary>
    public async Task<byte[]> PdfContratoAsync(Guid contratoId, CancellationToken ct)
    {
        var contrato = await contexto.Contrato.AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Unidad)
            .Include(c => c.Facturadora)
            .Include(c => c.PlantillaCongelada)
            .SingleOrDefaultAsync(c => c.Id == contratoId, ct)
            ?? throw new KeyNotFoundException($"No existe el contrato '{contratoId:D}'.");

        var congelada = contrato.PlantillaCongelada
            ?? throw new InvalidOperationException(
                "Este contrato no tiene plantilla congelada, y sin ella no hay documento: la plantilla se "
                + "congela al aprobar (§3.5).");

        // El documento de identidad se descifra para el PDF y solo para el PDF (D16).
        return DocumentoContratoPdf.Generar(
            contrato,
            congelada,
            contrato.Cliente?.Documento is { } documento ? cifrado.Descifrar(documento) : null);
    }

    // ---------------------------------------------------------------- internos

    private async Task<PlantillaContrato> CargarContratoAsync(Guid id, CancellationToken ct) =>
        await contexto.PlantillaContrato.SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe la plantilla '{id:D}'.");

    private async Task<PlantillaAnexo> CargarAnexoAsync(Guid id, CancellationToken ct) =>
        await contexto.PlantillaAnexo.SingleOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe el anexo '{id:D}'.");

    private static async Task<PlantillaContratoRespuesta> RespuestaContratoAsync(
        IQueryable<PlantillaContrato> consulta,
        Guid id,
        CancellationToken ct)
    {
        var plantilla = await consulta.SingleOrDefaultAsync(p => p.Id == id, ct);
        return plantilla is null
            ? throw new KeyNotFoundException($"No existe la plantilla '{id:D}'.")
            : ARespuestaContrato(plantilla);
    }

    private static async Task<PlantillaAnexoRespuesta> RespuestaAnexoAsync(
        IQueryable<PlantillaAnexo> consulta,
        Guid id,
        CancellationToken ct)
    {
        var anexo = await consulta.SingleOrDefaultAsync(a => a.Id == id, ct);
        return anexo is null
            ? throw new KeyNotFoundException($"No existe el anexo '{id:D}'.")
            : ARespuestaAnexo(anexo);
    }

    private static PlantillaContratoRespuesta ARespuestaContrato(PlantillaContrato plantilla) => new(
        plantilla.Id,
        plantilla.Codigo,
        plantilla.Nombre,
        plantilla.Secciones,
        plantilla.AnexosTipos,
        plantilla.Activa,
        plantilla.CreatedAt,
        plantilla.UpdatedAt);

    private static PlantillaAnexoRespuesta ARespuestaAnexo(PlantillaAnexo anexo) => new(
        anexo.Id,
        anexo.Tipo,
        anexo.Nombre,
        anexo.Secciones,
        anexo.Activa,
        anexo.CreatedAt,
        anexo.UpdatedAt);

    /// <summary>
    /// Auditoría de una plantilla: qué se guardó, no el texto. La auditoría se firma y va a una
    /// tabla que solo el gerente lee; volcar el contrato entero en ella no aporta nada y multiplica
    /// lo que hay que proteger (docs/DOC_TECNICA.md §6).
    /// </summary>
    private static object DatosAuditoriaContrato(PlantillaContrato plantilla) => new
    {
        plantilla.Codigo,
        plantilla.Nombre,
        plantilla.Activa,
        Secciones = plantilla.Secciones.Count,
        plantilla.AnexosTipos,
    };

    private static object DatosAuditoriaAnexo(PlantillaAnexo anexo) => new
    {
        anexo.Tipo,
        anexo.Nombre,
        anexo.Activa,
        Secciones = anexo.Secciones.Count,
    };

    /// <summary>
    /// Los bloques que llegan del front. Se limpian y se descartan los que se quedarían en blanco:
    /// un bloque sin texto no imprime nada y en el PDF se vería como un hueco.
    /// </summary>
    /// <summary>
    /// La copia de cada anexo con su propio texto. Si mañana se edita el anexo del catálogo, el
    /// contrato sigue con el que tenía el día de la firma.
    /// </summary>
    private static List<AnexoCongelado> AnexosCongelados(List<PlantillaAnexo> anexos)
    {
        var copiados = new List<AnexoCongelado>();
        foreach (var anexo in anexos)
        {
            copiados.Add(
                new AnexoCongelado(anexo.Tipo, anexo.Nombre, new List<SeccionTexto>(anexo.Secciones)));
        }

        return copiados;
    }

    private static List<SeccionTexto> Secciones(List<SeccionPeticion>? secciones) =>
        [.. (secciones ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Texto))
            .Select(s => new SeccionTexto(Limpiar(s.Titulo) ?? string.Empty, (s.Texto ?? string.Empty).Trim()))];

    /// <summary>Los tipos de anexo sin repetir: la plantilla declara qué necesita, no cuántas veces.</summary>
    private static List<TipoAnexo> AnexosTipos(List<TipoAnexo>? tipos) =>
        [.. (tipos ?? []).Distinct()];

    private static string ExigirTexto(string? valor, string mensaje) =>
        Limpiar(valor) ?? throw new InvalidOperationException(mensaje);

    private static void ExigirActiva(bool activa, string mensaje)
    {
        if (!activa)
        {
            throw new InvalidOperationException(mensaje);
        }
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}