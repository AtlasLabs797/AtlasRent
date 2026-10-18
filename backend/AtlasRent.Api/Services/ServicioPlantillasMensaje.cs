using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// CRUD de las plantillas de correo por tipo de mensaje e idioma (F4-02,
/// docs/ESPECIFICACION.md §3.14). <b>Solo el administrador</b> las edita: es el permiso
/// <c>plantilla.editar</c> de la matriz de §2, y el que escribe lo que le llega al cliente no es
/// el mismo rol que el que registra cobros.
/// <para>
/// El texto va en <b>texto plano</b>. No hay forma de escribir HTML ni de meter entidades: si un
/// nombre trae una etiqueta, sale con la etiqueta al lado en el correo, que es lo mismo que pasa
/// con los PDF (SEC-001, docs/SEGURIDAD.md) y por el mismo motivo.
/// </para>
/// <para>
/// <b>Nada se borra</b>, ni siquiera una plantilla que ya no se quiere: se desactiva. Un envío hecho
/// con una plantilla concreta dice qué texto encontró al salir, y si la plantilla desaparece ese
/// envío queda sin explicación.
/// </para>
/// </summary>
public sealed class ServicioPlantillasMensaje(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    TimeProvider reloj)
{
    /// <summary>Listado, opcionalmente por tipo de mensaje.</summary>
    public async Task<Pagina<PlantillaMensajeRespuesta>> ListarAsync(
        ConsultaLista paginacion,
        TipoMensajeEnvio? tipo,
        CancellationToken ct)
    {
        var consulta = contexto.PlantillaMensaje.AsNoTracking()
            .Where(p => !tipo.HasValue || p.Tipo == tipo);

        return await ConsultaPaginada.PaginarAsync(
            consulta, Ordenes, "tipo", paginacion, ARespuesta, ct);
    }

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<PlantillaMensaje>, bool, IOrderedQueryable<PlantillaMensaje>>> Ordenes =
        new Dictionary<string, Func<IQueryable<PlantillaMensaje>, bool, IOrderedQueryable<PlantillaMensaje>>>
        {
            ["tipo"] = (q, desc) => desc
                ? q.OrderByDescending(p => p.Tipo).ThenByDescending(p => p.Idioma)
                : q.OrderBy(p => p.Tipo).ThenBy(p => p.Idioma),
            ["idioma"] = (q, desc) => desc
                ? q.OrderByDescending(p => p.Idioma).ThenBy(p => p.Tipo)
                : q.OrderBy(p => p.Idioma).ThenBy(p => p.Tipo),
        };

    public async Task<PlantillaMensajeRespuesta> GuardarAsync(
        PlantillaMensajePeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.PlantillaEditar, $"plantilla_mensaje/{peticion.Tipo}/{peticion.Idioma}", usuario, ct);

        var idioma = peticion.Idioma.Trim().ToLowerInvariant();

        var actual = await contexto.PlantillaMensaje.FirstOrDefaultAsync(
            p => p.Tipo == peticion.Tipo && p.Idioma == idioma, ct);

        if (actual is null)
        {
            var alta = new PlantillaMensaje
            {
                Tipo = peticion.Tipo,
                Idioma = idioma,
                Asunto = peticion.Asunto.Trim(),
                Cuerpo = peticion.Cuerpo,
                Activa = peticion.Activa,
            };

            return await GuardadoAuditado.GuardarAsync(
                contexto, auditoria, idempotencia, reloj,
                contexto.PlantillaMensaje, alta,
                esAlta: true,
                accion: "plantilla_mensaje.creada",
                entidad: "plantilla_mensaje",
                usuario,
                ip,
                mutacion,
                proyectar: ARespuesta,
                datosAuditoria: DatosAuditoria,
                ct: ct);
        }

        actual.Asunto = peticion.Asunto.Trim();
        actual.Cuerpo = peticion.Cuerpo;
        actual.Activa = peticion.Activa;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.PlantillaMensaje, actual,
            esAlta: false,
            accion: "plantilla_mensaje.editada",
            entidad: "plantilla_mensaje",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    private static PlantillaMensajeRespuesta ARespuesta(PlantillaMensaje p) => new(
        p.Id,
        p.Tipo,
        p.Idioma,
        p.Asunto,
        p.Cuerpo,
        p.Activa,
        p.CreatedAt,
        p.UpdatedAt);

    /// <summary>
    /// Lo que va a la auditoría. El asunto sí (es corto y es lo que más se cambia); el cuerpo va
    /// entero también porque es texto escrito por el administrador, no dato de cliente, y la
    /// auditoría es la forma de saber qué decía la plantilla cuando salió un envío.
    /// </summary>
    private static object DatosAuditoria(PlantillaMensaje p) => new
    {
        tipo = p.Tipo.ToString(),
        idioma = p.Idioma,
        asunto = p.Asunto,
        activa = p.Activa,
    };
}