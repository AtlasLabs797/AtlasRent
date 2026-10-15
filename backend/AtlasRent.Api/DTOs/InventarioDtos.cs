using AtlasRent.Api.Dominio;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta o corrección de un elemento del inventario base de la unidad (F2-08, §3.1).
/// <para>
/// Lo que no es inválido aquí sale con <b>400</b> y no con 409: un elemento sin nombre o con cantidad
/// negativa no es un conflicto con el estado de nadie, es un cuerpo que no describe un mueble.
/// </para>
/// </summary>
public sealed class InventarioBaseItemPeticion : IValidatableObject
{
    [Required]
    [StringLength(120)]
    public string? Nombre { get; set; }

    [Required]
    public decimal? Cantidad { get; set; }

    [StringLength(1000)]
    public string? Notas { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Cantidad is { } cantidad && cantidad < 0)
        {
            // Negative es una cantidad: "hay -3 sillas" describe un error de tecleo, no un mueble.
            yield return new ValidationResult(
                "La cantidad no puede ser negativa. Si no hay ninguno, la cantidad es cero y se anota en la nota.",
                [nameof(Cantidad)]);
        }
    }
}

/// <summary>Fila del inventario base de la unidad.</summary>
public sealed record InventarioBaseItemRespuesta(
    Guid Id,
    Guid UnidadId,
    string Nombre,
    decimal Cantidad,
    string? Notas,
    int Orden,
    bool Activo,
    DateTimeOffset CreatedAt);

/// <summary>
/// Elemento del inventario congelado del contrato. Solo lectura: no hay ninguna forma de escribirlo
/// después de la activación (docs/ESPECIFICACION.md §3.5).
/// </summary>
public sealed record ContratoInventarioItemRespuesta(
    Guid Id,
    Guid? InventarioBaseId,
    string Nombre,
    decimal Cantidad,
    string? Notas,
    int Orden);

/// <summary>
/// Línea de la revisión de salida: el estado de un elemento del inventario congelado.
/// <para>
/// Referencia el elemento por su identificador, no por su nombre: el nombre puede estar repetido y
/// porque el que se firma es el inventario congelado, no el catálogo.
/// </para>
/// </summary>
public sealed class RevisionInventarioLineaPeticion : IValidatableObject
{
    [Required]
    public Guid? ContratoInventarioItemId { get; set; }

    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoRevisionInventario>))]
    public EstadoRevisionInventario? Estado { get; set; }

    [StringLength(1000)]
    public string? Nota { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Estado is EstadoRevisionInventario.Ausente or EstadoRevisionInventario.Danado
            && string.IsNullOrWhiteSpace(Nota))
        {
            // Solo para lo que no está bien. En "presente" la nota es opcional: obligar a escribir
            // algo cuando no hay nada que escribir produce relleno que luego hay que leer.
            // TODO(A#) F2-08: la especificación §3.6 no dice si la nota es obligatoria en "ausente" y
            // "dañado". Aquí se exige, porque es donde la nota es lo que explica el importe que se
            // va a proponer. Si el orquestador decide lo contrario, se quita este bloque y el test que
            // lo cubre: es una decisión de negocio, no técnica.
            yield return new ValidationResult(
                "Un elemento ausente o dañado necesita su nota: es lo que explica el faltante.",
                [nameof(Nota)]);
        }
    }
}

/// <summary>
/// Firma de la revisión de salida (F2-08, §3.6). Se manda <b>entera</b> y con las líneas de todos los
/// elementos del inventario congelado: la revisión dice el estado de cada elemento (§3.6, "estado de
/// cada elemento"), no solo de los que faltan.
/// </summary>
public sealed class RevisionInventarioPeticion : IValidatableObject
{
    [Required]
    [MinLength(1)]
    public List<RevisionInventarioLineaPeticion>? Lineas { get; set; }

    /// <summary>
    /// Importe del faltante que el gerente deja constancia, en la moneda del contrato. No es un
    /// cobro: la tabla <c>cobro</c> es de F3-01. F2-09 liquida contra esta cifra.
    /// </summary>
    public decimal? ImportePropuesto { get; set; }

    [StringLength(1000)]
    public string? Observacion { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ImportePropuesto is { } importe && importe < 0)
        {
            // El faltante es lo que se pide al cliente: nunca negativo. Un importe negativo aquí
            // significaría un saldo a favor que nadie ha pedido.
            yield return new ValidationResult(
                "El importe propuesto del faltante no puede ser negativo.",
                [nameof(ImportePropuesto)]);
        }
    }
}

/// <summary>La revisión de salida ya firmada, con sus líneas.</summary>
public sealed record RevisionInventarioRespuesta(
    Guid Id,
    Guid ContratoId,
    DateTimeOffset FirmadaAt,
    Guid? FirmadaPorId,
    decimal? ImportePropuesto,
    string? Observacion,
    List<RevisionInventarioLineaRespuesta> Lineas);

/// <summary>Línea de la revisión firmada, con el nombre del elemento que se revisó.</summary>
public sealed record RevisionInventarioLineaRespuesta(
    Guid Id,
    Guid ContratoInventarioItemId,
    string Nombre,
    decimal Cantidad,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoRevisionInventario>))]
    EstadoRevisionInventario Estado,
    string? Nota);