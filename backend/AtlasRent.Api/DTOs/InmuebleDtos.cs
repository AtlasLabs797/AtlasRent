using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>Alta o cambio de un inmueble (docs/ESPECIFICACION.md §3.1).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class InmueblePeticion
{
    [Required]
    [UnaLinea]
    [StringLength(200, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [StringLength(300)]
    public string? Direccion { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoInmueble>))]
    public required TipoInmueble Tipo { get; init; }

    /// <summary>Subempresa propietaria. Debe existir en <c>subempresa</c>.</summary>
    public Guid? PropietariaId { get; init; }

    [StringLength(1000)]
    public string? Notas { get; init; }

    /// <summary>Plantilla de contrato que se propone por defecto en los contratos de este inmueble.</summary>
    [StringLength(120)]
    public string? PlantillaContratoPorDefecto { get; init; }

    /// <summary>Renovación por defecto en meses. Sin valor si no hay ninguna propuesta.</summary>
    [Range(1, 600)]
    public int? RenovacionDefectoMeses { get; init; }
}

/// <summary>
/// Inmueble en la lista y en las respuestas de alta y cambio. Sin unidades: la lista de un
/// inmueble con sus unidades es la ficha (<see cref="InmuebleRespuesta"/>).
/// </summary>
public sealed record InmuebleResumen(
    Guid Id,
    string Nombre,
    string? Direccion,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoInmueble>))] TipoInmueble Tipo,
    Guid? PropietariaId,
    string? PlantillaContratoPorDefecto,
    int? RenovacionDefectoMeses,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>Ficha del inmueble: el resumen más sus unidades con modalidades y tarifas.</summary>
public sealed record InmuebleRespuesta(
    Guid Id,
    string Nombre,
    string? Direccion,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoInmueble>))] TipoInmueble Tipo,
    Guid? PropietariaId,
    string? Notas,
    string? PlantillaContratoPorDefecto,
    int? RenovacionDefectoMeses,
    bool Activo,
    DateTimeOffset CreatedAt,
    IReadOnlyList<UnidadRespuesta> Unidades);

/// <summary>
/// Alta o cambio de una unidad (docs/ESPECIFICACION.md §3.1).
/// <para>
/// <see cref="Modalidades"/> es la lista de modalidades que la unidad ofrece, y
/// <see cref="Tarifas"/> tiene que estar dentro de ella: una tarifa de una modalidad no permitida
/// se rechaza.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UnidadPeticion
{
    [Required]
    [UnaLinea]
    [StringLength(120, MinimumLength = 1)]
    public required string Nombre { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<TipoUnidad>))]
    public required TipoUnidad Tipo { get; init; }

    /// <summary>Subempresa propietaria. Si no viene, se hereda la del inmueble.</summary>
    public Guid? PropietariaId { get; init; }

    /// <summary>Subempresa que factura por defecto en los contratos de esta unidad.</summary>
    public Guid? FacturadoraPorDefectoId { get; init; }

    /// <summary>
    /// Estado manual. No dice si la unidad está ocupada: eso se calcula por fechas.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoUnidad>))]
    public EstadoUnidad EstadoManual { get; init; } = EstadoUnidad.Disponible;

    /// <summary>
    /// Modalidades que ofrece la unidad. Tiene que traer al menos una: una unidad que no ofrece
    /// ninguna no se puede alquilar.
    /// </summary>
    [MinLength(1)]
    public required IReadOnlyList<ModalidadAlquiler> Modalidades { get; init; }

    /// <summary>Tarifas por modalidad y moneda. Cada modalidad debe estar permitida en <see cref="Modalidades"/>.</summary>
    public IReadOnlyList<TarifaPeticion> Tarifas { get; init; } = [];

    /// <summary>Metros cuadrados. No negativo.</summary>
    [Range(0, double.MaxValue)]
    public decimal? MetrosCuadrados { get; init; }

    [Range(0, int.MaxValue)]
    public int? Capacidad { get; init; }

    [Range(0, int.MaxValue)]
    public int? Habitaciones { get; init; }

    /// <summary>Baños con medios: 1,5 para un aseo y un medio.</summary>
    [Range(0, double.MaxValue)]
    public decimal? Banos { get; init; }

    [StringLength(120)]
    public string? Medidor { get; init; }

    [StringLength(1000)]
    public string? Notas { get; init; }
}

/// <summary>Tarifa de una modalidad en una moneda.</summary>
public sealed class TarifaPeticion
{
    [JsonConverter(typeof(JsonStringEnumConverter<ModalidadAlquiler>))]
    public required ModalidadAlquiler Modalidad { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>Importe de una sola modalidad en una sola moneda. Nunca negativo.</summary>
    public required decimal Importe { get; init; }
}

public sealed record TarifaRespuesta(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ModalidadAlquiler>))] ModalidadAlquiler Modalidad,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Importe);

/// <summary>
/// Unidad con sus modalidades permitidas y sus tarifas.
/// <para>
/// <strong>No trae "ocupada" ni "reservada"</strong>, y no es un olvido: la ocupación se calcula por
/// fechas (AGENTS.md §6). Lo que sí trae es el estado manual.
/// </para>
/// </summary>
public sealed record UnidadRespuesta(
    Guid Id,
    Guid InmuebleId,
    string Nombre,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoUnidad>))] TipoUnidad Tipo,
    Guid? PropietariaId,
    Guid? FacturadoraPorDefectoId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoUnidad>))] EstadoUnidad EstadoManual,
    IReadOnlyList<ModalidadAlquiler> Modalidades,
    IReadOnlyList<TarifaRespuesta> Tarifas,
    decimal? MetrosCuadrados,
    int? Capacidad,
    int? Habitaciones,
    decimal? Banos,
    string? Medidor,
    string? Notas,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>
/// Documento de propiedad en la lista: lo que se ve sin abrir el archivo.
/// <para>
/// No lleva la ruta del archivo. El PDF se descarga por su endpoint, que comprueba el permiso; por
/// eso aquí no hay nada que sirva.
/// </para>
/// </summary>
public sealed record DocumentoInmuebleRespuesta(
    Guid Id,
    Guid InmuebleId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CategoriaDocumento>))] CategoriaDocumento Categoria,
    int Version,
    string NombreOriginal,
    long TamanoBytes,
    string TipoContenido,
    DateTimeOffset CreatedAt);