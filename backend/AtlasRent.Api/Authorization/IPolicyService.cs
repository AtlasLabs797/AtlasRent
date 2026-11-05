using AtlasRent.Api.Data;
using AtlasRent.Api.Services;

namespace AtlasRent.Api.Authorization;

/// <summary>
/// Control de acceso por acción (docs/ESPECIFICACION.md §2, AGENTS.md §6). Ningún endpoint entra
/// sin pasar por aquí, y todo se comprueba en el backend: esconder un botón no es protección.
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Permisos efectivos del usuario de la petición: el techo de su rol recortado por sus permisos
    /// por usuario. Vacío hasta que se llama a <see cref="CargarAsync"/>.
    /// </summary>
    IReadOnlySet<string> Permisos { get; }

    /// <summary>
    /// Carga los permisos del usuario de la petición. Lo llama el filtro global una vez por
    /// petición; a partir de ahí <see cref="Puede"/> y <see cref="Exigir"/> responden sin ir a la
    /// base de datos.
    /// </summary>
    Task CargarAsync(CancellationToken ct = default);

    /// <summary>Si el usuario de la petición tiene esa acción.</summary>
    bool Puede(string accion);

    /// <summary>
    /// Igual que <see cref="Puede"/>, pero lanza <see cref="PermisoDenegadoException"/> si no la
    /// tiene, que es lo que acaba en 403 y en la auditoría.
    /// </summary>
    void Exigir(string accion, string? recurso = null);

    /// <summary>
    /// Permiso de un usuario cualquiera. Se usa donde no hay sesión todavía (el login, que decide si
    /// toca segundo factor) o donde el usuario no es el de la petición (el acceso de emergencia).
    /// </summary>
    Task<bool> PuedeAsync(string accion, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Permisos efectivos de un usuario cualquiera, con su techo y sus recortes.</summary>
    Task<IReadOnlySet<string>> PermisosDeAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Rol de un usuario, leído de <c>usuario_rol</c>. Es lo que pasa a <c>app.rol</c> para las
    /// políticas de RLS.
    /// </summary>
    Task<string?> RolDeAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Comprueba que activar un permiso a un usuario está dentro del techo de su rol y lanza
    /// <see cref="PermisoFueraDeTechoException"/> si no (D17). Quitar un permiso nunca falla: siempre
    /// está dentro del techo.
    /// </summary>
    Task ExigirDentroDelTechoAsync(Guid usuarioId, string accion, bool activar, CancellationToken ct = default);

    /// <summary>
    /// Escribe la denegación en la auditoría firmada, en su propia transacción para que un
    /// <c>ROLLBACK</c> posterior no se la lleve por delante.
    /// </summary>
    Task AuditarDenegadoAsync(string accion, string? recurso, CancellationToken ct = default);
}