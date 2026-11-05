using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// F4-05: los avisos de pago de §3.14 (D12), a las 09:00 de la zona de negocio.
/// <para>
/// Esta clase no decide nada. La cadencia (5 días el primero y cada 15), el modo
/// (apagado, manual o automático) y la pausa por cliente están en
/// <see cref="ServicioRecordatorios"/> y en los ajustes de la casa, que es donde §3.14 los deja: el
/// trabajo es el que dispara, no el que sabe.
/// </para>
/// <para>
/// <b>Un trabajo diario que falla se reintenta solo</b>, así que una excepción suelta aquí dejaría
/// clientes a medias entre dos avisos. El servicio no lanza nunca: cada cliente y unidad va en su
/// transacción y un fallo de uno se cuenta y se registra, sin tirar los demás.
/// </para>
/// </summary>
public sealed class TrabajoRecordatorios(ServicioRecordatorios recordatorios)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resumen = await recordatorios.EjecutarAsync(ct);

        Log.Information(
            "Recordatorios del {Fecha}: modo {Modo}, {Candidatos} candidato(s), {Preparados} preparado(s), "
                + "{Enviados} enviado(s), {Omitidos} omitido(s) y {Fallidos} con fallo",
            resumen.Fecha,
            resumen.Modo,
            resumen.Candidatos,
            resumen.Preparados,
            resumen.Enviados,
            resumen.Omitidos,
            resumen.Fallidos);
    }
}