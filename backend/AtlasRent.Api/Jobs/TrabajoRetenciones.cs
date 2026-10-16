using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// F3-04: el aviso de las facturas que retienen y que todavía no han recibido su comprobante (§3.9,
/// D14).
/// <para>
/// <b>Persistente y sin duplicar</b>, como el resto de los avisos de §3.16: la clave es estable por
/// factura, así que pasar dos veces el mismo día refresca la misma fila y suma repeticiones en vez de
/// llenar la bandeja de copias.
/// </para>
/// <para>
/// <b>Se resuelve sola</b> cuando el comprobante llega, y en la recepción, no aquí: el trabajo solo
/// levanta alertas, y quien sabe que el papel llegó es quien lo anotó. Como la alerta cerrada vuelve
/// a abrirse en la misma fila si el problema regresa (BUG-008), una retención que vuelve a quedarse
/// sin comprobante vuelve a avisar.
/// </para>
/// <para>
/// La consulta es solo por estado, que es exactamente lo que cubre el índice: si un día llegaran a
/// existir muchas facturas pendientes, el trabajo crece con ellas y no con la base entera.
/// </para>
/// </summary>
public sealed class TrabajoRetenciones(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        var pendientes = await contexto.ComprobanteRetencion
            .AsNoTracking()
            .Where(c => c.Estado == EstadoComprobanteRetencion.Pendiente)
            .Include(c => c.Factura)
            .ToListAsync(ct);

        foreach (var comprobante in pendientes)
        {
            var factura = comprobante.Factura;
            var dias = hoy.DayNumber - TiempoNegocio.FechaEnZonaNegocio(comprobante.CreatedAt).DayNumber;

            await alertas.RegistrarAsync(
                ServicioRetenciones.TipoAlerta,
                ServicioRetenciones.ClaveAlerta(comprobante.FacturaId),
                $"La factura {NumeroDeFactura(factura)} retuvo {comprobante.Importe:0.00} por {comprobante.Tipo} "
                    + $"y su comprobante de retención sigue pendiente {DiasPendientes(dias)}. Pídeselo al cliente.",
                ct);
        }

        Log.Information("Avisos de comprobante de retención: {Pendientes} factura(s) sin comprobante",
            pendientes.Count);
    }

    /// <summary>
    /// Cómo se nombra la factura en el aviso: el número si ya lo tiene, y si no la fecha en que se
    /// emitió. Una factura pendiente de comprobante ya está emitida, así que el número existe
    /// siempre; el segundo camino es solo para que el aviso no quede con un hueco si algún día se
    /// llama a esto con algo sin emitir.
    /// </summary>
    private static string NumeroDeFactura(Factura? factura) =>
        factura is null
            ? "desconocida"
            : factura.Numero is { } numero
                ? $"nº {numero}"
                : $"del {factura.FechaEmision:yyyy-MM-dd}";

    /// <summary>
    /// Cuánto lleva sin llegar el papel, que es lo que dice si esto es una chase de hoy o una factura
    /// que lleva semanas sin que nadie la gestionó. En días de negocio, no de reloj.
    /// </summary>
    private static string DiasPendientes(int dias) =>
        dias <= 0 ? "desde hoy" : $"desde hace {dias} día(s)";
}