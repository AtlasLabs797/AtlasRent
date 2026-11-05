namespace AtlasRent.Api.Data;

/// <summary>
/// Permiso que un rol trae de serie: el techo. Un usuario solo puede recortarlo con
/// <see cref="UsuarioPermiso"/>, nunca ampliarlo (D17).
/// </summary>
public sealed class PermisoRol
{
    public string RolClave { get; set; } = string.Empty;

    /// <summary>Identificador de acción de <c>AtlasRent.Api.Authorization.Acciones</c>.</summary>
    public string Accion { get; set; } = string.Empty;
}