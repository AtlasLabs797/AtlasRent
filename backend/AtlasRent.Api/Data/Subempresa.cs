namespace AtlasRent.Api.Data;

/// <summary>
/// Subempresa propietaria o facturadora (docs/ESPECIFICACION.md §3.7 y §3.8: la factura exige
/// facturadora y el contrato, propietario).
/// <para>
/// F4-01: el RNC y los datos de contacto se guardan porque el PDF de factura los imprime en la
/// cabecera (docs/DESIGN.md §7). Antes no había ningún dato de contacto de la facturadora, así que
/// la cabecera no tenía nada que poner.
/// </para>
/// <para>
/// El <see cref="Rnc"/> va cifrado en reposo con el mismo <see cref="Services.ServicioCifrado"/> y
/// el mismo formato <c>v1:nonce:cifrado</c> que el documento del cliente (AGENTS.md §6). Se ve
/// completo en la ficha y en la cabecera del PDF de factura (docs/DESIGN.md §7), con el mismo
/// tratamiento que aquel (D16).
/// </para>
/// <para>
/// A diferencia del documento del cliente, el RNC <b>no lleva índice ciego</b>: no hay ningún
/// endpoint que busque subempresas por RNC —la lista solo ordena por nombre—, así que una columna
/// <c>rnc_hmac</c> sería un valor que nadie consulta. El índice ciego existe para buscar y detectar
/// duplicados sin descifrar, y aquí no hay ninguna de las dos cosas que hacer.
/// </para>
/// </summary>
public sealed class Subempresa : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Registro Nacional de Contribuyentes de la subempresa, **cifrado en reposo** con
    /// AES-256-GCM: <c>v1:nonce:cifrado</c>, igual que <see cref="Cliente.Documento"/>. Nulo si
    /// todavía no se ha configurado, que es el caso de una empresa dada de alta por importación.
    /// </summary>
    public string? Rnc { get; set; }

    /// <summary>
    /// Domicilio fiscal. Va en la cabecera del PDF de factura (docs/DESIGN.md §7). Nulo si no se
    /// ha configurado: es un dato de contacto, no un identificador, y la cabecera lo salta.
    /// </summary>
    public string? Direccion { get; set; }

    /// <summary>Teléfono de la subempresa, tal y como lo publica para que lo llame el cliente.</summary>
    public string? Telefono { get; set; }

    /// <summary>Correo de la subempresa. Es el contacto que se imprime, no el de un usuario.</summary>
    public string? Email { get; set; }

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