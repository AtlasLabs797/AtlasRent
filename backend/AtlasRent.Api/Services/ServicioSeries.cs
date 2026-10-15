using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Contador de series (docs/ESPECIFICACION.md §3.7, docs/DOC_TECNICA.md §4.3 y AGENTS.md §6).
/// <para>
/// El número se saca con <c>UPDATE serie SET siguiente = siguiente + 1 ... RETURNING siguiente - 1</c>,
/// dentro de la transacción que emite: si esa transacción falla, el número no se consume, y dos
/// emisiones simultáneas no pueden salir con el mismo porque la fila se bloquea hasta el commit.
/// Nunca se usa <c>MAX() + 1</c>.
/// </para>
/// <para>
/// Un número emitido no se reutiliza. Anularlo lo marca como anulado y nada más: ni el contador
/// retrocede ni el número vuelve a estar libre (D28).
/// </para>
/// </summary>
public sealed class ServicioSeries(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    TimeProvider reloj)
{
    /// <summary>
    /// Saca el siguiente número de una serie y deja constancia en <c>numero_serie</c>.
    /// <para>
    /// No audita por su cuenta: la auditoría es la de la emisión completa (factura, comprobante y
    /// número en la misma transacción, docs/DOC_TECNICA.md §5). Auditar aquí, además, serializaría a
    /// todas las emisiones con el bloqueo de la cadena de auditoría y ocultaría justo la
    /// concurrencia que esta operación tiene que aguantar.
    /// </para>
    /// </summary>
    /// <param name="usuarioId">Usuario que emite; queda en la fila del número.</param>
    /// <exception cref="KeyNotFoundException">La serie no existe o está desactivada.</exception>
    public async Task<int> ReservarNumeroAsync(Guid serieId, Guid usuarioId, CancellationToken ct)
    {
        // Si quien llama ya tiene una transacción abierta (la de la emisión), se usa esa: el número
        // tiene que salir o no salir junto con el documento.
        var transaccionPropia = contexto.Database.CurrentTransaction is null;

        await using var transaccion = transaccionPropia
            ? await contexto.Database.BeginTransactionAsync(ct)
            : null;

        // Un serie activa siempre tiene siguiente >= 1, así que el 0 del resultado solo puede
        // significar que la fila no existe o está desactivada. ToListAsync y no SingleOrDefaultAsync
        // porque un UPDATE ... RETURNING no admite composición de LINQ sobre él.
        var numeros = await contexto.Database
            .SqlQuery<int>(
                $"""
                 UPDATE serie SET siguiente = siguiente + 1, updated_at = now()
                 WHERE id = {serieId} AND activa
                 RETURNING siguiente - 1 AS "Value"
                 """)
            .ToListAsync(ct);

        var numero = numeros.Count > 0 ? numeros[0] : 0;

        if (numero == 0)
        {
            throw new KeyNotFoundException($"La serie '{serieId:D}' no existe o está desactivada.");
        }

        contexto.NumeroSerie.Add(new NumeroSerie
        {
            Id = Guid.NewGuid(),
            SerieId = serieId,
            Numero = numero,
            Estado = EstadoNumeroSerie.Emitido,
            EmitidoAt = reloj.GetUtcNow(),
            EmitidoPor = usuarioId,
        });

        await contexto.SaveChangesAsync(ct);

        if (transaccionPropia)
        {
            await transaccion!.CommitAsync(ct);
        }

        return numero;
    }

    /// <summary>
    /// Marca un número emitido como anulado (docs/ESPECIFICACION.md §3.7, D28). El contador no
    /// retrocede: el número sigue ocupado y el siguiente sigue siendo el de después.
    /// </summary>
    /// <param name="usuarioAdmin">Usuario que anula; queda en auditoría y en la fila.</param>
    /// <param name="ip">IP de origen, obligatoria en toda mutación (AGENTS.md §6).</param>
    public async Task AnularNumeroAsync(
        Guid serieId,
        int numero,
        string motivo,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("La anulación de un número lleva motivo.", nameof(motivo));
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var fila = await contexto.NumeroSerie
            .SingleOrDefaultAsync(n => n.SerieId == serieId && n.Numero == numero, ct)
            ?? throw new KeyNotFoundException($"El número {numero} de la serie '{serieId:D}' no existe.");

        if (fila.Estado == EstadoNumeroSerie.Anulado)
        {
            throw new InvalidOperationException($"El número {numero} ya estaba anulado.");
        }

        fila.Estado = EstadoNumeroSerie.Anulado;
        fila.AnuladoAt = reloj.GetUtcNow();
        fila.AnuladoPor = usuarioAdmin;
        fila.MotivoAnulacion = motivo.Trim();
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "numero_serie.anulado",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "numero_serie",
            EntidadId = fila.Id.ToString("D"),
            DatosJson = $$"""{"serie":"{{serieId:D}}","numero":{{numero}},"motivo":"{{motivo.Trim()}}"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }
}