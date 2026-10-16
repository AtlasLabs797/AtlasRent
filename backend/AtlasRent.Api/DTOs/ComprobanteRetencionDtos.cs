using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Recepción del comprobante de retención de una factura (F3-04, docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// <b>Solo el número y la fecha que dice el documento.</b> No se valida contra nada porque no hay a
/// qué validarlo: la aplicación no habla con la DGII y §3.9 no pide nada más. El número es lo que
/// el cliente escribe en el papel que entrega, y esa es toda la información que se conserva.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ComprobanteRetencionPeticion
{
    /// <summary>Número del comprobante, tal y como viene en el documento.</summary>
    [Required]
    [StringLength(120, MinimumLength = 1)]
    public required string Numero { get; init; }

    /// <summary>
    /// Fecha que dice el comprobante, en día de negocio. Obligatoria: un comprobante sin fecha no
    /// dice cuándo se retuvo, que es la mitad de lo que hay que poder responder después.
    /// </summary>
    [Required]
    public required DateOnly FechaRecepcion { get; init; }
}

/// <summary>
/// Comprobante de retención tal y como se devuelve: lo congelado en la emisión y lo que se-recibió
/// después.
/// <para>
/// Va por su propio endpoint y no dentro de la factura porque es lo único que cambia con el tiempo,
/// y porque quien llama a verlo es quien va a buscar el papel (F3-04).
/// </para>
/// </summary>
public sealed record ComprobanteRetencionRespuesta(
    Guid Id,
    Guid FacturaId,
    string Tipo,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] BaseRetencion Base,
    decimal Porcentaje,
    decimal Importe,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoComprobanteRetencion>))]
    EstadoComprobanteRetencion Estado,
    string? Numero,
    DateOnly? FechaRecepcion,
    DateTimeOffset? RecibidoAt,
    Guid? RecibidoPor,
    DateTimeOffset CreatedAt);