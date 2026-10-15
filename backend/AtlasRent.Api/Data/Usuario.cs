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

    /// <summary>Marcador para F1-06 (MFA/TOTP). Aquí no hace nada más que quedar disponible.</summary>
    public bool MfaActivado { get; set; }

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