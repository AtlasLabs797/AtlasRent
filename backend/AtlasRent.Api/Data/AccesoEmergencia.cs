namespace AtlasRent.Api.Data;

/// <summary>
/// Acceso de emergencia (break-glass) para recuperar el sistema cuando un administrador pierde su
/// dispositivo y sus códigos de recuperación (docs/DOC_TECNICA.md §11, runbook de admin bloqueado).
/// <para>
/// Hacen falta <b>dos</b> administradores distintos: el primero pide el acceso y el segundo lo
/// aprueba. Solo entonces existe un código de un solo uso con el que el administrador bloqueado
/// entra sin segundo factor.
/// </para>
/// <para>
/// De ese código solo se guarda el hash, como en los códigos de recuperación. Todo el proceso queda
/// en auditoría firmada, y la sesión que sale de aquí nace marcada como de emergencia.
/// </para>
/// </summary>
public sealed class AccesoEmergencia
{
    public Guid Id { get; set; }

    /// <summary>Administrador al que se le da el acceso: el que perdió el dispositivo.</summary>
    public Guid UsuarioId { get; set; }

    /// <summary>Administrador que pidió el acceso (la primera de las dos aprobaciones).</summary>
    public Guid SolicitanteId { get; set; }

    /// <summary>Segundo administrador que lo aprobó. Nula hasta que lo hay (F1-06).</summary>
    public Guid? AprobadoPorId { get; set; }

    /// <summary>Por qué se pide. Queda escrito en la auditoría, que es lo que la hace revisable.</summary>
    public string Motivo { get; set; } = string.Empty;

    public string Ip { get; set; } = string.Empty;

    /// <summary>SHA-256 en hexadecimal del código de acceso. Nula hasta la segunda aprobación.</summary>
    public string? CodigoHash { get; set; }

    public DateTimeOffset CreadoAt { get; set; }

    public DateTimeOffset? AprobadoAt { get; set; }

    /// <summary>Caducidad del código de acceso una vez aprobado.</summary>
    public DateTimeOffset? CaducaAt { get; set; }

    /// <summary>Cuándo se usó el código para entrar. Un acceso de emergencia no se repite.</summary>
    public DateTimeOffset? UsadoAt { get; set; }
}