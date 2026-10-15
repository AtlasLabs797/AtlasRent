namespace AtlasRent.Api.Data;

/// <summary>
/// Usuario y rol de la petición en curso. El <see cref="ContextoTransaccionInterceptor"/> los
/// escribe en <c>app.usuario_id</c> y <c>app.rol</c> al abrir cada transacción, que es lo que
/// leen las políticas de RLS (docs/DOC_TECNICA.md §4.3).
/// <para>
/// En F1-03 todavía no hay autenticación: lo rellena el pipeline de sesión de F1-06.
/// </para>
/// </summary>
public sealed class ContextoSesion
{
    public Guid? UsuarioId { get; set; }

    /// <summary>
    /// Rol efectivo: <c>admin</c>, <c>gerente</c> o <c>empleado</c>.
    /// Vacío o ausente = sin acceso de lectura a auditoría.
    /// </summary>
    public string Rol { get; set; } = string.Empty;
}