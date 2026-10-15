namespace AtlasRent.Api.Data;

/// <summary>
/// Sesión abierta por un usuario. Es la tabla que hace que un JWT siga siendo revocable
/// (docs/SEGURIDAD.md SEC-003): en cada petición se valida la fila contra la base de datos.
/// <para>
/// Del token de refresco solo se guarda el hash SHA-256. El token en claro viaja únicamente en la
/// cookie <c>atlas-rent-rt</c> del navegador.
/// </para>
/// </summary>
public sealed class Sesion
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>SHA-256 en hexadecimal del token de refresco. Único: es la búsqueda del token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Familia de rotación: todos los tokens que descienden del mismo login comparten familia.
    /// Si llega un token ya rotado se revoca la familia completa, porque significa que alguien
    /// copió un token o que hubo robo.
    /// </summary>
    public Guid FamiliaId { get; set; }

    public string Ip { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    public DateTimeOffset EmitidaAt { get; set; }

    /// <summary>Última petición autenticada. La inactividad se mide contra este campo.</summary>
    public DateTimeOffset UltimaActividad { get; set; }

    /// <summary>Máximo absoluto de la sesión (12 h), heredado por los tokens rotados.</summary>
    public DateTimeOffset CaducaAbsoluta { get; set; }

    public DateTimeOffset? RevocadaAt { get; set; }

    /// <summary>
    /// Motivo de la revocación. <c>rotacion</c> marca un token ya rotado (su reutilización es un
    /// intento de robo); el resto son finales de vida: <c>logout</c>, <c>inactividad</c>,
    /// <c>maximo_absoluto</c> o <c>reutilizacion_detectada</c>.
    /// </summary>
    public string? MotivoRevocacion { get; set; }
}