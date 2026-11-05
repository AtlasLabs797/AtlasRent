namespace AtlasRent.Api.Data;

/// <summary>
/// Desafío del segundo factor: lo que devuelve el login cuando la contraseña es correcta pero hace
/// falta el TOTP (o el alta del MFA de un administrador).
/// <para>
/// No es una sesión. Es un token opaco de vida corta y un solo uso, y solo habilita los endpoints
/// del segundo factor; con él no se puede llamar a nada más (docs/DOC_TECNICA.md §7, F1-06).
/// </para>
/// <para>
/// Del token solo se guarda el hash SHA-256, igual que con el token de refresco.
/// </para>
/// </summary>
public sealed class MfaDesafio
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>SHA-256 en hexadecimal del token del desafío. Único: es la búsqueda del token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>IP desde la que se emitió el desafío, para auditar el segundo factor con ella.</summary>
    public string Ip { get; set; } = string.Empty;

    public DateTimeOffset EmitidoAt { get; set; }

    /// <summary>Caducidad corta (5 minutos): el código TOTP caduca cada 30 s, no tiene sentido más.</summary>
    public DateTimeOffset CaducaAt { get; set; }

    /// <summary>Cuándo se gastó. Un desafío con este valor no vale para nada más.</summary>
    public DateTimeOffset? ConsumidoAt { get; set; }
}