namespace AtlasRent.Api.Data;

/// <summary>
/// Contacto de un cliente (docs/ESPECIFICACION.md §3.2: contactos múltiples, con casillas de
/// correo y teléfono y uno por defecto). Se guardan separados y no en el cliente porque los
/// contactos cambian y el cliente no.
/// </summary>
public sealed class ContactoCliente : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid ClienteId { get; set; }

    public Cliente Cliente { get; set; } = null!;

    /// <summary>Nombre del contacto.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Relación con el cliente: "propietario", "inquilino", "fiador", "contable"...</summary>
    public string? Relacion { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    /// <summary>Contacto principal: el que se usa si no se elige otro (docs/ESPECIFICACION.md §3.2).</summary>
    public bool EsPrincipal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}