using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// F3-09: el aviso de las fianzas cuyo contrato ya terminó y que siguen sin devolver ni liquidar
/// (docs/ESPECIFICACION.md §3.11 y §3.16).
/// <para>
/// <b>«Sin devolver ni liquidar» es literal.</b> Avisa mientras la fianza no esté devuelta, retenida a
/// medias o en liquidación, y en los tres casos hay una decisión escrita detrás. Una fianza a la que
/// se le retiene parte por faltantes de inventario (§3.6) está resuelta: nadie la va a devolver, y
/// avisar de ella todos los días sería la forma de alerta que enseña a ignorar la bandeja.
/// </para>
/// <para>
/// <b>Persistente y sin duplicar</b>, como el resto de los avisos de §3.16: la clave lleva la fianza, no
/// el contrato y no la fecha, así que pasar dos veces el mismo día refresca la misma fila y suma
/// repeticiones en vez de llenar la bandeja de copias. Y si el asunto vuelve, la alerta cerrada se
/// reabre en la misma fila (BUG-008), que es lo que garantiza el índice parcial de <c>alerta</c>.
/// </para>
/// <para>
/// <b>Se resuelve sola en el movimiento, no aquí</b>, igual que el comprobante de retención de F3-04:
/// la devolución y la puesta en liquidación cierran el aviso en su propio commit. Este trabajo solo
/// levanta lo que ve.
/// </para>
/// <para>
/// <b>La consulta es acotada</b> por el estado de la fianza, que es la condición que puede filtrar en la
/// base sin tener que componer el estado derivado en SQL: el trabajo crece con las fianzas abiertas y
/// no con la base entera.
/// </para>
/// </summary>
public sealed class TrabajoFianzas(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var pendientes = await contexto.Fianza
            .AsNoTracking()
            .Where(f => f.Estado == EstadoFianza.Pendiente || f.Estado == EstadoFianza.Parcial)
            .Include(f => f.Contrato)
            .ToListAsync(ct);

        var avisadas = 0;

        foreach (var fianza in pendientes)
        {
            var contrato = fianza.Contrato;

            // No hace falta comprobar aquí que la fianza "está pendiente": la consulta solo trae las que
            // no tienen decisión guardada, y sin decisión el estado derivado es Pendiente o Parcial, que
            // los dos son pendientes por definición. La regla vive entera en CalculoFianza, que es donde
            // está escrita una sola vez.
            if (contrato is null || !Terminado(contrato))
            {
                continue;
            }

            var cifras = await CifrasAsync(fianza.Id, ct);

            await alertas.RegistrarAsync(
                ServicioFianzas.TipoAlerta,
                ServicioFianzas.ClaveAlerta(fianza.Id),
                $"El contrato '{contrato.Id:D}' terminó y su fianza de {fianza.ImporteExigido:0.00} "
                    + $"{fianza.Moneda} ({fianza.NumeroFianzas} fianza(s)) sigue sin devolver: quedan "
                    + $"{cifras.Saldo:0.00} por devolver.",
                ct);

            avisadas++;
        }

        Log.Information("Avisos de fianza sin devolver: {Pendientes} fianza(s)", avisadas);
    }

    /// <summary>
    /// Si el contrato ya terminó. Se mira el estado y no solo la fecha: un contrato que se terminó y se
    /// reactivó no está terminado, y avisar de su fianza sería avisar de algo que no está pasando.
    /// </summary>
    private static bool Terminado(Contrato contrato) =>
        contrato.Estado is EstadoContrato.Finalizado or EstadoContrato.Liquidado;

    /// <summary>
    /// Lo que queda por devolver de una fianza: lo cobrado menos lo devuelto menos lo retenido. Va
    /// aquí y no se reutiliza de <c>ServicioFianzas</c> a propósito: el trabajo no mueve nada y solo
    /// necesita leer, así que tener su propia cuenta de tres sumas es más barato que darle permiso sobre
    /// el dinero a un trabajo de avisos.
    /// </summary>
    private async Task<Cifras> CifrasAsync(Guid fianzaId, CancellationToken ct)
    {
        var cobrado = await contexto.CobroAsignacion
            .AsNoTracking()
            .Where(a => a.FianzaId == fianzaId && a.Vigente && a.Cobro!.Estado == EstadoCobro.Registrado)
            .SumAsync(a => (decimal?)a.Importe, ct) ?? 0m;

        var salidas = await contexto.FianzaDevolucion
            .AsNoTracking()
            .Where(d => d.FianzaId == fianzaId)
            .GroupBy(d => d.FianzaId)
            .Select(g => new { Devuelto = g.Sum(d => d.Importe), Descontado = g.Sum(d => d.ImporteDescontado) })
            .FirstOrDefaultAsync(ct);

        return new Cifras(
            Redondeo.Importe(cobrado),
            Redondeo.Importe(salidas?.Devuelto ?? 0m),
            Redondeo.Importe(salidas?.Descontado ?? 0m));
    }

    /// <summary>Lo cobrado, lo devuelto y lo retenido de una fianza, con el saldo ya restado.</summary>
    private sealed record Cifras(decimal Cobrado, decimal Devuelto, decimal Descontado)
    {
        public decimal Saldo => CalculoFianza.Saldo(Cobrado, Devuelto, Descontado);
    }
}