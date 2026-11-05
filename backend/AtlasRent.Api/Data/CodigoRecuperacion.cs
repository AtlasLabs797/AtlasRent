namespace AtlasRent.Api.Data;

/// <summary>
/// Código de recuperación de un solo uso (docs/SEGURIDAD.md A.2). Entra cuando el usuario pierde
/// el dispositivo, así que sustituye al TOTP.
/// <para>
/// Del código solo se guarda el hash SHA-256: el valor en claro se muestra una única vez, al
/// activar el MFA, y no vuelve a salir de la base de datos ni de un log.
/// </para>
/// </summary>
public sealed class CodigoRecuperacion
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>SHA-256 en hexadecimal del código normalizado (mayúsculas y sin guion).</summary>
    public string CodigoHash { get; set; } = string.Empty;

    public DateTimeOffset CreadoAt { get; set; }

    /// <summary>Cuándo se gastó. Un código usado no vuelve a servir (docs/SEGURIDAD.md A.2).</summary>
    public DateTimeOffset? UsadoAt { get; set; }
}