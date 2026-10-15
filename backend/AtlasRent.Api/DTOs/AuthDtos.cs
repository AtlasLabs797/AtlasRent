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
/// Identidad del usuario de la petición. <see cref="Rol"/> va ya porque F1-07 lo añadirá; aquí es
/// el valor por defecto (<see cref="Roles.Valor"/>).
/// </summary>
public sealed record IdentidadRespuesta(
    Guid Id,
    string NombreAcceso,
    string Nombre,
    string? Email,
    string Rol,
    bool DebeCambiarPassword,
    bool MfaActivado,
    DateTimeOffset? UltimoAcceso)
{
    public static IdentidadRespuesta Desde(Usuario usuario) => new(
        usuario.Id,
        usuario.NombreAcceso,
        usuario.Nombre,
        usuario.Email,
        Roles.Valor,
        usuario.DebeCambiarPassword,
        usuario.MfaActivado,
        usuario.UltimoAcceso);
}