using System.Text.Json;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Reintentos de mutaciones con <c>Idempotency-Key</c> (AGENTS.md §6: un evento repetido no crea
/// dos documentos). La fila se guarda en la transacción de la mutación, así que una clave solo
/// queda gastada si la mutación llegó a commitear.
/// </summary>
public sealed class ServicioIdempotencia(AtlasRentDbContext contexto, TimeProvider reloj)
{
    /// <summary>
    /// Mismas opciones que usa la API para responder (camelCase): el cuerpo guardado se devuelve
    /// tal cual en el reintento y tiene que ser indistinguible del de la primera vez.
    /// </summary>
    private static readonly JsonSerializerOptions OpcionesRespuesta = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new AtlasRent.Api.DTOs.ImporteJsonConverter(),
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
        },
    };
    /// <summary>
    /// Respuesta ya guardada para esta clave, o <c>null</c> si es la primera vez.
    /// <para>
    /// Es una lectura sin seguimiento: la fila la escribió otra petición y no tiene nada que ver
    /// con el cambio de estado de esta.
    /// </para>
    /// </summary>
    public async Task<string?> ReplayAsync(
        Guid usuarioId,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        if (mutacion is null)
        {
            return null;
        }

        var fila = await contexto.Idempotencia
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.UsuarioId == usuarioId && i.Clave == mutacion.Clave, ct);

        if (fila is null)
        {
            return null;
        }

        if (fila.Huella != mutacion.Huella)
        {
            throw new IdempotenciaEnConflictoException(mutacion.Clave);
        }

        return fila.Respuesta.RootElement.GetRawText();
    }

    /// <summary>
    /// Guarda la respuesta de la mutación. Se llama dentro de su transacción, antes del commit: si
    /// la mutación falla, la clave queda libre.
    /// </summary>
    public async Task RegistrarAsync(
        Guid usuarioId,
        MutacionIdempotente? mutacion,
        object respuesta,
        CancellationToken ct)
    {
        if (mutacion is null)
        {
            return;
        }

        await contexto.SaveChangesAsync(ct);

        contexto.Idempotencia.Add(new Idempotencia
        {
            Id = Guid.NewGuid(),
            Clave = mutacion.Clave,
            UsuarioId = usuarioId,
            Ruta = mutacion.Ruta,
            Huella = mutacion.Huella,
            Respuesta = JsonDocument.Parse(JsonSerializer.Serialize(respuesta, OpcionesRespuesta)),
            CreatedAt = reloj.GetUtcNow(),
        });

        await contexto.SaveChangesAsync(ct);
    }
}