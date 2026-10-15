namespace AtlasRent.Api.Data;

/// <summary>
/// Subempresa propietaria o facturadora (docs/ESPECIFICACION.md §3.7 y §3.8: la factura exige
/// facturadora y el contrato, propietario).
/// <para>
/// No se guarda RNC: cuando se añada tiene que ir cifrado con AES-256-GCM y con índice ciego HMAC
/// (AGENTS.md §6), que es trabajo de su propia tarea.
/// </para>
/// </summary>
public sealed class Subempresa : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Es propietaria de unidades.</summary>
    public bool Propietaria { get; set; }

    /// <summary>Emite facturas a nombre propio.</summary>
    public bool Facturadora { get; set; }

    public bool Activa { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}