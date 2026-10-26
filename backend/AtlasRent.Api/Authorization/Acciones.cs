namespace AtlasRent.Api.Authorization;

/// <summary>
/// Identificadores de acción, uno por fila de la matriz de docs/ESPECIFICACION.md §2. Son
/// estables: los escriben los endpoints con <c>[Accion]</c> y se guardan tal cual en
/// <c>permiso_rol</c> y <c>usuario_permiso</c>, así que renombrar uno es una migración, no un
/// cambio de texto.
/// <para>
/// El identificador no dice qué recurso se toca: el permiso es de la operación, no del documento
/// (docs/ESPECIFICACION.md §2).
/// </para>
/// </summary>
public static class Acciones
{
    /// <summary>1. Configuración, series, numeradores, impuestos, usuarios y roles. Solo admin.</summary>
    public const string ConfiguracionEditar = "configuracion.editar";

    /// <summary>2. Ver clientes, unidades, reservas y contratos.</summary>
    public const string ClienteVer = "cliente.ver";

    /// <summary>3. Crear o editar clientes, prospectos y reservas.</summary>
    public const string ClienteEditar = "cliente.editar";

    /// <summary>4. Documentos de propiedad (PDF) del inmueble.</summary>
    public const string InmuebleDocumento = "inmueble.documento";

    /// <summary>5. Crear y editar contratos en borrador.</summary>
    public const string ContratoBorrador = "contrato.borrador";

    /// <summary>
    /// 6. Aprobar, activar, modificar activo, dar de baja y liquidar. Es una sola acción porque la
    /// especificación las pone en una fila con el mismo alcance: gerente y admin, nunca empleado.
    /// </summary>
    public const string ContratoEstado = "contrato.estado";

    /// <summary>7. Ver facturas individuales (no sus totales, que son <see cref="InformeVer"/>).</summary>
    public const string FacturaVer = "factura.ver";

    /// <summary>8. Crear y editar facturas, proformas y cotizaciones en borrador.</summary>
    public const string FacturaBorrador = "factura.borrador";

    /// <summary>9. Emitir factura: asigna número y comprobante.</summary>
    public const string FacturaEmitir = "factura.emitir";

    /// <summary>10. Modificar o anular una factura ya emitida.</summary>
    public const string FacturaAnular = "factura.anular";

    /// <summary>11. Registrar, corregir o anular cobros.</summary>
    public const string CobroRegistrar = "cobro.registrar";

    /// <summary>12. Fianzas: cobrar, devolver y descontar.</summary>
    public const string FianzaMover = "fianza.mover";

    /// <summary>13. Enviar facturas, proformas, recibos y estados de cuenta.</summary>
    public const string DocumentoEnviar = "documento.enviar";

    /// <summary>
    /// 14. Enviar cotizaciones. Es la columna "Permiso" de la especificación: está en el techo del
    /// empleado pero llega desactivada (docs/ESPECIFICACION.md §2, D15).
    /// </summary>
    public const string CotizacionEnviar = "cotizacion.enviar";

    /// <summary>15. Envío masivo y recordatorios.</summary>
    public const string MensajeriaMasivo = "mensajeria.masivo";

    /// <summary>16. Editar plantillas de mensaje. Solo admin.</summary>
    public const string PlantillaEditar = "plantilla.editar";

    /// <summary>
    /// 17. Totales, informes, dashboard económico y exportaciones. El empleado nunca lo tiene, por
    /// ningún endpoint ni por ninguna exportación (docs/ESPECIFICACION.md §2).
    /// </summary>
    public const string InformeVer = "informe.ver";

    /// <summary>
    /// 18. Auditoría. La especificación da al gerente "Lectura", no "Sí": como la tabla es
    /// append-only y no existe ninguna escritura de auditoría por API, un solo identificador cubre
    /// el caso. Si algún día existiera, se separaría en <c>auditoria.ver</c> y
    /// <c>auditoria.gestionar</c>.
    /// </summary>
    public const string AuditoriaVer = "auditoria.ver";

    /// <summary>19. Desbloquear una IP bloqueada por intentos fallidos. Solo admin.</summary>
    public const string IpDesbloquear = "ip.desbloquear";

    /// <summary>19. Desbloquear una cuenta bloqueada por intentos fallidos. Solo admin.</summary>
    public const string CuentaDesbloquear = "cuenta.desbloquear";

    // Acciones técnicas: no están en la matriz de docs/ESPECIFICACION.md §2 porque no son acciones
    // de negocio sobre los datos, pero también pasan por [Accion] y por el techo del rol. Están
    // sembradas como las demás, así que ningún endpoint se queda abierto por descuido.

    /// <summary>Ver la identidad propia (<c>GET /api/v1/auth/yo</c>). Todos los roles.</summary>
    public const string SesionVer = "sesion.ver";

    /// <summary>Cerrar la propia sesión (<c>POST /api/v1/auth/logout</c>). Todos los roles.</summary>
    public const string SesionCerrar = "sesion.cerrar";

    /// <summary>Cambiar la contraseña propia (<c>POST /api/v1/auth/password</c>). Todos los roles (SEC-015).</summary>
    public const string SesionPassword = "sesion.password";

    /// <summary>
    /// El segundo factor es obligatorio para quien lo tiene (D4). Se comprueba como permiso para
    /// que "obligatorio" sea un dato y no una columna: hoy solo el administrador lo tiene.
    /// </summary>
    public const string MfaObligatorio = "mfa.obligatorio";

    /// <summary>
    /// Pedir y aprobar el acceso de emergencia (break-glass). Solo admin, y por la misma vía
    /// <see cref="ServicioEmergencia"/> lo comprueba otra vez antes de aprobar.
    /// </summary>
    public const string SeguridadAccesoEmergencia = "seguridad.acceso_emergencia";

    /// <summary>
    /// F6-01: emitir, listar y revocar tokens de agente. Solo admin, como
    /// <see cref="ConfiguracionEditar"/>: un token corre con los permisos de su usuario y la
    /// decisión de emitirlo no es de negocio.
    /// </summary>
    public const string AgenteToken = "agente.token";

    /// <summary>
    /// F6-01: usar el asistente o la consulta de IA en modo de solo lectura.
    /// <para>
    /// Admin y gerente, nunca empleado: lo que devuelve el asistente son datos agregados de clientes
    /// y cuentas, y el empleado no recibe totales por ningún endpoint (docs/ESPECIFICACION.md §2 y
    /// AGENTS.md §6). Es el mismo motivo por el que <see cref="InformeVer"/> no es del empleado.
    /// </para>
    /// </summary>
    public const string IaConsultar = "ia.consultar";

    /// <summary>
    /// F6-03: proponer una acción para que la apruebe alguien. La usan <c>POST /api/v1/ia/propuesta</c>
    /// y <c>POST /api/v1/agente/propuesta</c>; aprobar y rechazar son de otra acción, <c>mensajeria.masivo</c>.
    /// </summary>
    public const string IaProponer = "ia.proponer";

    /// <summary>
    /// Los 19 identificadores de la matriz, en el orden de la especificación. La fila 19 ("desbloquear
    /// IP o cuenta") tiene dos porque son dos operaciones distintas sobre dos datos distintos.
    /// </summary>
    public static readonly string[] Matriz =
    [
        ConfiguracionEditar,
        ClienteVer,
        ClienteEditar,
        InmuebleDocumento,
        ContratoBorrador,
        ContratoEstado,
        FacturaVer,
        FacturaBorrador,
        FacturaEmitir,
        FacturaAnular,
        CobroRegistrar,
        FianzaMover,
        DocumentoEnviar,
        CotizacionEnviar,
        MensajeriaMasivo,
        PlantillaEditar,
        InformeVer,
        AuditoriaVer,
        IpDesbloquear,
        CuentaDesbloquear,
    ];

    /// <summary>Todas las acciones conocidas, las de la matriz y las técnicas.</summary>
    public static readonly IReadOnlySet<string> Todas = new HashSet<string>(
        [.. Matriz, SesionVer, SesionCerrar, SesionPassword, MfaObligatorio, SeguridadAccesoEmergencia,
            AgenteToken, IaConsultar, IaProponer],
        StringComparer.Ordinal);
}