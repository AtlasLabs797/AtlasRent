using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta y edición de una cotización (docs/ESPECIFICACION.md §3.3). Solo se acepta en borrador: si
/// la cotización ya salió, se edita una copia, no la que se envió.
/// <para>
/// Las reglas de negocio no van aquí como atributos sino en el servicio, que devuelve el conflicto
/// en 409; los atributos solo cubren lo que el modelo puede rechazar sin abrir la base de datos.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CotizacionPeticion
{
    /// <summary>Cliente al que se cotiza. Exactamente uno de <see cref="ClienteId"/> o <see cref="ProspectoId"/>.</summary>
    public Guid? ClienteId { get; init; }

    /// <summary>Prospecto al que se cotiza. Exactamente uno de los dos campos de destinatario.</summary>
    public Guid? ProspectoId { get; init; }

    /// <summary>Unidad cotizada, si la cotización es sobre una unidad concreta.</summary>
    public Guid? UnidadId { get; init; }

    /// <summary>
    /// Moneda única de todo el documento (D7). No hay conversión: si el cliente pide otra moneda se
    /// rehace la cotización (docs/ESPECIFICACION.md §3.8).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    [Required]
    [MinLength(1)]
    public required IReadOnlyList<CotizacionLineaPeticion> Lineas { get; init; }
}

/// <summary>Línea de la petición. El importe lo calcula la aplicación, no quien la escribe.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CotizacionLineaPeticion
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Descripcion { get; init; }

    /// <summary>Cantidad ofrecida. No negativa: una cantidad negativa no es una cantidad.</summary>
    public decimal Cantidad { get; init; }

    /// <summary>Precio unitario en la moneda de la cotización. No negativo.</summary>
    public decimal PrecioUnitario { get; init; }
}

/// <summary>
/// Cambio de estado de una cotización. El destino es el que pide el flujo D18; si no es un camino
/// válido, el servicio lo rechaza con 409.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CotizacionEstadoPeticion
{
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoCotizacion>))]
    public required EstadoCotizacion Estado { get; init; }
}

/// <summary>
/// Fila de la lista y respuesta de las mutaciones. Sin líneas: la lista se pagina y traerlas todas
/// en cada fila no sirve de nada.
/// </summary>
public sealed record CotizacionResumen(
    Guid Id,
    Guid? ClienteId,
    string? ClienteNombre,
    Guid? ProspectoId,
    string? ProspectoNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoCotizacion>))] EstadoCotizacion Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Subtotal,
    decimal Total,
    int Lineas,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>Cotización con sus líneas, que es como la lee el detalle y el PDF.</summary>
public sealed record CotizacionRespuesta(
    Guid Id,
    Guid? ClienteId,
    string? ClienteNombre,
    Guid? ProspectoId,
    string? ProspectoNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoCotizacion>))] EstadoCotizacion Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Subtotal,
    decimal Total,
    bool Activo,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CotizacionLineaRespuesta> Lineas);

/// <summary>Línea tal y como se guarda y tal y como se imprime.</summary>
public sealed record CotizacionLineaRespuesta(
    int Posicion,
    string Descripcion,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal Importe);

/// <summary>
/// Datos copiados de una cotización aceptada, listos para rellenar el formulario de reserva
/// (F2-03) o el de contrato (F2-05). Los dos endpoints devuelven lo mismo porque hoy la cotización
/// no guarda fechas ni condiciones: lo que hay es a quién se cotiza, en qué unidad, en qué moneda y
/// con qué líneas.
/// <para>
/// No crea nada. La especificación (docs/ESPECIFICACION.md §3.3) lo dice claro: el botón copia los
/// datos al formulario y es la persona quien confirma.
/// </para>
/// </summary>
public sealed record CotizacionTransferenciaRespuesta(
    Guid CotizacionId,
    Guid? ClienteId,
    string? ClienteNombre,
    Guid? ProspectoId,
    string? ProspectoNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Total,
    IReadOnlyList<CotizacionLineaRespuesta> Lineas);