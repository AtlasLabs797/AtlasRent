using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Aviso de que una reserva entra dentro de 7 días (F2-03, docs/ESPECIFICACION.md §3.4).
/// <para>
/// Recorre las reservas <see cref="EstadoReserva.Activa"/> cuya entrada prevista cae <b>exactamente</b>
/// dentro de 7 días y deja una alerta con <see cref="ServicioAlertas"/>. Lo de "exactamente" es lo
/// que hace el trabajo idempotente por la vía de la base: la clave de deduplicación lleva el
/// identificador de la reserva, así que correrlo dos veces el mismo día —que es lo que pasa si
/// alguien lo relanza a mano o si el servidor se reinicia a media ejecución— no deja dos alertas.
/// </para>
/// <para>
/// El día se calcula en la zona de negocio (D16), no en UTC, como en el trabajo de copia: a las
/// 05:00 en Santo Domingo son las 09:00 UTC, y buscar "hoy" en UTC escribiría el día siguiente en
/// las reservas de la frontera.
/// </para>
/// <para>
/// DECISIÓN DEL ORQUESTADOR: avisa de toda reserva activa que entra dentro de 7 días, mensual o
/// corta. La especificación (§3.4) habla de la reserva mensual que lleva 7 días sin convertirse en
/// contrato, que es otro momento; este trabajo es el que se ha pedido aquí y el que avisa de que
/// toca. Si más adelante se quiere el aviso de los 7 días sin convertir, es otro trabajo y otra
/// clave de deduplicación.
/// </para>
/// </summary>
public sealed class TrabajoAlertaReserva(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>Cuántos días antes de la entrada prevista se avisa.</summary>
    private const int DiasDeAviso = 7;

    /// <summary>Tipo de alerta, el mismo que usan las de copia y las de integridad.</summary>
    private const string TipoAlerta = "reserva";

    /// <summary>
    /// Clave de deduplicación de la alerta. Lleva la reserva y no la fecha: el aviso es una vez y
    /// para siempre por reserva, como el resto de alertas del sistema.
    /// </summary>
    public static string Clave(Guid reservaId) => $"reserva-entrada:{reservaId:D}";

    public async Task EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());
        var objetivo = hoy.AddDays(DiasDeAviso);

        var reservas = await contexto.Reserva
            .AsNoTracking()
            .Where(r => r.Activo
                && r.Estado == EstadoReserva.Activa
                && r.EntradaPrevista == objetivo)
            .Select(r => new { r.Id, r.UnidadId, r.EntradaPrevista })
            .ToListAsync(ct);

        foreach (var reserva in reservas)
        {
            await alertas.RegistrarAsync(
                TipoAlerta,
                Clave(reserva.Id),
                $"La reserva {reserva.Id:D} de la unidad {reserva.UnidadId:D} entra el "
                    + $"{reserva.EntradaPrevista:dd/MM/yyyy}, dentro de {DiasDeAviso} días.",
                ct);
        }

        Log.Information(
            "Alerta de reservas: {Cantidad} reserva(s) entran el {Objetivo:dd/MM/yyyy}",
            reservas.Count,
            objetivo);
    }
}