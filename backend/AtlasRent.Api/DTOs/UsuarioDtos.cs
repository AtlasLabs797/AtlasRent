using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta de usuario. Campos desconocidos rechazados (docs/SEGURIDAD.md A.5).
/// <para>
/// El rol es el techo de sus permisos y no se puede crear por encima del techo propio: el
/// administrador que da de alta a otro administrador decide quién entra, y queda en auditoría.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UsuarioCrearPeticion
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string NombreAcceso { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; init; }

    [Required]
    [StringLength(ServicioContrasenas.LongitudMaxima, MinimumLength = ServicioContrasenas.LongitudMinima)]
    public required string Contrasena { get; init; }

    /// <summary>Clave de <c>rol</c>: <c>admin</c>, <c>gerente</c> o <c>empleado</c>.</summary>
    [Required]
    [RegularExpression("^(admin|gerente|empleado)$")]
    public required string Rol { get; init; }
}

/// <summary>Permiso por usuario: activar o quitar uno dentro del techo de su rol.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UsuarioPermisoPeticion
{
    /// <summary>Identificador de acción de <see cref="Acciones"/>.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Accion { get; init; }

    /// <summary>
    /// <c>true</c> activa el permiso para este usuario, <c>false</c> se lo quita. Activar algo que
    /// su rol no trae se rechaza: los permisos por usuario recortan, nunca amplían (D17).
    /// </summary>
    public bool Permitido { get; init; }
}

/// <summary>Cambio de rol de un usuario.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UsuarioRolPeticion
{
    [Required]
    [RegularExpression("^(admin|gerente|empleado)$")]
    public required string Rol { get; init; }
}

/// <summary>Fila de la lista de usuarios.</summary>
public sealed record UsuarioResumen(
    Guid Id,
    string NombreAcceso,
    string Nombre,
    string? Email,
    bool Activo,
    string Rol);

/// <summary>Rol y permisos efectivos (techo del rol menos lo que este usuario tiene recortado).</summary>
public sealed record UsuarioPermisosRespuesta(Guid Id, string Rol, IReadOnlyList<string> Permisos);

/// <summary>Respuesta del alta de usuario.</summary>
/// <param name="ContrasenaComprobada">
/// Falso si HaveIBeenPwned no respondió: la contraseña se aceptó sin comprobar si está filtrada.
/// </param>
public sealed record UsuarioCreadoRespuesta(Guid Id, string NombreAcceso, string Rol, bool ContrasenaComprobada = true);

/// <summary>Activar o desactivar un usuario.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UsuarioActivoPeticion
{
    public required bool Activo { get; init; }
}

/// <summary>
/// Restablecimiento de contraseña por el administrador. El usuario queda obligado a cambiarla en su
/// primer acceso, porque la conoce quien la fijó.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UsuarioPasswordPeticion
{
    [Required]
    [StringLength(ServicioContrasenas.LongitudMaxima, MinimumLength = ServicioContrasenas.LongitudMinima)]
    public required string Contrasena { get; init; }
}

/// <summary>Cambio de la contraseña propia: exige la actual.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CambiarPasswordPeticion
{
    [Required]
    [StringLength(ServicioContrasenas.LongitudMaxima, MinimumLength = 1)]
    public required string ContrasenaActual { get; init; }

    [Required]
    [StringLength(ServicioContrasenas.LongitudMaxima, MinimumLength = ServicioContrasenas.LongitudMinima)]
    public required string ContrasenaNueva { get; init; }
}

/// <param name="ContrasenaComprobada">Falso si HaveIBeenPwned no respondió (se aceptó sin comprobar).</param>
public sealed record PasswordCambiadaRespuesta(bool ContrasenaComprobada);

/// <summary>Desbloqueo de IP: cuerpo y respuesta.</summary>
public sealed class IpDesbloquearPeticion
{
    /// <summary>IP bloqueada por intentos fallidos de login.</summary>
    [Required]
    [StringLength(45, MinimumLength = 3)]
    public required string Ip { get; init; }
}

/// <summary>Desbloqueo de cuenta: cuerpo.</summary>
public sealed class CuentaDesbloquearPeticion
{
    [Required]
    public required Guid UsuarioId { get; init; }
}