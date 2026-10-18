using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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