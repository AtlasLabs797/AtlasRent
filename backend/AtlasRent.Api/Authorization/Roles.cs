namespace AtlasRent.Api.Authorization;

/// <summary>
/// Claves de rol. Son las mismas que viajan en el JWT y en <c>app.rol</c> para las políticas de
/// RLS (docs/DOC_TECNICA.md §4.3): cambiarlas rompe a la vez la sesión y la base de datos.
/// </summary>
public static class Roles
{
    public const string Admin = "admin";

    public const string Gerente = "gerente";

    public const string Empleado = "empleado";
}