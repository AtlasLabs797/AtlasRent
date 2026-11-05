namespace AtlasRent.Api.Data;

/// <summary>
/// Un intento de iniciar sesión, correcto o fallido (docs/SEGURIDAD.md A.2).
/// <para>
/// Es la fuente de verdad del bloqueo de cuenta y del de IP: el contador de fallos de una cuenta
/// son sus filas fallidas desde el último acierto, y el contador de una IP son sus filas fallidas
/// dentro de la ventana. Por eso se guardan también los intentos correctos: son los que marcan
/// desde dónde cuenta el contador.
/// </para>
/// <para>
/// Los intentos con usuario desconocido se guardan igual, con <see cref="UsuarioId"/> nulo: una
/// oleada de fuerza bruta contra nombres inventados también tiene que poder bloquear una IP.
/// </para>
/// </summary>
public sealed class IntentoLogin
{
    public Guid Id { get; set; }

    /// <summary>Nulo si el nombre de acceso no existe en la base de datos.</summary>
    public Guid? UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>
    /// Nombre de acceso tal y como llegó, ya normalizado. Se guarda porque con un usuario
    /// desconocido es lo único que identifica el intento.
    /// </summary>
    public string? NombreAcceso { get; set; }

    /// <summary>IP de origen. Obligatoria: el bloqueo por IP se calcula con ella.</summary>
    public string Ip { get; set; } = string.Empty;

    public bool Exito { get; set; }

    public DateTimeOffset OcurridoAt { get; set; }
}