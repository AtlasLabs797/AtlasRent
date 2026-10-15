using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Lo que el front manda para crear o editar un contrato. Todo lo obligatorio llega aquí como
/// opcional a propósito: la comprobación de "falta esto" la hace el servicio con un mensaje de
/// negocio (409), que es más útil que un 400 de validación de modelo.
/// </summary>
public sealed class ContratoPeticion
{
    public Guid? ClienteId { get; set; }

    public Guid? UnidadId { get; set; }

    public Guid? FacturadoraId { get; set; }

    public DateOnly? FechaInicio { get; set; }

    public int? DuracionInicialMeses { get; set; }

    public decimal? Renta { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public Moneda? Moneda { get; set; }

    public int? DiaPago { get; set; }

    public decimal? Fianza { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ReglaRenovacion>))]
    public ReglaRenovacion ReglaRenovacion { get; set; }

    public int? RenovacionMeses { get; set; }

    [StringLength(120)]
    public string? PlantillaContratoId { get; set; }

    [StringLength(1000)]
    public string? Observaciones { get; set; }
}

/// <summary>
/// Cambio en un contrato ya activo (docs/ESPECIFICACION.md §3.5: "un cambio en contrato activo
/// genera versión nueva" y "el cambio de renta afecta solo a facturas futuras").
/// <para>
/// Deliberadamente <b>no</b> trae cliente, unidad, facturadora, fecha de inicio ni duración
/// inicial: en un contrato vigente no se cambian. Cambiar de titular, de unidad o de fechas es otro
/// contrato, y dejarlo escribible aquí obligaría a rehacer el candado de la unidad con la misma
/// operación que cambia la renta, que es justo lo que no quiere pasar.
/// </para>
/// </summary>
public sealed class ContratoCambioPeticion
{
    public decimal? Renta { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public Moneda? Moneda { get; set; }

    public int? DiaPago { get; set; }

    public decimal? Fianza { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ReglaRenovacion>))]
    public ReglaRenovacion ReglaRenovacion { get; set; }

    public int? RenovacionMeses { get; set; }

    [StringLength(120)]
    public string? PlantillaContratoId { get; set; }

    [StringLength(1000)]
    public string? Observaciones { get; set; }

    /// <summary>
    /// Por qué se cambia. Va escrito en la versión, así que no es opcional: una versión sin motivo no
    /// explica nada dentro de seis meses.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string? Motivo { get; set; }
}

/// <summary>Rechazo de la solicitud de aprobación. El comentario es obligatorio (§3.5).</summary>
public sealed class ContratoRechazoPeticion
{
    [Required]
    [StringLength(1000)]
    public string? Comentario { get; set; }
}

/// <summary>Nota con la que el empleado manda el contrato a aprobación.</summary>
public sealed class ContratoEnvioPeticion
{
    [StringLength(1000)]
    public string? Comentario { get; set; }
}

/// <summary>
/// Baja programada con su fecha efectiva de fin (§3.5: preaviso recomendado de 30 días, sin
/// penalización). La fecha es obligatoria: sin ella no hay baja, hay una intención.
/// </summary>
public sealed class ContratoBajaPeticion
{
    [Required]
    public DateOnly? FechaFin { get; set; }

    [StringLength(1000)]
    public string? Comentario { get; set; }
}

/// <summary>Fila del listado de contratos.</summary>
public sealed record ContratoResumen(
    Guid Id,
    Guid ClienteId,
    string? Cliente,
    Guid UnidadId,
    string? Unidad,
    Guid FacturadoraId,
    DateOnly FechaInicio,
    DateOnly FechaFinVigente,
    decimal Renta,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    EstadoContrato Estado,
    DateOnly? EstadoActualDesde,
    DateOnly? FechaBajaProgramada,
    DateTimeOffset CreatedAt);

/// <summary>Una versión del historial, tal como se devuelve.</summary>
public sealed record ContratoVersionRespuesta(
    int Numero,
    string Cambio,
    decimal? RentaAnterior,
    decimal? RentaNueva,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    string Alcance,
    DateTimeOffset CreatedAt);

/// <summary>La solicitud de aprobación vigente (o la última que hubo).</summary>
public sealed record SolicitudAprobacionRespuesta(
    Guid Id,
    ResultadoSolicitud Resultado,
    string HuellaContenido,
    DateTimeOffset SolicitadoAt,
    Guid? ResueltoPorId,
    DateTimeOffset? ResueltoAt,
    string? Comentario,
    string? MotivoCaducidad);

/// <summary>Ficha completa del contrato.</summary>
public sealed record ContratoRespuesta(
    Guid Id,
    Guid ClienteId,
    string? Cliente,
    Guid UnidadId,
    string? Unidad,
    Guid FacturadoraId,
    string? Facturadora,
    DateOnly FechaInicio,
    int DuracionInicialMeses,
    DateOnly FechaFinInicial,
    DateOnly FechaFinVigente,
    decimal Renta,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    int DiaPago,
    decimal Fianza,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ReglaRenovacion>))]
    ReglaRenovacion ReglaRenovacion,
    int RenovacionMeses,
    string PlantillaContratoId,
    string? PlantillaContratoCongeladaId,
    DateTimeOffset? PlantillaCongeladaAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoContrato>))]
    EstadoContrato Estado,
    DateOnly? EstadoActualDesde,
    string? EstadoActualComentario,
    DateOnly? FechaBajaProgramada,
    DateOnly? FechaFinReal,
    string? Observaciones,
    IReadOnlyList<ContratoVersionRespuesta> Versiones,
    IReadOnlyList<SolicitudAprobacionRespuesta> Solicitudes,
    DateTimeOffset CreatedAt);