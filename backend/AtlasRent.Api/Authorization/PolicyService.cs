using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Authorization;

/// <summary>
/// Resuelve el techo del rol y lo combina con los permisos por usuario, siempre dentro de ese techo:
/// un permiso por usuario puede quitar algo que el rol trae, nunca añadir algo que el rol no trae
/// (docs/ESPECIFICACION.md §2, D17).
/// <para>
/// Es de ámbito de petición: guarda los permisos ya calculados para no repetirlos en cada
/// <c>[Accion]</c>. La denegación va siempre a la auditoría firmada.
/// </para>
/// </summary>
public sealed class PolicyService(
    AtlasRentDbContext contexto,
    ContextoSesion sesion,
    AuditoriaService auditoria,
    IResolutorIpPeticion resolutorIp,
    IHttpContextAccessor accesoHttp) : IPolicyService
{
    private HashSet<string>? _permisos;

    public IReadOnlySet<string> Permisos =>
        _permisos ?? new HashSet<string>(StringComparer.Ordinal);

    public async Task CargarAsync(CancellationToken ct = default)
    {
        if (_permisos is not null)
        {
            return;
        }

        HashSet<string> permisos;
        if (sesion.UsuarioId is { } usuarioId)
        {
            permisos = [.. await PermisosDeAsync(usuarioId, ct)];
        }
        else
        {
            permisos = new HashSet<string>(StringComparer.Ordinal);
        }

        _permisos = permisos;
    }

    public bool Puede(string accion) => Permisos.Contains(accion);

    public void Exigir(string accion, string? recurso = null)
    {
        if (!Puede(accion))
        {
            throw new PermisoDenegadoException(accion, recurso);
        }
    }

    public async Task<bool> PuedeAsync(string accion, Guid usuarioId, CancellationToken ct = default) =>
        (await PermisosDeAsync(usuarioId, ct)).Contains(accion);

    public async Task<IReadOnlySet<string>> PermisosDeAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var rol = await RolDeAsync(usuarioId, ct);
        if (rol is null)
        {
            // Sin rol no hay techo: se deniega todo. Un usuario al que se le borrara el rol se queda
            // fuera en vez de quedarse con el techo del último rol que tuvo.
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var efectivos = await TechoAsync(rol, ct);

        var porUsuario = await contexto.UsuarioPermiso
            .AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => new { p.Accion, p.Permitido })
            .ToListAsync(ct);

        foreach (var permiso in porUsuario)
        {
            // La fila solo puede recortar: si el rol no trae la acción, se ignora. La API no deja
            // guardarla, pero aquí tampoco se fía de que la fila exista como debe.
            if (!efectivos.Contains(permiso.Accion))
            {
                continue;
            }

            if (permiso.Permitido)
            {
                efectivos.Add(permiso.Accion);
            }
            else
            {
                efectivos.Remove(permiso.Accion);
            }
        }

        return efectivos;
    }

    public async Task<string?> RolDeAsync(Guid usuarioId, CancellationToken ct = default) =>
        await contexto.UsuarioRol
            .AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId)
            .Select(r => r.RolClave)
            .SingleOrDefaultAsync(ct);

    public async Task ExigirDentroDelTechoAsync(
        Guid usuarioId,
        string accion,
        bool activar,
        CancellationToken ct = default)
    {
        if (!activar)
        {
            // Quitar un permiso siempre está dentro del techo: no hay nada que pueda quedarse por
            // encima del rol.
            return;
        }

        var rol = await RolDeAsync(usuarioId, ct);
        if (rol is null || !(await TechoAsync(rol, ct)).Contains(accion))
        {
            throw new PermisoFueraDeTechoException(accion, rol ?? "(sin rol)");
        }
    }

    public async Task AuditarDenegadoAsync(string accion, string? recurso, CancellationToken ct = default)
    {
        // En su propia transacción: la denegación no va con la operación denegada (que no existe),
        // y si fuera dentro de la de ella, el ROLLBACK se la llevaría por delante.
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = $"permiso.denegado.{accion}",
            Ip = Ip(),
            UsuarioId = sesion.UsuarioId,
            Entidad = "permiso",
            EntidadId = accion,
            DatosJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["accion"] = accion,
                ["recurso"] = recurso ?? string.Empty,
                ["rol"] = sesion.Rol,
            }),
            Resultado = "denegado",
            // F6-01: una denegación de un token de agente se registra como tal, para que el soporte
            // pueda distinguirla de un usuario que se pasó de listo con su sesión.
            Origen = sesion.EsAgente ? "agente" : "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }

    private async Task<HashSet<string>> TechoAsync(string rol, CancellationToken ct) =>
        [.. await contexto.PermisoRol
            .AsNoTracking()
            .Where(p => p.RolClave == rol)
            .Select(p => p.Accion)
            .ToListAsync(ct)];

    private string Ip() =>
        accesoHttp.HttpContext is { } http ? resolutorIp.Resolver(http) : ResolutorIpPeticion.Desconocida;
}