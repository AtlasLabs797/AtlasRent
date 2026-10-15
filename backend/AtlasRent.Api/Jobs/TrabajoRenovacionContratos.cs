using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Renovación automática por bloques e incrementos de renta de los contratos vencidos (F2-06,
/// docs/ESPECIFICACION.md §3.5).
/// <para>
/// Va a las 06:00 de Santo Domingo, detrás de la copia (02:00), la integridad (03:00), la
/// vigilancia (04:00) y el aviso de reservas (05:00). El motivo es operativo: es el último trabajo
/// del día y mueve datos que factura el día siguiente, así que un fallo suyo se ve en la mañana
/// siguiente y no en mitad de la operación. La copia anterior no es una dependencia funcional —si
/// falta, la renovación sigue haciendo falta igual—, solo que es más fácil de mirar si el trabajo
/// no escribe al día siguiente de un volcado que nadie sabe si salió bien.
/// </para>
/// <para>
/// <b>El trabajo es idempotente por partida doble, y las dos partes hacen falta.</b> La primera es
/// la condición: solo se renueva lo que tiene
/// <see cref="Data.Contrato.FechaFinVigente"/> anterior al día de hoy en Santo Domingo, así que una
/// segunda ejecución el mismo día ya no encuentra nada que hacer. La segunda es la carrera: si dos
/// copias se pisan de todos modos, el índice único de <c>(contrato_id, numero)</c> en
/// <c>contrato_bloque</c> hace que una falle antes de dejar dos bloques iguales.
/// </para>
/// <para>
/// Con solo la primera, dos ejecuciones simultáneas —un reinicio del servidor con el trabajo a
/// medias, un relanzamiento a mano— podrían dejar el bloque duplicado y la renta del contrato
/// contada dos veces. Con solo la segunda, una segunda pasada normal crearía un bloque nuevo de una
/// renta que no tocaba, que es el fallo más caro de los dos: no se ve en los datos, se ve en la
/// factura del mes que viene.
/// </para>
/// <para>
/// El día se calcula en la zona de negocio (D16), no en UTC, como en el trabajo de aviso de
/// reservas: el planificador corre en UTC y buscar "hoy" en UTC escribiría el bloque del día
/// siguiente en los contratos de la frontera.
/// </para>
/// </summary>
public sealed class TrabajoRenovacionContratos(ServicioRenovaciones renovaciones)
{
    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resumen = await renovaciones.RenovarVencidosAsync(ct);

        Log.Information(
            "Trabajo de renovación terminado: {Renovados} contrato(s) renovado(s)",
            resumen.ContratosRenovados);
    }
}