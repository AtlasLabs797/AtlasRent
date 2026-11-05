namespace AtlasRent.Api.Data;

/// <summary>
/// Rol de un usuario. Es lo que lee el pipeline de autenticación y lo que pasa a <c>app.rol</c>
/// para las políticas de RLS.
/// <para>
/// Un usuario tiene un solo rol (docs/ESPECIFICACION.md §2: administrador, gerente o empleado), así
/// que el índice de <see cref="UsuarioId"/> es único.
/// </para>
/// </summary>
public sealed class UsuarioRol
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public string RolClave { get; set; } = string.Empty;

    /// <summary>Dueño del rol. Sin navegación inversa: es una relación uno a uno.</summary>
    public Usuario? Usuario { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}