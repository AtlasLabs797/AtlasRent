using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Aviso de la reserva mensual que lleva 7 días sin convertirse en contrato (F2-03,
/// docs/ESPECIFICACION.md §3.4: "alerta si una reserva mensual lleva 7 días sin convertirse (una vez,
/// persistente)").
/// <para>
/// Recorre las reservas mensuales <see cref="EstadoReserva.Activa"/> cuya fecha de reserva es de hace
/// 7 días o más y deja una alerta con <see cref="ServicioAlertas"/>. Es una ventana y no una igualdad
/// (BUG-028): si el trabajo no corre un día, al siguiente sigue avisando. La clave lleva el
/// identificador de la reserva, así que correrlo varias veces no deja más de una alerta viva.
/// </para>
/// <para>
/// El aviso es persistente: se mantiene abierto mientras la reserva siga <c>Activa</c> y se cierra
/// solo cuando se convierte, se cancela o cambia de estado (p. ej. en conflicto, que tiene su propio
/// aviso).
/// </para>
/// <para>
/// El día se calcula en la zona de negocio (D16), no en UTC, como en el trabajo de copia.
/// </para>
/// </summary>
public sealed class TrabajoAlertaReserva(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>Cuántos días sin convertirse tiene que llevar la reserva para avisar.</summary>
    private const int DiasSinConvertir = 7;

    /// <summary>Tipo de alerta, el mismo que usan las de copia y las de integridad.</summary>
    private const string TipoAlerta = "reserva";

    private const string Prefijo = "reserva-sin-convertir:";

    /// <summary>
    /// Clave de deduplicación de la alerta. Lleva la reserva y no la fecha: el aviso es una vez y
    /// para siempre por reserva, como el resto de alertas del sistema.
    /// </summary>
    public static string Clave(Guid reservaId) => $"{Prefijo}{reservaId:D}";

    public async Task EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());
        var limite = hoy.AddDays(-DiasSinConvertir);

        var reservas = await contexto.Reserva
            .AsNoTracking()
            .Where(r => r.Activo
                && r.Tipo == TipoReserva.Mensual
                && r.Estado == EstadoReserva.Activa
                && r.FechaReserva <= limite)
            .Select(r => new { r.Id, r.UnidadId, r.FechaReserva })
            .ToListAsync(ct);

        foreach (var reserva in reservas)
        {
            await alertas.RegistrarAsync(
                TipoAlerta,
                Clave(reserva.Id),
                $"La reserva mensual {reserva.Id:D} de la unidad {reserva.UnidadId:D}, hecha el "
                    + $"{reserva.FechaReserva:dd/MM/yyyy}, lleva más de {DiasSinConvertir} días sin "
                    + "convertirse en contrato.",
                ct);
        }

        // Cerrar las que ya no aplican (convertida, cancelada, en conflicto...). La comparación va en
        // memoria: las vivas de este prefijo son pocas.
        var vigentes = reservas.Select(r => Clave(r.Id)).ToHashSet();
        var vivas = await contexto.Alerta.AsNoTracking()
            .Where(a => a.ResueltaAt == null && a.Clave.StartsWith(Prefijo))
            .Select(a => a.Clave)
            .ToListAsync(ct);

        var cerradas = 0;
        foreach (var clave in vivas.Where(c => !vigentes.Contains(c)))
        {
            await alertas.ResolverAsync(clave, ct);
            cerradas++;
        }

        Log.Information(
            "Alerta de reservas: {Cantidad} reserva(s) mensual(es) llevan {Dias} días o más sin convertirse, "
                + "{Cerradas} alerta(s) cerradas",
            reservas.Count,
            DiasSinConvertir,
            cerradas);
    }
}
