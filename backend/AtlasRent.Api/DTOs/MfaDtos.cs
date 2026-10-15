using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Services;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Respuesta del login cuando la contraseña es correcta pero falta el segundo factor.
/// <para>
/// No es una sesión: no hay cookie de acceso y el token provisional solo habilita los endpoints del
/// segundo factor.
/// </para>
/// </summary>
public sealed record RespuestaDesafioMfa(
    bool RequiereMfa,
    bool RequiereConfiguracion,
    string NombreAcceso,
    DateTimeOffset Caduca);

/// <summary>Código TOTP de 6 dígitos.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MfaConfirmarPeticion
{
    [Required]
    [StringLength(ServicioMfa.Digitos, MinimumLength = ServicioMfa.Digitos)]
    public required string Codigo { get; init; }
}

/// <summary>
/// Código del segundo factor: TOTP o de recuperación. Se acepta el formato con guion
/// (<c>ABCDE-FGHIJ</c>) y sin él.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SegundoFactorPeticion
{
    [Required]
    [StringLength(16, MinimumLength = 6)]
    public required string Codigo { get; init; }
}

/// <summary>Lo que hay que pintar en el QR. El frontend dibuja la imagen; la API no.</summary>
public sealed record RespuestaActivacionMfa(string Uri, int Digitos, int PeriodoSegundos);

/// <summary>
/// Códigos de recuperación en claro. Se devuelven una única vez, aquí, y la base de datos solo
/// guarda sus hashes.
/// </summary>
public sealed record RespuestaConfirmacionMfa(IReadOnlyList<string> CodigosRecuperacion);

/// <summary>Petición del acceso de emergencia para un administrador bloqueado.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EmergenciaSolicitarPeticion
{
    /// <summary>Administrador al que se le da el acceso: el que perdió el dispositivo.</summary>
    [Required]
    public required Guid UsuarioObjetivoId { get; init; }

    /// <summary>Por qué se pide. Queda escrito en la auditoría firmada.</summary>
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public required string Motivo { get; init; }
}

/// <summary>Segunda aprobación del acceso de emergencia.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EmergenciaAprobarPeticion
{
    [Required]
    public required Guid SolicitudId { get; init; }
}

/// <summary>
/// Resultado de aprobar el acceso de emergencia. <c>CodigoAcceso</c> solo viene cuando la
/// aprobación es la segunda; es de un solo uso y no se vuelve a mostrar.
/// </summary>
public sealed record RespuestaAprobacionEmergencia(Guid Id, DateTimeOffset? Caduca, string? CodigoAcceso);