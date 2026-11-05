using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Numerador del comprobante fiscal y comprobantes emitidos (F3-03, docs/ESPECIFICACION.md §3.7).
/// <para>
/// <b>El número de comprobante se saca con el mismo patrón que el de la factura</b>
/// (<c>UPDATE numerador_fiscal SET siguiente_numero = siguiente_numero + 1 … RETURNING</c>), dentro
/// de la transacción que emite: si esa transacción falla, el número no se consume, y dos emisiones
/// simultáneas no pueden salir con el mismo. Nunca <c>MAX() + 1</c>, nunca por entidad (docs/DOC_TECNICA.md §4.3).
/// </para>
/// <para>
/// <b>Son dos contadores, no uno.</b> El de <c>serie.siguiente</c> numera facturas y este numera
/// comprobantes. Compartirlos dejaría el rango autorizado con huecos por facturas que no son
/// comprobantes, o una factura consumiendo un número de comprobante ajeno.
/// </para>
/// <para>
/// <b>Una serie fiscal sin numerador no emite.</b> No es una degradación: una factura de serie fiscal
/// sin comprobante es un documento que no se puede declarar (§3.7).
/// </para>
/// </summary>
public sealed class ServicioComprobantes(
    AtlasRentDbContext contexto,
    TimeProvider reloj)
{
    /// <summary>Tipo de alerta de estos avisos, el mismo que la tabla ya preveía.</summary>
    public const string TipoAlerta = "comprobante";

    /// <summary>
    /// Comprueba que la serie pueda emitir comprobante ahora mismo. Va <b>antes</b> de
    /// <see cref="ServicioSeries.ReservarNumeroAsync"/> y por eso es un método aparte del que saca
    /// el número: un numerador agotado o vencido tiene que rechazar la emisión sin haber gastado el
    /// número de factura, que es el criterio de aceptación de <c>docs/PLAN.md</c>.
    /// </summary>
    /// <param name="serieNumero">Número visible de la serie, para que el 409 lo diga.</param>
    /// <exception cref="InvalidOperationException">No hay numerador, está vencido o se agotó.</exception>
    public async Task ExigirUtilizableAsync(Guid serieId, int serieNumero, DateOnly hoy, CancellationToken ct)
    {
        var numerador = await contexto.NumeradorFiscal
            .AsNoTracking()
            .SingleOrDefaultAsync(n => n.SerieId == serieId, ct);

        if (numerador is null)
        {
            throw new InvalidOperationException(
                $"La serie {serieNumero} genera comprobante fiscal y no tiene numerador configurado. "
                    + "Créalo en Configuración → Numeradores fiscales antes de emitir: una factura de "
                    + "serie fiscal sin comprobante no se puede declarar.");
        }

        // El día del vencimiento todavía vale: lo que corta es el día siguiente.
        if (hoy > numerador.FechaVencimiento)
        {
            throw new InvalidOperationException(
                $"El numerador fiscal de la serie {serieNumero} venció el "
                    + $"{numerador.FechaVencimiento:yyyy-MM-dd} y no admite más comprobantes. Configura un "
                    + "rango nuevo en Configuración → Numeradores fiscales antes de emitir.");
        }

        var restantes = numerador.RangoHasta - numerador.SiguienteNumero + 1;
        if (restantes <= 0)
        {
            throw new InvalidOperationException(
                $"El numerador fiscal de la serie {serieNumero} se agotó en el "
                    + $"{numerador.RangoHasta}. Configura un rango nuevo en Configuración → Numeradores "
                    + "fiscales antes de emitir.");
        }
    }

    /// <summary>
    /// Saca el siguiente número de comprobante de la serie y deja la fila en
    /// <c>comprobante_fiscal</c>.
    /// <para>
    /// No audita por su cuenta: la auditoría es la de la emisión completa (factura, número de serie y
    /// comprobante en la misma transacción, docs/DOC_TECNICA.md §5). Auditar aquí, además,
    /// serializaría a todas las emisiones con el bloqueo de la cadena de auditoría y ocultaría justo
    /// la concurrencia que esto tiene que aguantar.
    /// </para>
    /// <para>
    /// El <c>WHERE … AND siguiente_numero &lt;= rango_hasta</c> es lo que hace el bloqueo de
    /// agotado íntegro bajo carrera: entre el aviso de <see cref="ExigirUtilizableAsync"/> y este
    /// <c>UPDATE</c> puede haber colado otra emisión que se lleve el último número, y entonces el
    /// <c>UPDATE</c> no toca fila y el 409 sale sin haber gastado nada.
    /// </para>
    /// </summary>
    /// <param name="facturaId">Factura a la que se asigna el comprobante; es 1:1.</param>
    public async Task<int> ReservarAsync(
        Guid serieId,
        Guid facturaId,
        Guid usuario,
        DateOnly hoy,
        CancellationToken ct)
    {
        // Si quien llama ya tiene una transacción abierta (la de la emisión), se usa esa: el
        // comprobante tiene que salir o no salir junto con la factura y su número de serie.
        var transaccionPropia = contexto.Database.CurrentTransaction is null;

        await using var transaccion = transaccionPropia
            ? await contexto.Database.BeginTransactionAsync(ct)
            : null;

        // Un UPDATE ... RETURNING no admite composición de LINQ sobre él, por eso ToListAsync.
        var numeros = await contexto.Database
            .SqlQuery<int>(
                $"""
                 UPDATE numerador_fiscal
                 SET siguiente_numero = siguiente_numero + 1, updated_at = now()
                 WHERE serie_id = {serieId}
                   AND siguiente_numero <= rango_hasta
                 RETURNING siguiente_numero - 1 AS "Value"
                 """)
            .ToListAsync(ct);

        if (numeros.Count == 0)
        {
            throw new InvalidOperationException(
                $"El numerador fiscal de la serie '{serieId:D}' se agotó mientras se emitía. "
                    + "Configura un rango nuevo en Configuración → Numeradores fiscales.");
        }

        var numero = numeros[0];

        // El tipo se lee después del UPDATE, no antes: el UPDATE tiene el bloqueo de la fila, así que
        // nadie puede cambiarlo entre los dos. Leído antes, un cambio concurrente del administrador
        // dejaría el comprobante con el tipo viejo.
        var tipo = await contexto.NumeradorFiscal
            .AsNoTracking()
            .Where(n => n.SerieId == serieId)
            .Select(n => n.TipoPrefijo)
            .SingleAsync(ct);

        var ahora = reloj.GetUtcNow();

        contexto.ComprobanteFiscal.Add(new ComprobanteFiscal
        {
            Id = Guid.NewGuid(),
            FacturaId = facturaId,
            SerieId = serieId,
            Numero = numero,
            Tipo = tipo,
            Estado = EstadoComprobanteFiscal.Emitido,
            RegistradoAt = ahora,
            RegistradoPor = usuario,
            CreatedAt = ahora,
            CreatedBy = usuario,
        });

        await contexto.SaveChangesAsync(ct);

        if (transaccionPropia)
        {
            await transaccion!.CommitAsync(ct);
        }

        return numero;
    }

    /// <summary>
    /// Clave de la alerta de "quedan pocos números" de una serie.
    /// <para>
    /// Lleva la serie y no la fecha: el aviso no es «hoy quedan pocos», es «quedan pocos», y la
    /// fila se refresca con el estado de cada día mientras siga en esa situación (§3.16).
    /// </para>
    /// </summary>
    public static string ClavePocos(Guid serieId) => $"numerador:{serieId:D}:pocos";

    /// <summary>Clave de la alerta de numerador vencido de una serie.</summary>
    public static string ClaveVencido(Guid serieId) => $"numerador:{serieId:D}:vencido";

    /// <summary>Clave de la alerta de numerador agotado de una serie.</summary>
    public static string ClaveAgotado(Guid serieId) => $"numerador:{serieId:D}:agotado";
}