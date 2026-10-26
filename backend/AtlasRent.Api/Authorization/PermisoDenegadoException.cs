namespace AtlasRent.Api.Authorization;

/// <summary>
/// Permiso denegado. La escribe <see cref="PolicyService"/>; el filtro global la convierte en 403 y
/// deja la denegación en la auditoría firmada (docs/ESPECIFICACION.md §2: un intento no permitido
/// se rechaza y se audita).
/// </summary>
public sealed class PermisoDenegadoException(string accion, string? recurso)
    : Exception($"El usuario no tiene el permiso '{accion}'.")
{
    public string Accion { get; } = accion;

    /// <summary>Sobre qué se quiso actuar: la ruta del endpoint. Va en la auditoría, no en el log.</summary>
    public string? Recurso { get; } = recurso;
}

/// <summary>
/// Se ha intentado activar a un usuario un permiso que su rol no trae. Los permisos por usuario
/// recortan el techo del rol, nunca lo amplían (D17), así que se rechaza al guardar y no al usar.
/// </summary>
public sealed class PermisoFueraDeTechoException(string accion, string rol)
    : Exception($"El rol '{rol}' no trae el permiso '{accion}'.")
{
    public string Accion { get; } = accion;

    public string Rol { get; } = rol;
}

/// <summary>
/// Cambio que dejaría al sistema sin su salvaguarda de administración: quitar a un administrador
/// los permisos que lo protegen (MFA obligatorio, acceso de emergencia) o dejar sin administrador
/// activo a la instalación (SEC-025).
/// </summary>
public sealed class ReglaDeAdministradorException(string mensaje) : Exception(mensaje);
