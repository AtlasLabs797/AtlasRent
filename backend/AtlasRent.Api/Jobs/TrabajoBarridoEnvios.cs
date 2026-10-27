using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// BUG-037: barrido recurrente que reencola los envíos masivos que quedaron <c>Pendiente</c> sin
/// trabajo (el proceso cayó entre el commit y el encolado). Es idempotente; ver
/// <see cref="ServicioEnviosMasivos.BarrerPendientesAsync"/>.
/// </summary>
public sealed class TrabajoBarridoEnvios(ServicioEnviosMasivos masivos)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var reencoladas = await masivos.BarrerPendientesAsync(ct);

        if (reencoladas > 0)
        {
            Log.Warning("Barrido de envíos: {Reencoladas} envío(s) pendiente(s) sin trabajo reencolado(s).", reencoladas);
        }
    }
}
