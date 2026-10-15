namespace AtlasRent.Api.Data;

/// <summary>
/// Usuario y rol de la petición en curso. El <see cref="ContextoTransaccionInterceptor"/> los
/// escribe en <c>app.usuario_id</c> y <c>app.rol</c> al abrir cada transacción, que es lo que
/// leen las políticas de RLS (docs/DOC_TECNICA.md §4.3).
/// <para>
/// F1-04 lo rellena el pipeline de autenticación (<c>ValidacionSesionMiddleware</c>) con el usuario
/// y el rol de la petición. Está registrado con ámbito de petición: si fuera singleton, dos
/// peticiones simultáneas se pisarían el rol y RLS leería el del otro usuario.
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