namespace AtlasRent.Api.Dominio;

/// <summary>
/// Qué clase de documento es el que se manda por correo (docs/ESPECIFICACION.md §3.14, tabla
/// <c>envio</c>).
/// <para>
/// <b>No hay un valor para la proforma</b>, y no es un descuido: proforma y factura son filas de la
/// misma tabla <c>factura</c> y se distinguen por <c>estado</c> (una proforma está en
/// <c>Borrador</c>, F3-02), así que un <see cref="Factura"/> cubre las dos y el envío de una
/// proforma se registra como <c>Factura</c>. Meter un valor más obligaría a inventar la conversión
/// entre los dos en cada consulta y el filtro «enviar un documento» de §3.14 seguiría siendo el
/// mismo.
/// </para>
/// </summary>
public enum TipoDocumentoEnvio
{
    /// <summary>Factura o proforma (<c>factura.id</c>).</summary>
    Factura = 1,

    /// <summary>Recibo de un cobro (<c>cobro.id</c>).</summary>
    Recibo = 2,

    /// <summary>
    /// Estado de cuenta del cliente (§3.13). No tiene fila propia: se identifica por cliente y
    /// año, así que en <c>envio</c> van el identificador del cliente en <c>documento_id</c> y el
    /// año en <c>documento_anio</c>, que para todos los demás tipos queda en <c>NULL</c>.
    /// </summary>
    EstadoCuenta = 3,

    /// <summary>Cotización (<c>cotizacion.id</c>).</summary>
    Cotizacion = 4,

    /// <summary>Contrato (<c>contrato.id</c>, el PDF que compone <c>ServicioPlantillas</c>).</summary>
    Contrato = 5,
}