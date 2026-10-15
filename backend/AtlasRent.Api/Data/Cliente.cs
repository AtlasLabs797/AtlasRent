using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Cliente (docs/ESPECIFICACION.md §3.2). <c>Documento</c> es la cédula, el pasaporte o el RNC:
/// se ve completo en la ficha (decisión D16) y se guarda cifrado con AES-256-GCM.
/// <para>
/// El número va en su propia columna porque conserva el número que ya existía en Factusol
/// (corrección técnica heredada de la v2.1) y los nuevos salen de la secuencia
/// <c>cliente_numero_seq</c>. Un número nunca se reutiliza.
/// </para>
/// </summary>
public sealed class Cliente : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Número de cliente. Lo asigna la secuencia salvo en la importación de Factusol.</summary>
    public int Numero { get; set; }

    /// <summary>Nombre de la persona física o razón social de la empresa.</summary>
    public string Nombre { get; set; } = string.Empty;

    public TipoCliente Tipo { get; set; }

    /// <summary>
    /// Documento de identidad cifrado en reposo: <c>v1:nonce:cifrado</c>. Es <c>null</c> si el
    /// cliente se dio de alta sin documento, que es el caso de los datos incompletos de importación.
    /// </summary>
    public string? Documento { get; set; }

    /// <summary>
    /// HMAC-SHA256 del documento normalizado: el índice ciego. Es único, y por eso detecta
    /// duplicados y permite buscar sin descifrar la columna anterior.
    /// </summary>
    public string? DocumentoHmac { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public string? Direccion { get; set; }

    public string? Notas { get; set; }

    /// <summary>Agente de retención: la plataforma le avisa cuando hay que retener.</summary>
    public bool AgenteRetencion { get; set; }

    /// <summary>En la facturación está en manos de la plataforma y no en las del cliente.</summary>
    public bool EnGestionCobro { get; set; }

    /// <summary>No se le avisa de vencimientos.</summary>
    public bool RecordatoriosPausados { get; set; }

    /// <summary>
    /// Baja lógica. Un cliente con efecto económico (facturas, contratos) nunca se borra
    /// (docs/ESPECIFICACION.md §3.2); sin efecto económico se desactiva y desaparece de la lista.
    /// </summary>
    public bool Activo { get; set; }

    public List<ContactoCliente> Contactos { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}