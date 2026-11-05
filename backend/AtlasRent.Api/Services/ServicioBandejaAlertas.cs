using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lectura y cierre de las alertas (docs/ESPECIFICACION.md §3.16, F2-10).
/// <para>
/// <b>Qué ve cada uno depende del tipo de alerta</b>, no solo de que tenga sesión: un mensaje de
/// backup o de IP bloqueada no es para un empleado, y uno de fianza lleva importes. Cada tipo exige
/// una acción (<see cref="AccionPorTipo"/>) y se comprueba con <see cref="IPolicyService"/>, así que
/// un permiso recortado a un usuario también le quita sus alertas. Un tipo que no está en el mapa
/// exige <c>configuracion.editar</c>: denegar por omisión.
/// </para>
/// </summary>
public sealed class ServicioBandejaAlertas(
    AtlasRentDbContext contexto,
    IPolicyService politica,
    AuditoriaService auditoria,
    ContextoSesion sesion,
    TimeProvider reloj)
{
    public const string EstadoAbiertas = "abiertas";

    public const string EstadoCerradas = "cerradas";

    public const string EstadoTodas = "todas";

    /// <summary>Acción que da acceso a cada tipo de alerta. Decisión del orquestador, F2-10.</summary>
    public static readonly IReadOnlyDictionary<string, string> AccionPorTipo =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contrato"] = Acciones.ClienteVer,
            ["reserva"] = Acciones.ClienteVer,
            ["factura"] = Acciones.FacturaEmitir,
            ["comprobante"] = Acciones.FacturaEmitir,
            ["tipo_cambio"] = Acciones.FacturaEmitir,
            ["fianza"] = Acciones.FianzaMover,
            ["recordatorio"] = Acciones.MensajeriaMasivo,
            ["envio"] = Acciones.DocumentoEnviar,
            ["seguridad"] = Acciones.IpDesbloquear,
            ["backup"] = Acciones.ConfiguracionEditar,
            ["auditoria"] = Acciones.ConfiguracionEditar,
        };

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<Alerta>, bool, IOrderedQueryable<Alerta>>>
        Ordenes = new Dictionary<string, Func<IQueryable<Alerta>, bool, IOrderedQueryable<Alerta>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["ocurridoAt"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.OcurridoAt).ThenBy(a => a.Id)
                : q.OrderBy(a => a.OcurridoAt).ThenBy(a => a.Id),
            ["tipo"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.Tipo).ThenBy(a => a.Id)
                : q.OrderBy(a => a.Tipo).ThenBy(a => a.Id),
            ["repeticiones"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.Repeticiones).ThenBy(a => a.Id)
                : q.OrderBy(a => a.Repeticiones).ThenBy(a => a.Id),
        };

    public static bool EstadoValido(string estado) =>
        estado is EstadoAbiertas or EstadoCerradas or EstadoTodas;

    /// <summary>
    /// Alertas que el usuario de la petición puede ver. Pedir un <paramref name="tipo"/> que no le
    /// toca devuelve una lista vacía y no un 403: no se confirma que existan alertas de ese tipo.
    /// </summary>
    public async Task<Pagina<AlertaFila>> ListarAsync(
        ConsultaLista paginacion,
        string? tipo,
        string estado,
        CancellationToken ct)
    {
        IQueryable<Alerta> consulta = contexto.Alerta.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var pedido = tipo.Trim();
            if (!PuedeVer(pedido))
            {
                return new Pagina<AlertaFila>([], 0, paginacion.Page, paginacion.PageSize, 0);
            }

            consulta = consulta.Where(a => a.Tipo == pedido);
        }
        else
        {
            var permitidos = AccionPorTipo.Keys.Where(PuedeVer).ToList();
            var conocidos = AccionPorTipo.Keys.ToList();
            var verDesconocidos = Puede(Acciones.ConfiguracionEditar);
            consulta = consulta.Where(a => permitidos.Contains(a.Tipo)
                || (verDesconocidos && !conocidos.Contains(a.Tipo)));
        }

        consulta = estado switch
        {
            EstadoCerradas => consulta.Where(a => a.ResueltaAt != null),
            EstadoTodas => consulta,
            _ => consulta.Where(a => a.ResueltaAt == null),
        };

        return await ConsultaPaginada.PaginarAsync(consulta, Ordenes, "ocurridoAt", paginacion, AFila, ct);
    }

    /// <summary>
    /// Cierra la alerta. Cerrar una ya cerrada no cambia la fecha ni vuelve a auditar. Si el problema
    /// persiste, el trabajo que la levantó la reabre: es lo esperado (§3.16).
    /// </summary>
    /// <exception cref="KeyNotFoundException">La alerta no existe.</exception>
    /// <exception cref="PermisoDenegadoException">El usuario no tiene la acción del tipo de alerta.</exception>
    public async Task<AlertaFila> CerrarAsync(Guid id, Guid usuarioId, string ip, CancellationToken ct)
    {
        var alerta = await contexto.Alerta.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"La alerta '{id:D}' no existe.");

        // Un token de agente no muta (SEC-010/011): cerrar le está vedado aunque vea la alerta.
        if (sesion.EsAgente || !PuedeVer(alerta.Tipo))
        {
            var recurso = $"alerta/{id:D}";
            var accion = AccionDe(alerta.Tipo);
            await politica.AuditarDenegadoAsync(accion, recurso, ct);

            throw new PermisoDenegadoException(accion, recurso);
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // El UPDATE condicionado a resuelta_at IS NULL es lo que conserva la fecha del primer cierre
        // y lo que evita que dos cierres simultáneos (o un cierre y el ResolverAsync de un trabajo)
        // escriban dos veces.
        var ahora = reloj.GetUtcNow();
        var cerradas = await contexto.Alerta
            .Where(a => a.Id == id && a.ResueltaAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ResueltaAt, ahora), ct);

        if (cerradas > 0)
        {
            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = "alerta.cerrada",
                Ip = ip,
                UsuarioId = usuarioId,
                Entidad = "alerta",
                EntidadId = id.ToString("D"),
                DatosJson = System.Text.Json.JsonSerializer.Serialize(new { tipo = alerta.Tipo }),
                Resultado = "permitido",
                Origen = "api",
            }, ct);
        }

        await transaccion.CommitAsync(ct);

        return AFila(await contexto.Alerta.AsNoTracking().SingleAsync(a => a.Id == id, ct));
    }

    private bool PuedeVer(string tipo) => Puede(AccionDe(tipo));

    /// <summary>
    /// AUD-10 (H-01): el endpoint se declara con <c>cliente.ver</c>, que un token de agente puede
    /// llevar, así que el tipo se comprueba también contra las acciones del token y la lista blanca.
    /// </summary>
    private bool Puede(string accion) =>
        politica.Puede(accion)
        && (!sesion.EsAgente
            || (Acciones.PermitidasAAgente.Contains(accion) && sesion.AgenteAcciones.Contains(accion)));

    private static string AccionDe(string tipo) =>
        AccionPorTipo.TryGetValue(tipo, out var accion) ? accion : Acciones.ConfiguracionEditar;

    private static AlertaFila AFila(Alerta a) =>
        new(a.Id, a.Tipo, a.Mensaje, a.OcurridoAt, a.Repeticiones, a.ResueltaAt);
}
