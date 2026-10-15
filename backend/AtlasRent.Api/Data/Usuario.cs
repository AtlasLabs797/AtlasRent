namespace AtlasRent.Api.Data;

/// <summary>
/// Usuario de la aplicación. Solo guarda el hash BCrypt de la contraseña: nunca la contraseña
/// ni nada derivable de ella en claro (docs/SEGURIDAD.md A.2).
/// <para>
/// El nombre de acceso se guarda ya normalizado en minúsculas, que es como se busca, para que la
/// unicidad no dependa de mayúsculas.
/// </para>
/// </summary>
public sealed class Usuario
{
    public Guid Id { get; set; }

    /// <summary>Nombre de acceso único, en minúsculas y sin espacios sobrantes.</summary>
    public string NombreAcceso { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>Hash BCrypt de la contraseña. Campo único que la representa.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Cuenta desactivada: la validación de sesión responde 401 en la siguiente petición.</summary>
    public bool Activo { get; set; }

    public bool DebeCambiarPassword { get; set; }

    /// <summary>
    /// Segundo factor activo: el usuario entra con contraseña y además con TOTP o con un código de
    /// recuperación. Para el administrador es obligatorio (docs/ESPECIFICACION.md D4).
    /// </summary>
    public bool MfaActivado { get; set; }

    /// <summary>
    /// Secreto TOTP en base32. Nulo si el usuario nunca empezó a activarlo. Se sustituye cada vez
    /// que se reinicia la activación y se necesita en claro para validar el código, así que no
    /// se muestra nunca: sale una sola vez, dentro de la URI <c>otpauth://</c>.
    /// <para>
    /// TODO(A#) F1-09: cifrarlo en reposo con la misma clave AES-256-GCM que los documentos de
    /// identidad. Hasta entonces vive en claro en la base de datos, que es donde está el riesgo.
    /// </para>
    /// </summary>
    public string? MfaSecreto { get; set; }

    /// <summary>
    /// El segundo factor se empezó a configurar pero todavía no se confirmó con un código válido.
    /// El usuario entra, pero el frontend tiene que dejarle claro que le falta el segundo factor.
    /// </summary>
    public bool MfaPendiente => MfaSecreto is not null && !MfaActivado;

    /// <summary>
    /// Administrador: para ellos el MFA es obligatorio y sin él no hay sesión (D4).
    /// <para>
    /// TODO(A#) F1-07: este indicador desaparece cuando exista la tabla de roles y permisos. Las
    /// dos únicas decisiones que dependen de él están en ServicioMfa (si toca segundo factor) y en
    /// ServicioEmergencia (quién puede aprobar); hasta entonces no hay forma de crear un
    /// administrador por la API (esa tarea llega con F1-07).
    /// </para>
    /// </summary>
    public bool EsAdmin { get; set; }

    /// <summary>
    /// Hasta cuándo está bloqueada la cuenta por intentos fallidos (docs/SEGURIDAD.md A.2).
    /// Nula o pasada significa que la cuenta se puede volver a usar.
    /// </summary>
    public DateTimeOffset? BloqueadaHasta { get; set; }

    public DateTimeOffset? UltimoAcceso { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    /// <summary>
    /// Normalización del nombre de acceso: minúsculas y sin espacios sobrantes. Se usa al crear
    /// y al buscar, de modo que el índice único y las consultas no dependan de cómo escribió el
    /// usuario su nombre.
    /// </summary>
    public static string NormalizarNombreAcceso(string nombreAcceso) =>
        nombreAcceso.Trim().ToLowerInvariant();
}