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

    /// <summary>
    /// F6-01: la petición no viene de una sesión de navegador sino de un token de agente. El
    /// usuario y el rol son los del propietario del token, así que RLS y los permisos se comporta
    /// igual que en su sesión; lo único que cambia es que además hay que comprobar la lista de
    /// acciones del token (<see cref="AgenteAcciones"/>).
    /// </summary>
    public bool EsAgente { get; set; }

    /// <summary>Token de agente que atendió la petición, para poder auditarlo.</summary>
    public Guid? AgenteTokenId { get; set; }

    /// <summary>Acciones que el token de agente puede pedir. Vacío fuera de una petición de agente.</summary>
    public IReadOnlySet<string> AgenteAcciones { get; set; } = new HashSet<string>(StringComparer.Ordinal);
}