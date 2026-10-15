namespace AtlasRent.Api.Data;

/// <summary>
/// Permiso que difiere del techo del rol en un usuario concreto (docs/ESPECIFICACION.md §2, D17).
/// <para>
/// Solo se guarda lo que difiere: <see cref="Permitido"/> a <c>false</c> quita al usuario un
/// permiso que su rol trae, y a <c>true</c> le activa uno que su rol trae desactivado ("enviar
/// cotizaciones" del empleado). Nunca hay una fila con un permiso que el rol no trae: eso sería
/// ampliar por encima del techo, y la API lo rechaza al guardar.
/// </para>
/// </summary>
public sealed class UsuarioPermiso
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public string Accion { get; set; } = string.Empty;

    public bool Permitido { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}