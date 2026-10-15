using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Inventario de una unidad y del contrato que la ocupa (F2-08, docs/ESPECIFICACION.md §3.1, §3.5 y
/// §3.6).
/// <para>
/// Son <b>tres cosas y no una</b>, y el orden de las secciones de aquí es el mismo:
/// </para>
/// <list type="number">
/// <item>
/// <b>Inventario base de la unidad</b> (<see cref="InventarioBaseItem"/>): lo que hay en la unidad.
/// Opcional (§3.1) y sin fotos. Lo prepara el empleado y lo corrige mientras la unidad esté en el
/// mercado.
/// </item>
/// <item>
/// <b>Inventario del contrato</b> (<see cref="ContratoInventarioItem"/>): la <b>copia congelada</b>
/// del base en el momento de activar (§3.5). Copia y no referencia, para que corregir el catálogo
/// mañana no cambie lo que un contrato ya firmado tiene por suyo. Solo la escribe
/// <see cref="CongelarAsync"/>, y solo desde la activación.
/// </item>
/// <item>
/// <b>Revisión de salida</b> (<see cref="RevisionInventario"/>): lo que se comprueba cuando el cliente
/// se va (§3.6). Apunta al contrato, nace firmada y no se vuelve a escribir. No borra el inventario
/// del contrato: lo necesita como la contra que se firmó.
/// </item>
/// </list>
/// <para>
/// <b>Nada de esto se borra.</b> Un elemento que sobra del inventario base se desactiva, como las
/// unidades y las plantillas; la copia congelada no tiene ni endpoint ni marca de actualizado; y una
/// revisión firmada se vuelve a firmar por 409, que es lo mismo que hace un incremento ya aplicado
/// (F2-06).
/// </para>
/// </summary>
public sealed class ServicioInventario(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    TimeProvider reloj)
{
    // ---------------------------------------------------------------- 1. inventario base de la unidad

    /// <summary>
    /// El inventario base de la unidad, activo primero y en el orden en que lo escribió el empleado.
    /// Vacío es una respuesta válida: §3.1 dice que el inventario base es opcional.
    /// </summary>
    public async Task<List<InventarioBaseItemRespuesta>> ListarBaseAsync(
        Guid unidadId,
        CancellationToken ct) =>
        await contexto.InventarioBaseItem
            .AsNoTracking()
            .Where(i => i.UnidadId == unidadId && i.Activo)
            .OrderBy(i => i.Orden)
            .ThenBy(i => i.Nombre)
            .Select(i => new InventarioBaseItemRespuesta(
                i.Id, i.UnidadId, i.Nombre, i.Cantidad, i.Notas, i.Orden, i.Activo, i.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Añade un elemento al inventario base de la unidad. Lo escribe el empleado con
    /// <c>cliente.editar</c>, que en la matriz §2 es la fila donde están las unidades y las reservas,
    /// y es el mismo permiso con el que prepara el contrato que va a usar esa unidad.
    /// </summary>
    public async Task<InventarioBaseItemRespuesta> CrearBaseAsync(
        Guid unidadId,
        InventarioBaseItemPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirPermisoAsync(Acciones.ClienteEditar, $"unidad/{unidadId:D}/inventario-base", usuario, ct);
        await ExigirUnidadAsync(unidadId, ct);

        var ahora = reloj.GetUtcNow();
        var siguiente = await contexto.InventarioBaseItem
            .Where(i => i.UnidadId == unidadId)
            .Select(i => (int?)i.Orden)
            .MaxAsync(ct);

        var item = new InventarioBaseItem
        {
            CreatedAt = ahora,
            CreatedBy = usuario,
            UnidadId = unidadId,
            Nombre = Limpiar(peticion.Nombre)!,
            Cantidad = Redondeo.Importe(peticion.Cantidad!.Value),
            Notas = Limpiar(peticion.Notas),
            Orden = siguiente is { } maximo ? maximo + 1 : 0,
            Activo = true,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.InventarioBaseItem, item,
            esAlta: true,
            accion: "unidad.inventario_base_creado",
            entidad: "inventario_base_item",
            usuario,
            ip,
            mutacion,
            proyectar: ABase,
            datosAuditoria: DatosAuditoriaBase,
            ct: ct);
    }

    /// <summary>
    /// Corrige un elemento del inventario base. Esto <b>no</b> toca el inventario de los contratos ya
    /// activos: esos tienen su propia copia congelada y no se reescriben desde aquí, que es justo lo
    /// que un contrato firmado necesita.
    /// </summary>
    public async Task<InventarioBaseItemRespuesta> EditarBaseAsync(
        Guid unidadId,
        Guid itemId,
        InventarioBaseItemPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirPermisoAsync(Acciones.ClienteEditar, $"unidad/{unidadId:D}/inventario-base", usuario, ct);
        var item = await CargarBaseAsync(unidadId, itemId, ct);

        item.Nombre = Limpiar(peticion.Nombre)!;
        item.Cantidad = Redondeo.Importe(peticion.Cantidad!.Value);
        item.Notas = Limpiar(peticion.Notas);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.InventarioBaseItem, item,
            esAlta: false,
            accion: "unidad.inventario_base_editado",
            entidad: "inventario_base_item",
            usuario,
            ip,
            mutacion,
            proyectar: ABase,
            datosAuditoria: DatosAuditoriaBase,
            ct: ct);
    }

    /// <summary>
    /// Retira un elemento del inventario base. La fila se queda con <c>Activo = false</c>: no hay
    /// <c>DELETE</c> en ninguna de las tres tablas de este servicio (AGENTS.md §6).
    /// </summary>
    public async Task<InventarioBaseItemRespuesta> DesactivarBaseAsync(
        Guid unidadId,
        Guid itemId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirPermisoAsync(Acciones.ClienteEditar, $"unidad/{unidadId:D}/inventario-base", usuario, ct);
        var item = await CargarBaseAsync(unidadId, itemId, ct);

        item.Activo = false;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.InventarioBaseItem, item,
            esAlta: false,
            accion: "unidad.inventario_base_desactivado",
            entidad: "inventario_base_item",
            usuario,
            ip,
            mutacion,
            proyectar: ABase,
            datosAuditoria: DatosAuditoriaBase,
            ct: ct);
    }

    // ---------------------------------------------------------------- 2. inventario congelado del contrato

    /// <summary>
    /// La copia congelada del inventario base, tal y como quedó el día del activar. Solo lectura:
    /// no hay ningún método que la escriba después de <see cref="CongelarAsync"/>.
    /// </summary>
    public async Task<List<ContratoInventarioItemRespuesta>> ListarContratoAsync(
        Guid contratoId,
        CancellationToken ct) =>
        await contexto.ContratoInventarioItem
            .AsNoTracking()
            .Where(i => i.ContratoId == contratoId)
            .OrderBy(i => i.Orden)
            .ThenBy(i => i.Nombre)
            .Select(i => new ContratoInventarioItemRespuesta(
                i.Id, i.InventarioBaseId, i.Nombre, i.Cantidad, i.Notas, i.Orden))
            .ToListAsync(ct);

    /// <summary>
    /// Copia el inventario base de la unidad al contrato que se está activando y lo congela (§3.5:
    /// "el inventario se copia y congela al activar").
    /// <para>
    /// Se llama desde <c>antesDeGuardar</c> de la activación, no desde el endpoint: la copia no es un
    /// paso aparte del activar, es parte de él, y por eso el cambio de estado, el candado de la
    /// unidad y el inventario salen en un solo commit o no sale ninguno. Mismo patrón que la plantilla
    /// congelada de F2-07.
    /// </para>
    /// <para>
    /// <b>Una unidad sin inventario base activa igual</b>: §3.1 dice que el inventario base es
    /// opcional, así que aquí no hay copia y no hay nada que congelar. El contrato sale sin
    /// inventario, que es una unidad sin inventario, y no un fallo.
    /// </para>
    /// </summary>
    public async Task CongelarAsync(Contrato contrato, CancellationToken ct)
    {
        var baseItems = await contexto.InventarioBaseItem
            .AsNoTracking()
            .Where(i => i.UnidadId == contrato.UnidadId && i.Activo)
            .OrderBy(i => i.Orden)
            .ToListAsync(ct);

        if (baseItems.Count == 0)
        {
            return;
        }

        // Red de seguridad, no camino normal: activar exige estar en "aprobado" y de ahí solo se pasa
        // a "activo". Si algún día existiera un camino que reescribiera la copia, eso no sería una
        // congelación sino un cambio de un contrato firmado, y tiene que notarse en el 409.
        if (await contexto.ContratoInventarioItem.AnyAsync(i => i.ContratoId == contrato.Id, ct))
        {
            throw new InvalidOperationException(
                $"Este contrato ya tiene su inventario congelado. Congelarlo otra vez reescribiría lo que "
                + "el cliente firmó al entrar, y eso no es una edición: es otro contrato.");
        }

        // Copia de valores, no de referencias: los campos viajan dentro de la fila para que editar el
        // catálogo mañana no cambie lo que este contrato tiene por suyo.
        contexto.ContratoInventarioItem.AddRange(baseItems.Select(i => new ContratoInventarioItem
        {
            Id = Guid.NewGuid(),
            ContratoId = contrato.Id,
            InventarioBaseId = i.Id,
            Nombre = i.Nombre,
            Cantidad = i.Cantidad,
            Notas = i.Notas,
            Orden = i.Orden,
        }));

        contrato.InventarioCongeladoAt = reloj.GetUtcNow();
    }

    // ---------------------------------------------------------------- 3. revisión de salida

    /// <summary>
    /// La revisión de salida firmada. 404 si el contrato no tiene ninguna, que es lo normal mientras
    /// el cliente todavía no se ha ido.
    /// </summary>
    public async Task<RevisionInventarioRespuesta> LeerRevisionAsync(
        Guid contratoId,
        CancellationToken ct)
    {
        var revision = await contexto.RevisionInventario
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.ContratoId == contratoId, ct)
            ?? throw new KeyNotFoundException(
                $"El contrato '{contratoId:D}' no tiene revisión de inventario firmada todavía.");

        var congelado = await InventarioCongeladoAsync(contratoId, ct);

        return ARevision(revision, congelado);
    }

    /// <summary>
    /// Firma la revisión de salida (§3.6). Exige <c>contrato.estado</c>: gerente o administrador,
    /// nunca el empleado, porque la firma de esta revisión es la que desbloquea la liquidación de
    /// F2-09 y la que va al cliente.
    /// <para>
    /// <b>Se firma una sola vez.</b> La fila nace firmada y no hay camino para volver a escribirla: el
    /// índice único sobre <c>contrato_id</c> es la red en la base y el 409 del servicio es el motivo
    /// que se lee. Igual que el incremento ya aplicado de F2-06: eso ya pasó, se documenta.
    /// </para>
    /// <para>
    /// <b>No borra nada.</b> El inventario congelado del contrato se conserva tal cual: es la contra
    /// que el cliente firmó al entrar y contra la que se firma esto.
    /// </para>
    /// </summary>
    public async Task<RevisionInventarioRespuesta> FirmarRevisionAsync(
        Guid contratoId,
        RevisionInventarioPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirPermisoAsync(Acciones.ContratoEstado, $"contrato/{contratoId:D}", usuario, ct);

        var contrato = await CargarContratoAsync(contratoId, ct);
        ExigirRevisable(contrato);

        // Ya firmada. No es que se pueda cambiar: es que ya ocurrió.
        if (await contexto.RevisionInventario.AnyAsync(r => r.ContratoId == contratoId, ct))
        {
            throw new InvalidOperationException(
                "Este contrato ya tiene la revisión de inventario firmada. Firmar es un hecho histórico: "
                + "la revisión dice qué se vio el día de la salida y no se vuelve a escribir.");
        }

        var congelado = await InventarioCongeladoAsync(contratoId, ct);

        if (congelado.Count == 0)
        {
            throw new InvalidOperationException(
                "Este contrato no tiene inventario congelado, así que no hay nada que revisar. Es lo "
                + "normal en una unidad sin inventario base (§3.1 lo hace opcional).");
        }

        var lineas = ValidarLineas(peticion, congelado);

        var ahora = reloj.GetUtcNow();
        var revision = new RevisionInventario
        {
            CreatedAt = ahora,
            CreatedBy = usuario,
            ContratoId = contratoId,
            FirmadaAt = ahora,
            FirmadaPorId = usuario,
            ImportePropuesto = peticion.ImportePropuesto is { } importe ? Redondeo.Importe(importe) : null,
            Observacion = Limpiar(peticion.Observacion),
            Items = [.. lineas.Select(l => new RevisionInventarioItem
            {
                Id = Guid.NewGuid(),
                CreatedAt = ahora,
                CreatedBy = usuario,
                ContratoInventarioItemId = l.ContratoInventarioItemId!.Value,
                Estado = l.Estado!.Value,
                Nota = Limpiar(l.Nota),
            })],
        };

        var resultado = await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.RevisionInventario, revision,
            esAlta: true,
            accion: "contrato.revision_inventario_firmada",
            entidad: "revision_inventario",
            usuario,
            ip,
            mutacion,
            proyectar: r => ARevision(r, congelado),
            datosAuditoria: DatosAuditoriaRevision,
            antesDeGuardar: () =>
            {
                // GuardadoAuditado asigna el Id de la fila justo antes de guardar, así que las líneas
                // solo pueden apuntar a la revisión dentro de este hook, que corre antes del
                // SaveChanges. Es el mismo punto donde el contrato mueve su candado (F2-01).
                foreach (var item in revision.Items)
                {
                    item.RevisionId = revision.Id;
                }

                return Task.CompletedTask;
            },
            ct: ct);

        return resultado;
    }

    /// <summary>
    /// La condición que F2-09 tiene que exigir antes de liquidar: <b>un contrato con inventario de
    /// contrato no se liquida sin revisión firmada</b> (F2-08 deja la condición lista; la liquidación
    /// es de F2-09).
    /// <para>
    /// Un contrato sin inventario congelado no exige nada: §3.6 pide la revisión "si el contrato
    /// tiene inventario", y §3.1 hace que el inventario base sea opcional. Exigirla siempre dejaría
    /// sin poder liquidar a las unidades que no lo tienen, que son la mayoría.
    /// </para>
    /// </summary>
    public async Task ExigirRevisionFirmadaAsync(Guid contratoId, CancellationToken ct)
    {
        var tieneInventario = await contexto.ContratoInventarioItem
            .AnyAsync(i => i.ContratoId == contratoId, ct);

        if (!tieneInventario)
        {
            return;
        }

        var firmada = await contexto.RevisionInventario
            .AsNoTracking()
            .AnyAsync(r => r.ContratoId == contratoId, ct);

        if (!firmada)
        {
            throw new InvalidOperationException(
                "Este contrato tiene inventario y todavía no se ha firmado la revisión de salida. No se "
                + "puede liquidar sin ella: es la que dice qué falta y por cuánto.");
        }
    }

    // ---------------------------------------------------------------- reglas

    /// <summary>
    /// El permiso, comprobado en el servicio y no solo en el atributo, por la misma razón que en
    /// <c>ServicioContratos</c> y <c>ServicioIncrementos</c>: el atributo protege la entrada por HTTP
    /// y nada más.
    /// </summary>
    private async Task ExigirPermisoAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    private async Task ExigirUnidadAsync(Guid unidadId, CancellationToken ct)
    {
        var existe = await contexto.Unidad
            .AsNoTracking()
            .AnyAsync(u => u.Id == unidadId && u.Activo, ct);

        if (!existe)
        {
            throw new KeyNotFoundException($"No existe la unidad activa '{unidadId:D}'.");
        }
    }

    private async Task<InventarioBaseItem> CargarBaseAsync(Guid unidadId, Guid itemId, CancellationToken ct) =>
        await contexto.InventarioBaseItem
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UnidadId == unidadId, ct)
        ?? throw new KeyNotFoundException(
            $"La unidad '{unidadId:D}' no tiene el elemento de inventario '{itemId:D}'.");

    private async Task<Contrato> CargarContratoAsync(Guid contratoId, CancellationToken ct) =>
        await contexto.Contrato
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contratoId, ct)
        ?? throw new KeyNotFoundException($"No existe el contrato '{contratoId:D}'.");

    /// <summary>
    /// La salida se revisa con el contrato vigente <b>y también con el ya finalizado</b>.
    /// <para>
    /// Antes se exigía <c>Activo</c> o <c>BajaProgramada</c> y en <c>Finalizado</c> se rechazaba, y
    /// eso era un callejón sin salida: con el fin por fecha efectiva de F2-09 el contrato pasa a
    /// <c>Finalizado</c> solo, el día de la fecha efectiva, y la revisión se firma ese mismo día. Prohibir
    /// la firma en <c>Finalizado</c> hacía que un contrato finalizado sin firmar no se pudiera firmar
    /// nunca y, por lo tanto, tampoco se pudiera liquidar nunca: <c>ExigirRevisionFirmadaAsync</c>
    /// bloquea la liquidación con inventario, así que el rechazo convertía un olvidedo en un contrato
    /// que se queda abierto para siempre. La instrucción anterior era mía y estaba mal.
    /// </para>
    /// <para>
    /// Lo que no se puede es firmar antes de que el cliente entre ni después de liquidar: en
    /// <c>Borrador</c>, <c>PendienteAprobacion</c> y <c>Aprobado</c> el inquilino todavía no ha entrado
    /// y no hay nada que revisar; en <c>Liquidado</c> el cierre está hecho y firmarlo sería escribir el
    /// documento histórico a posteriori, con la decisión de fianza ya tomada.
    /// </para>
    /// </summary>
    private static void ExigirRevisable(Contrato contrato)
    {
        if (contrato.Estado is not (EstadoContrato.Activo
            or EstadoContrato.BajaProgramada
            or EstadoContrato.Finalizado))
        {
            throw new InvalidOperationException(
                $"Un contrato en estado {Estado(contrato.Estado)} no tiene salida que revisar. La revisión de "
                + "inventario se firma con el contrato vigente, con la baja ya programada o con el ya "
                + "finalizado, que es cuando el cliente se va.");
        }
    }

    /// <summary>
    /// La revisión dice el estado de <b>cada</b> elemento del inventario congelado (§3.6), así que las
    /// líneas tienen que ser exactamente las de la copia: ni una de menos (un elemento sin revisar es
    /// un elemento que no se comprobó) ni una de más (un elemento que no está en el contrato no se
    /// puede liquidar contra él).
    /// </summary>
    private static List<RevisionInventarioLineaPeticion> ValidarLineas(
        RevisionInventarioPeticion peticion,
        List<ContratoInventarioItem> congelado)
    {
        var lineas = peticion.Lineas!;
        var esperados = congelado.Select(i => i.Id).ToHashSet();

        var repetidos = lineas
            .GroupBy(l => l.ContratoInventarioItemId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (repetidos.Count > 0)
        {
            throw new InvalidOperationException(
                $"La revisión repite {repetidos.Count} elemento(s) del inventario. Cada elemento se revisa una vez.");
        }

        var incluidos = lineas.Select(l => l.ContratoInventarioItemId!.Value).ToHashSet();
        if (!incluidos.SetEquals(esperados))
        {
            var sinRevisar = esperados.Except(incluidos).Count();
            var ajenos = incluidos.Except(esperados).Count();
            throw new InvalidOperationException(
                $"La revisión tiene que tener una línea por cada elemento del inventario congelado: "
                + $"faltan {sinRevisar} y sobran {ajenos}. Revisar solo lo que falta deja sin comprobar lo demás.");
        }

        return lineas;
    }

    private async Task<List<ContratoInventarioItem>> InventarioCongeladoAsync(
        Guid contratoId,
        CancellationToken ct) =>
        await contexto.ContratoInventarioItem
            .AsNoTracking()
            .Where(i => i.ContratoId == contratoId)
            .OrderBy(i => i.Orden)
            .ToListAsync(ct);

    private static RevisionInventarioRespuesta ARevision(
        RevisionInventario revision,
        List<ContratoInventarioItem> congelado)
    {
        var porId = congelado.ToDictionary(i => i.Id);
        var posiciones = congelado
            .Select((elemento, posicion) => (elemento.Id, posicion))
            .ToDictionary(p => p.Id, p => p.posicion);

        return new(
            revision.Id,
            revision.ContratoId,
            revision.FirmadaAt,
            revision.FirmadaPorId,
            revision.ImportePropuesto,
            revision.Observacion,
            // Las líneas salen en el orden del inventario congelado, que es el orden del documento que
            // firmó el cliente, no en el que coincided el alta con el id.
            [.. revision.Items
                .OrderBy(i => posiciones.GetValueOrDefault(i.ContratoInventarioItemId, int.MaxValue))
                .Select(i =>
                {
                    var elemento = porId.GetValueOrDefault(i.ContratoInventarioItemId);
                    return new RevisionInventarioLineaRespuesta(
                        i.Id,
                        i.ContratoInventarioItemId,
                        elemento?.Nombre ?? string.Empty,
                        elemento?.Cantidad ?? 0m,
                        i.Estado,
                        i.Nota);
                })]);
    }

    private static InventarioBaseItemRespuesta ABase(InventarioBaseItem item) =>
        new(item.Id, item.UnidadId, item.Nombre, item.Cantidad, item.Notas, item.Orden, item.Activo, item.CreatedAt);

    private static object DatosAuditoriaBase(InventarioBaseItem item) => new
    {
        item.UnidadId,
        item.Nombre,
        item.Cantidad,
        item.Notas,
        item.Orden,
        item.Activo,
    };

    private static object DatosAuditoriaRevision(RevisionInventario revision) => new
    {
        revision.ContratoId,
        revision.FirmadaAt,
        revision.FirmadaPorId,
        revision.ImportePropuesto,
        revision.Observacion,
        Lineas = revision.Items.Count,
        Ausentes = revision.Items.Count(i => i.Estado == EstadoRevisionInventario.Ausente),
        Danados = revision.Items.Count(i => i.Estado == EstadoRevisionInventario.Danado),
    };

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>El nombre del estado para los mensajes de la API, en el idioma de quien la lee.</summary>
    private static string Estado(EstadoContrato estado) => estado switch
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