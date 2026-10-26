namespace AtlasRent.Api.Authorization;

/// <summary>
/// La matriz de docs/ESPECIFICACION.md §2, como datos: se siembra en <c>permiso_rol</c> con la
/// migración y no vive en un <c>switch</c> del código (AGENTS.md §6: cambiar la matriz de
/// permisos es una migración con registro, no un redespliegue).
/// <para>
/// Cada rol tiene su techo. Lo que el usuario puede quitar o activar dentro de ese techo está en
/// <c>usuario_permiso</c> (D17).
/// </para>
/// </summary>
public static class MatrizPermisos
{
    /// <summary>Nombres que se muestran de los tres roles.</summary>
    public static readonly IReadOnlyDictionary<string, string> Nombres = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Roles.Admin] = "Administrador",
        [Roles.Gerente] = "Gerente",
        [Roles.Empleado] = "Empleado",
    };

    /// <summary>Techo de cada rol.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PorRol =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Roles.Admin] =
            [
                .. Acciones.Matriz,
                Acciones.SesionVer,
                Acciones.SesionCerrar,
                Acciones.SesionPassword,
                Acciones.MfaObligatorio,
                Acciones.SeguridadAccesoEmergencia,
                Acciones.AgenteToken,
                Acciones.IaConsultar,
                Acciones.IaProponer,
            ],
            [Roles.Gerente] =
            [
                Acciones.ClienteVer,
                Acciones.ClienteEditar,
                Acciones.InmuebleDocumento,
                Acciones.ContratoBorrador,
                Acciones.ContratoEstado,
                Acciones.FacturaVer,
                Acciones.FacturaBorrador,
                Acciones.FacturaEmitir,
                Acciones.FacturaAnular,
                Acciones.CobroRegistrar,
                Acciones.FianzaMover,
                Acciones.DocumentoEnviar,
                Acciones.CotizacionEnviar,
                Acciones.MensajeriaMasivo,
                Acciones.InformeVer,
                Acciones.AuditoriaVer,
                Acciones.SesionVer,
                Acciones.SesionCerrar,
                Acciones.SesionPassword,
                // F6-01: el asistente es de lectura y responde con datos agregados de clientes y
                // cuentas, así que el gerente sí y el empleado no — igual que con informe.ver.
                Acciones.IaConsultar,
                // F6-01: proponer es más estrecho que consultar (la propuesta la aprueba alguien),
                // pero quien propone ya tiene que ver lo que propone, así que el mismo techo.
                // TODO(F6-03): la usa el endpoint de propuestas.
                Acciones.IaProponer,
            ],
            [Roles.Empleado] =
            [
                Acciones.ClienteVer,
                Acciones.ClienteEditar,
                Acciones.ContratoBorrador,
                Acciones.FacturaVer,
                Acciones.FacturaBorrador,
                Acciones.CotizacionEnviar,
                Acciones.SesionVer,
                Acciones.SesionCerrar,
                Acciones.SesionPassword,
            ],
        };

    /// <summary>
    /// Acciones que un usuario nuevo recibe desactivadas aunque su rol las traiga: la columna
    /// "Permiso" de la especificación. Al crear el usuario se escribe una fila en
    /// <c>usuario_permiso</c> con <c>permitido = false</c> para cada una (docs/ESPECIFICACION.md §2).
    /// </summary>
    public static readonly IReadOnlySet<string> DesactivadasAlCrearUsuario =
        new HashSet<string>(StringComparer.Ordinal) { Acciones.CotizacionEnviar };

    public static bool EnTecho(string rol, string accion) =>
        PorRol.TryGetValue(rol, out var acciones) && Array.IndexOf(acciones, accion) >= 0;
}