using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Prospecto (docs/ESPECIFICACION.md §3.2): alguien que todavía no es cliente y al que se le
/// quiere cotizar. Es una entidad aparte y no tiene número de cliente: lo recibe el día que se
/// convierte, y ese número no se reutiliza después.
/// </summary>
public sealed class Prospecto : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Nombre de la persona física o razón social de la empresa.</summary>
    public string Nombre { get; set; } = string.Empty;

    public TipoCliente Tipo { get; set; }

    /// <summary>Documento de identidad cifrado en reposo: <c>v1:nonce:cifrado</c>.</summary>
    public string? Documento { get; set; }

    /// <summary>HMAC-SHA256 del documento normalizado: índice ciego y único.</summary>
    public string? DocumentoHmac { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public string? Notas { get; set; }

    /// <summary>Baja lógica, por la misma razón que en <see cref="Cliente"/>.</summary>
    public bool Activo { get; set; }

    /// <summary>Cliente en que se convirtió este prospecto (§3.2). Null mientras siga siendo prospecto.</summary>
    public Guid? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    public DateTimeOffset? ConvertidoAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}