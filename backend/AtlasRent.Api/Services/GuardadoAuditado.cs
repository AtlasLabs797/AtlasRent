using System.Text.Json;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Guarda una fila con el mismo contrato en todas las entidades de F1-09 (docs/DOC_TECNICA.md §6 y
/// AGENTS.md §6): una transacción, la auditoría con IP dentro de esa misma transacción y la
/// respuesta guardada para que un reintento con la misma <c>Idempotency-Key</c> no vuelva a
/// ejecutar la mutación.
/// <para>
/// <paramref name="datosAuditoria"/> lo decide el llamante y nunca lleva el documento de identidad
/// ni el texto cifrado.
/// </para>
/// <param name="antesDeGuardar">Cambios que van en la misma transacción que la fila, como los
/// bloqueos de disponibilidad de una reserva (F2-03).</param>
/// <param name="enTransaccionDelLlamador">
/// <c>true</c> cuando quien llama ya tiene una transacción abierta: el servicio de contratos
/// (F2-05), que aprueba el contrato y marca la reserva en conflicto en un solo commit. Entonces no
/// se abre otra ni se confirma, se usa la de fuera y un fallo devuelve el control para que esa
/// transacción se deshaga entera.
/// </param>
/// </summary>
internal static class GuardadoAuditado
{
    public static async Task<TResultado> GuardarAsync<TEntidad, TResultado>(
        AtlasRentDbContext contexto,
        AuditoriaService auditoria,
        ServicioIdempotencia idempotencia,
        TimeProvider reloj,
        DbSet<TEntidad> conjunto,
        TEntidad fila,
        bool esAlta,
        string accion,
        string entidad,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        Func<TEntidad, TResultado> proyectar,
        Func<TEntidad, object> datosAuditoria,
        Func<Task>? antesDeGuardar = null,
        Func<Task>? despuesDeGuardar = null,
        bool enTransaccionDelLlamador = false,
        CancellationToken ct = default)
        where TEntidad : class, IEntidadTrazada
        where TResultado : notnull
    {
        var ahora = reloj.GetUtcNow();

        await using var transaccion = enTransaccionDelLlamador
            ? null
            : await contexto.Database.BeginTransactionAsync(ct);

        if (esAlta)
        {
            fila.Id = Guid.NewGuid();
            fila.CreatedAt = ahora;
            fila.CreatedBy = usuario;
            conjunto.Add(fila);
        }
        else
        {
            fila.UpdatedAt = ahora;
            fila.UpdatedBy = usuario;
        }

        if (antesDeGuardar is not null)
        {
            await antesDeGuardar();
        }

        await contexto.SaveChangesAsync(ct);

        if (despuesDeGuardar is not null)
        {
            await despuesDeGuardar();
        }

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = accion,
            Ip = ip,
            UsuarioId = usuario,
            Entidad = entidad,
            EntidadId = fila.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(datosAuditoria(fila)),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        var respuesta = proyectar(fila);

        await idempotencia.RegistrarAsync(usuario, mutacion, respuesta, ct);

        if (transaccion is not null)
        {
            await transaccion.CommitAsync(ct);
        }

        return respuesta;
    }
}