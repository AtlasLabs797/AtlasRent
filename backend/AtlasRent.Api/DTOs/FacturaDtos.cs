using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta y edición de la cabecera de una factura (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Las líneas van aparte, por su propio endpoint: son las que se dan de alta, se editan y se borran
/// mientras el documento está en borrador, y metidas aquí obligarían a reescribir la factura entera
/// para corregir un descuento.
/// </para>
/// <para>
/// Las reglas de negocio no van aquí como atributos sino en el servicio, que devuelve el conflicto
/// en 409; los atributos solo cubren lo que el modelo puede rechazar sin abrir la base de datos.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaPeticion
{
    /// <summary>Cliente al que se factura. Obligatorio: una factura siempre es para alguien.</summary>
    [Required]
    public required Guid ClienteId { get; init; }

    /// <summary>Unidad facturada, si la factura viene de una unidad concreta (§3.8).</summary>
    public Guid? UnidadId { get; init; }

    /// <summary>Contrato de origen, si la factura viene de un contrato. Nulo si es manual.</summary>
    public Guid? ContratoId { get; init; }

    /// <summary>
    /// Serie de numeración (§3.7). Es la que fija la moneda cuando la tiene fija y la que decide si
    /// lo que sale es una factura normal, fiscal o una proforma.
    /// </summary>
    [Required]
    public required Guid SerieId { get; init; }

    /// <summary>
    /// Moneda única de todo el documento (D7). No hay conversión en ningún sitio de la factura.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>Día de vencimiento, en día de negocio (§3.13).</summary>
    [Required]
    public required DateOnly FechaVencimiento { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Concepto { get; init; }
}

/// <summary>
/// Línea de una factura. El importe y su ITBIS los calcula la aplicación, no quien la escribe
/// (§3.8, D27).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaLineaPeticion
{
    [JsonConverter(typeof(JsonStringEnumConverter<TipoLineaFactura>))]
    [Required]
    public required TipoLineaFactura Tipo { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Concepto { get; init; }

    /// <summary>
    /// Cantidad facturable, con cuatro decimales. En una línea de tipo <see cref="TipoLineaFactura.Descuento"/>
    /// el importe sale en negativo: lo que decide el signo es la cantidad o el precio, no el tipo.
    /// </summary>
    public decimal Cantidad { get; init; }

    /// <summary>Precio unitario en la moneda de la factura.</summary>
    public decimal PrecioUnitario { get; init; }

    /// <summary>
    /// Tipo de ITBIS de la línea. Nulo si la línea no lleva impuesto: el ITBIS es opcional por línea
    /// (§3.8). El porcentaje se copia a la línea al guardarla, y de ahí no vuelve a cambiar.
    /// </summary>
    public Guid? TipoItbisId { get; init; }
}

/// <summary>
/// Fila de la lista: la cabecera y los totales, sin las líneas. La lista se pagina y traerlas
/// todas en cada fila no sirve de nada.
/// <para>
/// <b>Sin importes agregados</b> ni totales por periodo: los informes son de §3.15 y solo gerente y
/// administrador los ven (F3-10).
/// </para>
/// </summary>
public sealed record FacturaResumen(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    Guid? ContratoId,
    Guid SerieId,
    int SerieNumero,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie TipoSerie,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly? FechaEmision,
    DateOnly FechaVencimiento,
    string Concepto,
    decimal Subtotal,
    decimal Itbis,
    decimal Total,
    int Lineas,
    DateTimeOffset CreatedAt);

/// <summary>
/// Factura con sus líneas, que es como la lee el detalle, el PDF y la vista de cuentas (§3.13).
/// <para>
/// <b>No expone cobrado, pendiente ni estado de cobro</b>: dependen de <c>cobro</c>, que no existe
/// hasta F3-06, y la retención es de F3-04. Un campo que sale a cero y miente es peor que un campo
/// que no existe.
/// </para>
/// </summary>
public sealed record FacturaRespuesta(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    Guid? ContratoId,
    Guid SerieId,
    int SerieNumero,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie TipoSerie,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly? FechaEmision,
    DateOnly FechaVencimiento,
    string Concepto,
    decimal Subtotal,
    decimal Itbis,
    decimal Total,
    decimal? TipoCambioValor,
    DateOnly? TipoCambioFecha,
    Guid? TipoCambioId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<FacturaLineaRespuesta> Lineas);

/// <summary>
/// Línea tal y como se guarda y tal y como se imprime. El porcentaje de ITBIS va porque es el dato
/// congelado de la línea, no una referencia a la configuración de hoy.
/// </summary>
public sealed record FacturaLineaRespuesta(
    Guid Id,
    int Posicion,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoLineaFactura>))] TipoLineaFactura Tipo,
    string Concepto,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    Guid? TipoItbisId,
    string? TipoItbisNombre,
    decimal? PorcentajeItbis,
    decimal ItbisImporte);
