using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// F3-03: los avisos del numerador de comprobante fiscal (§3.7): por números restantes, por
/// vencimiento y por agotado.
/// <para>
/// <b>Persistente y sin duplicar</b>, como las de F2-09: la clave es estable por serie y condición,
/// así que pasar dos veces el mismo día refresca la misma fila y suma repeticiones en vez de llenar la
/// bandeja de copias. Y se resuelve sola cuando el problema deja de estarlo —un numerador al que se le
/// amplía el rango ya no está por debajo del umbral—, porque una alerta cerrada que vuelve a abrirse
/// cada día es ruido.
/// </para>
/// <para>
/// <b>"Agotado" suprime "pocos".</b> Cuando no queda ningún número, el aviso útil es el que dice que
/// hay que pedir un rango nuevo, no un "quedan pocos" al lado. Los dos avisos juntos serían dos
/// alertas para el mismo hecho y nadie sabría cuál exige una acción.
/// </para>
/// <para>
/// La lógica va aquí y no en el servicio de emisión, que solo tiene que lanzar el 409: esto corre una
/// vez al día sin mirar ninguna factura.
/// </para>
/// <para>
/// <b>Límite heredado de <see cref="IRegistroAlertas"/>.</b> <c>alerta.clave</c> es única, así que
/// cuando el <c>ON CONFLICT … DO UPDATE … WHERE resuelta_at IS NULL</c> choca con una alerta ya
/// cerrada no inserta ni revive nada: un numerador que vuelve a quedarse corto después de haber sido
/// ampliado <b>no vuelve a levantar aviso</b>. Es así en todas las alertas del sistema (las de F2-09
/// entre ellas); cambiarlo es un índice único parcial sobre las alertas vivas, y queda fuera de esta
/// tarea.
/// </para>
/// </summary>
public sealed class TrabajoComprobantes(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        var numeradores = await contexto.NumeradorFiscal
            .AsNoTracking()
            .Include(n => n.Serie)
            .ToListAsync(ct);

        var pocos = 0;
        var vencidos = 0;
        var agotados = 0;
        var cerradas = 0;

        foreach (var numerador in numeradores)
        {
            var nombre = numerador.Serie?.Numero ?? 0;
            var restantes = numerador.RangoHasta - numerador.SiguienteNumero + 1;
            var agotado = restantes <= 0;
            var porPocos = !agotado && restantes <= numerador.UmbralAlerta;
            var vencido = hoy > numerador.FechaVencimiento;

            if (porPocos)
            {
                await alertas.RegistrarAsync(
                    ServicioComprobantes.TipoAlerta,
                    ServicioComprobantes.ClavePocos(numerador.SerieId),
                    $"A la serie fiscal {nombre} le quedan {restantes} número(s) de comprobante "
                        + $"y el umbral de aviso es {numerador.UmbralAlerta}. Pide un rango nuevo antes "
                        + "de que se agote.",
                    ct);
                pocos++;
            }
            else
            {
                cerradas += await ResolverAsync(ServicioComprobantes.ClavePocos(numerador.SerieId), ct);
            }

            if (agotado)
            {
                await alertas.RegistrarAsync(
                    ServicioComprobantes.TipoAlerta,
                    ServicioComprobantes.ClaveAgotado(numerador.SerieId),
                    $"El numerador fiscal de la serie {nombre} se agotó en el {numerador.RangoHasta}. "
                        + "La emisión está bloqueada hasta que se configure un rango nuevo.",
                    ct);
                agotados++;
            }
            else
            {
                cerradas += await ResolverAsync(ServicioComprobantes.ClaveAgotado(numerador.SerieId), ct);
            }

            if (vencido)
            {
                await alertas.RegistrarAsync(
                    ServicioComprobantes.TipoAlerta,
                    ServicioComprobantes.ClaveVencido(numerador.SerieId),
                    $"El numerador fiscal de la serie {nombre} venció el "
                        + $"{numerador.FechaVencimiento:yyyy-MM-dd}. La emisión está bloqueada hasta que "
                        + "se configure un rango nuevo.",
                    ct);
                vencidos++;
            }
            else
            {
                cerradas += await ResolverAsync(ServicioComprobantes.ClaveVencido(numerador.SerieId), ct);
            }
        }

        Log.Information(
            "Avisos de numerador fiscal: {Pocos} por debajo del umbral, {Agotados} agotados, "
                + "{Vencidos} vencidos, {Cerradas} alerta(s) resueltas",
            pocos,
            agotados,
            vencidos,
            cerradas);
    }

    /// <summary>
    /// Cierra la alerta de una clave que ya no aplica y dice si había algo que cerrar, para poder
    /// contarlo en el resumen sin volver a consultar la tabla.
    /// </summary>
    private async Task<int> ResolverAsync(string clave, CancellationToken ct)
    {
        var abierta = await contexto.Alerta.AsNoTracking().AnyAsync(a => a.Clave == clave, ct);
        if (!abierta)
        {
            return 0;
        }

        await alertas.ResolverAsync(clave, ct);

        return 1;
    }
}