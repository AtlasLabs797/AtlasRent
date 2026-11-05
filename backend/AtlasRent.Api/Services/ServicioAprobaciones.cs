using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// La bandeja de aprobaciones de §3.16 (F2-10): los contratos que están esperando que alguien con
/// <c>contrato.estado</c> decida.
/// <para>
/// <b>F6-03: la bandeja se queda con los contratos y el resto de tipos traen su propia lista</b>
/// (<c>ServicioPropuestasAgente.ListarAsync</c>), porque las columnas que la pide —renta vigente,
/// estado del contrato— no existen para una propuesta de recordatorio. Lo que sí es común, y por eso
/// vive aquí, es <b>quién decide</b>: <see cref="AprobarAsync"/> y <see cref="RechazarAsync"/> miran
/// el <see cref="TipoSolicitud.Tipo"/> de la fila y la llevan al servicio que corresponde, de modo que
/// un contrato se aprueba con las reglas de §3.5 y una propuesta con las de F6-03 sin que haya dos
/// caminos para la misma cosa.
/// </para>
/// <para>
/// <b>Solo lecturas, y solo de lo que vive.</b> Ni aprobar ni rechazar por su cuenta: el servicio
/// reparte y quien decide es el servicio que sabe. Un segundo camino de aprobación sería dos reglas
/// de negocio y un sitio más donde se puede saltar la protección.
/// </para>
/// <para>
/// <b>Solo solicitudes vivas.</b> Las caducadas y las resueltas <i>no se borran</i>: son el
/// historial de por qué un contrato volvió a enviarse y se siguen viendo en la ficha del contrato.
/// Por eso el filtro es <c>Resultado == Pendiente</c> y además el contrato en
/// <c>PendienteAprobacion</c>: una solicitud huérfana que quedara pendiente porque alguien cambió el
/// estado del contrato a mano no puede aparecer en una bandeja desde la que se apruebe.
/// </para>
/// <para>
/// <b>Solo quien puede aprobar.</b> Se exige <c>contrato.estado</c> aquí también, no solo en el
/// atributo del endpoint, por lo mismo que el resto de los servicios de F2-05: el atributo protege la
/// entrada por HTTP y este método protege la lista. Y la denegación se audita.
/// </para>
/// </summary>
public sealed class ServicioAprobaciones(
    AtlasRentDbContext contexto,
    IPolicyService politica,
    ServicioContratos contratos,
    ServicioPropuestasAgente propuestas)
{
    private const string RecursoDeLaBandeja = "bandeja-aprobaciones";

    private static readonly Dictionary<string, Func<IQueryable<SolicitudAprobacion>, bool, IOrderedQueryable<SolicitudAprobacion>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["solicitadoAt"] = (q, desc) =>
                desc ? q.OrderByDescending(s => s.SolicitadoAt) : q.OrderBy(s => s.SolicitadoAt),
            ["fechaFin"] = (q, desc) =>
                desc
                    ? q.OrderByDescending(s => s.Contrato!.FechaFinVigente)
                    : q.OrderBy(s => s.Contrato!.FechaFinVigente),
            ["renta"] = (q, desc) =>
                desc ? q.OrderByDescending(s => s.Contrato!.Renta) : q.OrderBy(s => s.Contrato!.Renta),
        };

    /// <summary>
    /// Fila de la bandeja. <paramref name="usuario"/> es el de la petición: se comprueba su techo de
    /// permisos antes de leer nada, para que un empleado reciba 403 en vez de una lista vacía que
    /// parece "no hay nada que aprobar".
    /// </summary>
    public async Task<Pagina<AprobacionBandejaRespuesta>> ListarAsync(
        ConsultaLista paginacion,
        Guid usuario,
        CancellationToken ct)
    {
        await ExigirBandejaAsync(usuario, ct);

        var consulta = contexto.SolicitudAprobacion.AsNoTracking()
            .Include(s => s.Contrato).ThenInclude(c => c!.Cliente)
            .Include(s => s.Contrato).ThenInclude(c => c!.Unidad)
            .Where(s => s.Resultado == ResultadoSolicitud.Pendiente
                && s.Tipo == TipoSolicitud.Contrato
                && s.Contrato!.Estado == EstadoContrato.PendienteAprobacion);

        // El nombre de quien pidió la solicitud y la renta vigente del contrato no salen de la
        // navegación de la fila: `solicitud_aprobacion` no tiene relación con `usuario` ni con
        // `contrato_version`. Se leen aparte y se cruzan en memoria, que son unos pocos identificadores
        // por página. La alternativa —desnormalizar solicitante y renta en la solicitud— sería
        // guardar dos copias de un dato que ya existe y que se desactualizarían solas.
        var ids = await consulta
            .Select(s => s.SolicitadoPorId)
            .Where(id => id != null)
            .Distinct()
            .ToListAsync(ct);

        var solicitantes = await contexto.Usuario.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nombre })
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);

        var vigentes = await RentaVigentePorContratoAsync(consulta.Select(s => s.ContratoId!.Value), ct);

        return await ConsultaPaginada.PaginarAsync(
            consulta,
            Ordenes,
            "solicitadoAt",
            paginacion,
            s => Fila(s, solicitantes, vigentes),
            ct);
    }

    /// <summary>
    /// Decide la solicitud que sea, según lo que sea (F6-03). Un contrato va a
    /// <see cref="ServicioContratos.AprobarAsync"/>, que es la misma llamada que hace el botón de su
    /// ficha; una propuesta va a <see cref="ServicioPropuestasAgente.AprobarAsync"/>, que ejecuta el
    /// aviso. El permiso, la caducidad y la auditoría los exige cada servicio por su cuenta, que es
    /// donde están sus reglas: aquí solo se decide a quién le toca.
    /// <para>
    /// <b>Devuelve la respuesta del servicio que se haya usado, no una mezcla de las dos.</b> Contrato
    /// y propuesta no comparten DTO ni reglas, y que un día haya un tercer tipo no cambia nada de esto.
    /// </para>
    /// </summary>
    public async Task<object> AprobarAsync(
        Guid solicitudId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var solicitud = await LeerAsync(solicitudId, ct);

        return solicitud.Tipo switch
        {
            TipoSolicitud.Contrato
                => await contratos.AprobarAsync(solicitud.ContratoId!.Value, usuario, ip, mutacion, ct, solicitudId),
            TipoSolicitud.AccionAgente
                => await propuestas.AprobarAsync(solicitud.Id, usuario, ip, mutacion, ct),
            _ => throw new InvalidOperationException($"No hay servicio que decida una solicitud de tipo '{solicitud.Tipo}'."),
        };
    }

    /// <summary>Lo mismo que <see cref="AprobarAsync"/> para el rechazo, con el comentario de §3.5.</summary>
    public async Task<object> RechazarAsync(
        Guid solicitudId,
        string comentario,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var solicitud = await LeerAsync(solicitudId, ct);

        return solicitud.Tipo switch
        {
            TipoSolicitud.Contrato
                => await contratos.RechazarAsync(
                    solicitud.ContratoId!.Value,
                    new ContratoRechazoPeticion { Comentario = comentario },
                    usuario,
                    ip,
                    mutacion,
                    ct,
                    solicitudId),
            TipoSolicitud.AccionAgente
                => await propuestas.RechazarAsync(solicitud.Id, comentario, usuario, ip, mutacion, ct),
            _ => throw new InvalidOperationException($"No hay servicio que decida una solicitud de tipo '{solicitud.Tipo}'."),
        };
    }

    /// <summary>
    /// Contrato de una solicitud de tipo contrato (BUG-042): la ruta de la bandeja recibe el
    /// identificador de la solicitud, el que viaja en la fila. 404 si no existe o no es de un contrato.
    /// </summary>
    public async Task<Guid> ContratoDeLaSolicitudAsync(Guid solicitudId, CancellationToken ct)
    {
        var solicitud = await LeerAsync(solicitudId, ct);

        return solicitud.Tipo == TipoSolicitud.Contrato && solicitud.ContratoId is { } contratoId
            ? contratoId
            : throw new KeyNotFoundException($"No hay ninguna solicitud de contrato con identificador '{solicitudId:D}'.");
    }

    /// <summary>Solo el tipo y el contrato: lo que hace falta para saber a quién le toca decidir.</summary>
    private async Task<SolicitudAprobacion> LeerAsync(Guid id, CancellationToken ct) =>
        await contexto.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SolicitudAprobacion { Id = s.Id, Tipo = s.Tipo, ContratoId = s.ContratoId })
            .SingleOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException($"No hay ninguna solicitud de aprobación con identificador '{id:D}'.");

    /// <summary>
    /// Renta con la que el contrato queda hoy según su historial de versiones: la
    /// <see cref="ContratoVersion.RentaNueva"/> del último número. Es el "valor actual" que §3.16 pide
    /// en la fila.
    /// <para>
    /// Un contrato que nunca estuvo activo no tiene versiones y sale a <c>null</c>, que no es lo mismo
    /// que cero: no es que la renta actual sea cero, es que todavía no hay una renta vigente anterior
    /// con la que comparar. La fila lo dice con <c>hayCambioDeRenta</c>.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> RentaVigentePorContratoAsync(
        IQueryable<Guid> contratoIds,
        CancellationToken ct)
    {
        var ids = await contratoIds.Distinct().ToListAsync(ct);

        var ultimoNumero = await contexto.ContratoVersion.AsNoTracking()
            .Where(v => ids.Contains(v.ContratoId))
            .GroupBy(v => v.ContratoId)
            .Select(g => new { ContratoId = g.Key, Numero = g.Max(v => v.Numero) })
            .ToListAsync(ct);

        var buscaNumero = ultimoNumero.ToDictionary(x => x.ContratoId, x => x.Numero);
        Guid[] conVersion = [.. buscaNumero.Keys];

        var filas = await contexto.ContratoVersion.AsNoTracking()
            .Where(v => conVersion.Contains(v.ContratoId))
            .Select(v => new { v.ContratoId, v.Numero, v.RentaNueva })
            .ToListAsync(ct);

        var pendientes = filas
            .Where(v => buscaNumero.TryGetValue(v.ContratoId, out var numero) && numero == v.Numero)
            .Where(v => v.RentaNueva is not null)
            .ToList();

        return pendientes.ToDictionary(v => v.ContratoId, v => v.RentaNueva!.Value);
    }

    private static AprobacionBandejaRespuesta Fila(
        SolicitudAprobacion solicitud,
        IReadOnlyDictionary<Guid, string> solicitantes,
        IReadOnlyDictionary<Guid, decimal> rentasVigentes)
    {
        var contrato = solicitud.Contrato;
        decimal? vigente = contrato is not null && rentasVigentes.TryGetValue(contrato.Id, out var renta)
            ? renta
            : null;

        return new AprobacionBandejaRespuesta(
            solicitud.Id,
            contrato?.Id ?? solicitud.ContratoId!.Value,
            solicitud.SolicitadoPorId,
            solicitud.SolicitadoPorId is { } quien && solicitantes.TryGetValue(quien, out var nombre)
                ? nombre
                : null,
            contrato?.Cliente?.Nombre,
            contrato?.Unidad?.Nombre,
            contrato?.Estado ?? EstadoContrato.PendienteAprobacion,
            contrato?.FechaInicio ?? default,
            contrato?.FechaFinVigente ?? default,
            vigente,
            contrato?.Renta ?? 0m,
            contrato?.Moneda ?? Moneda.DOP,
            vigente is { } actual && contrato is not null && actual != contrato.Renta,
            solicitud.HuellaContenido,
            solicitud.SolicitadoAt,
            solicitud.Comentario);
    }

    /// <summary>
    /// La bandeja es de quien puede aprobar. La comprobación se repite en el servicio por la misma
    /// razón que en <see cref="ServicioContratos"/>: el atributo del endpoint protege el HTTP y este
    /// método protege la lista, de modo que ningún camino —una llamada interna, un endpoint mal
    /// cableado, un trabajo— le da a un empleado la lista de lo que puede decidir.
    /// </summary>
    private async Task ExigirBandejaAsync(Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.ContratoEstado, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(Acciones.ContratoEstado, RecursoDeLaBandeja, ct);
        throw new PermisoDenegadoException(Acciones.ContratoEstado, RecursoDeLaBandeja);
    }
}