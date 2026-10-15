using System.Text.Json;

namespace AtlasRent.Api.Data;

/// <summary>
/// Clave de idempotencia ya usada (docs/DOC_TECNICA.md §6, docs/ESPECIFICACION.md §3.17). Guarda
/// qué se respondió para poder devolver lo mismo si llega la misma mutación otra vez.
/// <para>
/// La fila se escribe en la misma transacción que la mutación: si esa mutación falla, la clave
/// queda libre y se puede reintentar.
/// </para>
/// </summary>
public sealed class Idempotencia
{
    public Guid Id { get; set; }

    /// <summary>Clave que envía el cliente en la cabecera <c>Idempotency-Key</c>.</summary>
    public string Clave { get; set; } = string.Empty;

    public Guid UsuarioId { get; set; }

    /// <summary>Ruta y método de la primera petición: solo para diagnóstico.</summary>
    public string Ruta { get; set; } = string.Empty;

    /// <summary>Huella del cuerpo de la primera petición, para detectar la misma clave con otro cuerpo.</summary>
    public string Huella { get; set; } = string.Empty;

    /// <summary>Cuerpo de la respuesta de la primera petición.</summary>
    public JsonDocument Respuesta { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}