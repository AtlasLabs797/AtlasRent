using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Data;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Consulta del asistente (F6-02). El alcance lo elige el cliente y es la <b>única</b> cosa que
/// decide qué datos se cargan: el modelo no elige ni una tabla ni una fila.
/// <para>
/// <b>Un miembro desconocido es un 400</b>, no una clave ignorada: un <c>alcance</c> mal escrito que
/// se aceptara en silencio cargaría unos datos que el usuario no pidió.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConsultaIaPeticion
{
    /// <summary>Lo que pregunta el usuario, en español.</summary>
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Pregunta { get; set; } = string.Empty;

    /// <summary>
    /// Datos que se cargan: <c>clientes</c>, <c>cuentas</c>, <c>facturas</c>, <c>contratos</c> o
    /// <c>informes</c>. Ver <see cref="Services.ServicioConsultaIa.Alcances"/>.
    /// </summary>
    [Required]
    public string Alcance { get; set; } = string.Empty;
}

/// <summary>
/// Respuesta del asistente. <b>Contrato cerrado con el frontend</b>
/// (<c>frontend/src/ia/api.ts</c>): cuatro campos, en camelCase, y nada más.
/// </summary>
/// <param name="Respuesta">Texto en español, ya con los nombres reales puestos.</param>
/// <param name="Alcance">Alcance con el que se respondió.</param>
/// <param name="Proveedor">Proveedor que contestó.</param>
/// <param name="Modelo">Modelo de ese proveedor, tal y como se configura.</param>
public sealed record ConsultaIaRespuesta(
    string Respuesta,
    string Alcance,
    string Proveedor,
    string Modelo);

/// <summary>
/// Propuesta de acción del asistente (F6-03). <b>Proponer no es hacer</b>: aquí solo se escribe una
/// fila de <c>solicitud_aprobacion</c> en pendiente y nada más ocurre hasta que alguien con permiso
/// la aprueba.
/// <para>
/// Solo hay un tipo de propuesta en esta fase —mandar el aviso de saldo que ya está preparado—, y se
/// dice por su nombre para que mañana añadir otro sea un cambio de una línea y no una migración.
/// Un tipo desconocido es un 400.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PropuestaIaPeticion
{
    /// <summary>Cliente al que se le quiere recordar el saldo. Va por identificador, no por nombre.</summary>
    [Required]
    public Guid ClienteId { get; set; }

    /// <summary>Qué se propone. Hoy, solo <c>recordatorio_saldo</c>.</summary>
    [Required]
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// Por qué lo propone el asistente. Se guarda como comentario legible de la propuesta y
    /// <b>no llega a la acción</b>: aprobar ejecuta el aviso con sus propios datos, no con lo que
    /// escribió el modelo.
    /// </summary>
    [StringLength(1000)]
    public string? Justificacion { get; set; }
}

/// <summary>
/// Estado de una propuesta. <see cref="Resultado"/> dice si sigue esperando; si sale
/// <c>Caducada</c>, <see cref="MotivoCaducidad"/> explica que el saldo cambió y hay que proponer otra.
/// </summary>
public sealed record PropuestaIaRespuesta(
    Guid Id,
    string Tipo,
    Guid ClienteId,
    string Cliente,
    Guid RecordatorioId,
    string Seudonimo,
    string Pendiente,
    string HuellaContenido,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ResultadoSolicitud>))]
    ResultadoSolicitud Resultado,
    string? MotivoCaducidad,
    string? Comentario,
    DateTimeOffset SolicitadoAt,
    string? SolicitadoPor,
    DateTimeOffset? ResueltoAt,
    string? ResueltoPor);

/// <summary>
/// Rechazo de una propuesta. El comentario es obligatorio por la misma razón que en el contrato
/// (§3.5): una propuesta rechazada sin explicación no se puede distinguir de una que caducó.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PropuestaIaRechazoPeticion
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Comentario { get; set; } = string.Empty;
}