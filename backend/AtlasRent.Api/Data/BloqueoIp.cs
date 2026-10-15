namespace AtlasRent.Api.Data;

/// <summary>
/// Bloqueo de una IP por ataque de fuerza bruta (docs/SEGURIDAD.md A.2).
/// <para>
/// El escalado es 1 h → 24 h → indefinido: cada reincidencia dentro de las 24 h siguientes a la
/// expiración del bloqueo anterior sube un nivel. El bloqueo indefinido es el que no tiene
/// fecha de fin (<see cref="BloqueadaHasta"/> nula) y solo lo levanta el administrador.
/// </para>
/// <para>
/// La lista blanca de IP de oficina no se guarda aquí: está en
/// <c>Seguridad__IpListaBlanca</c> y el servicio no bloquea nunca una IP que esté en ella.
/// </para>
/// </summary>
public sealed class BloqueoIp
{
    public Guid Id { get; set; }

    /// <summary>IP bloqueada. Única: una fila por IP.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Nivel del escalado: 1 (1 h), 2 (24 h) o 3 (indefinido). 0 = nunca bloqueada.</summary>
    public int Nivel { get; set; }

    /// <summary>Fallos que franchement dispararon el bloqueo actual.</summary>
    public int Fallos { get; set; }

    /// <summary>
    /// Fin del bloqueo, o <c>null</c> si es indefinido. Un nivel 1 o 2 siempre tiene fecha.
    /// </summary>
    public DateTimeOffset? BloqueadaHasta { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}