using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;

namespace AtlasRent.Api.DTOs;

/// <summary>Credenciales de entrada del login. Campos desconocidos rechazados (docs/SEGURIDAD.md A.5).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginPeticion
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string NombreAcceso { get; init; }

    [Required]
    [StringLength(ServicioContrasenas.LongitudMaxima, MinimumLength = 1)]
    public required string Contrasena { get; init; }
}

/// <summary>
/// Identidad del usuario de la petición. <see cref="Rol"/> viene de <c>usuario_rol</c>, que es lo
/// mismo que lee <c>app.rol</c> en las políticas de RLS.
/// </summary>
public sealed record IdentidadRespuesta(
    Guid Id,
    string NombreAcceso,
    string Nombre,
    string? Email,
    string Rol,
    bool DebeCambiarPassword,
    bool MfaActivado,
    bool MfaPendiente,
    bool AccesoEmergencia,
    DateTimeOffset? UltimoAcceso)
{
    public static IdentidadRespuesta Desde(Usuario usuario, bool accesoEmergencia, string rol) => new(
        usuario.Id,
        usuario.NombreAcceso,
        usuario.Nombre,
        usuario.Email,
        rol,
        usuario.DebeCambiarPassword,
        usuario.MfaActivado,
        usuario.MfaPendiente,
        accesoEmergencia,
        usuario.UltimoAcceso);

    /// <summary>Identidad de una sesión recién emitida, sea del login normal o del segundo factor.</summary>
    public static IdentidadRespuesta Desde(ResultadoSesion sesion) => new(
        sesion.UsuarioId,
        sesion.NombreAcceso,
        sesion.Nombre,
        sesion.Email,
        sesion.Rol,
        sesion.DebeCambiarPassword,
        sesion.MfaActivado,
        sesion.MfaPendiente,
        sesion.AccesoEmergencia,
        sesion.UltimoAcceso);
}