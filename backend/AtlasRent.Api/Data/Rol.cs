namespace AtlasRent.Api.Data;

/// <summary>
/// Rol de la aplicación (docs/ESPECIFICACION.md §2). Los tres están sembrados en la migración y su
/// techo de permisos vive en <see cref="PermisoRol"/>.
/// <para>
/// La clave es el identificador estable que viaja en el JWT y en <c>app.rol</c> para las políticas
/// de RLS (docs/DOC_TECNICA.md §4.3).
/// </para>
/// </summary>
public sealed class Rol
{
    public string Clave { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
}