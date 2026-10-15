using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Registra los fallos que alguien tiene que mirar. Es el destino del "fallo → alerta" que pide el
/// criterio de aceptación de F1-12.
/// <para>
/// Es una interfaz y no la base de datos directa para que el backup se pueda probar sin montarla.
/// </para>
/// </summary>
public interface IRegistroAlertas
{
    /// <summary>
    /// Deja constancia de un fallo. No lanza si no puede: perder la alerta no puede ser la causa de
    /// que el proceso caiga.
    /// </summary>
    Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct);

    /// <summary>
    /// Cierra la alerta viva de esa clave. F2-09: "alerta persistente hasta liquidar" significa que la
    /// alerta se queda abierta mientras el problema siga abierto y se cierra cuando deja de estarlo,
    /// no que se borra el rastro.
    /// <para>
    /// No lanza si no puede, por el mismo motivo que <see cref="RegistrarAsync"/>: una alerta sin
    /// cerrar es ruido, pero tumbar una liquidación —que sí es un hecho de negocio ya decidido— por
    /// no poder escribir una fila de aviso sería convertir un fallo pequeño en un incidente.
    /// </para>
    /// </summary>
    Task ResolverAsync(string clave, CancellationToken ct);
}

/// <summary>
/// Guarda la alerta en la tabla <c>alerta</c>.
/// <para>
/// Insertar y después actualizar sería un problema de concurrencia —dos jobs que fallan en el mismo
/// instante se pisan—, así que se hace un <c>INSERT … ON CONFLICT DO UPDATE</c> sobre la clave
/// única. Es la misma razón por la que el MFA no gasta el token asignando la propiedad en memoria:
/// leer y luego escribir deja hueco entre los dos.
/// </para>
/// <para>
/// <b>Alerta persistente, sin duplicar (F2-09).</b> La alerta de un contrato finalizado sin liquidar
/// tiene que seguir viva mientras el problema siga vivo (§3.5: "alerta persistente hasta liquidar"), y
/// eso NO es crear una alerta nueva cada día: con el índice único por clave sería imposible, y aunque
/// existiera, cien filas de lo mismo no son una alerta sino ruido que nadie mira. Lo que hay es una
/// fila viva que el <c>ON CONFLICT DO UPDATE</c> refresca: le sube el contador de repeticiones y le
/// reescribe el mensaje con el estado actual, así que el texto dice "lleva N días" y no "vio esto
/// aquel día". El mismo <c>UPDATE</c> lleva <c>WHERE alerta.resuelta_at IS NULL</c>, que es lo que
/// impide resucitar una alerta ya cerrada: si un problema vuelve a darse con la misma clave, no se
/// revive la vieja, se crea una nueva. Y cerrar es poner <c>resuelta_at</c>, nunca borrar: la fila
/// dice que pasó, cuándo y cuántas veces se vio.
/// </para>
/// </summary>
public sealed class ServicioAlertas(AtlasRentDbContext contexto, TimeProvider reloj) : IRegistroAlertas
{
    public async Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct)
    {
        var ocurrido = reloj.GetUtcNow();

        try
        {
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO alerta (id, clave, tipo, mensaje, ocurrido_at, repeticiones)
                 VALUES ({Guid.NewGuid()}, {clave}, {tipo}, {mensaje}, {ocurrido}, 1)
                 ON CONFLICT (clave) DO UPDATE
                 SET repeticiones = alerta.repeticiones + 1,
                     ocurrido_at = {ocurrido},
                     mensaje = {mensaje}
                 WHERE alerta.resuelta_at IS NULL
                 """,
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Perder la alerta es malo, pero tumbar el job por no poder guardarla es peor: el
            // respaldo falló igual, y eso ya está en el log y en el propio fallo del job.
            // El filtro es todo menos la cancelación, a propósito: se llama desde el manejador del
            // fallo del respaldo, y una excepción propia de la alerta —un timeout del driver, un
            // error de socket— no puede tapar el fallo que había que avisar.
            Log.Error("No se pudo registrar la alerta {Tipo}/{Clave}: {TipoExcepcion}: {Motivo}",
                tipo, clave, e.GetType().Name, e.Message);
        }
    }

    public async Task ResolverAsync(string clave, CancellationToken ct)
    {
        var resuelta = reloj.GetUtcNow();

        try
        {
            // El WHERE con resuelta_at IS NULL es lo que hace la resolución idempotente y además
            // conserva la fecha en que se cerró: reintentar no pisa el momento en que alguien liquidó.
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE alerta
                 SET resuelta_at = {resuelta}
                 WHERE clave = {clave}
                   AND resuelta_at IS NULL
                 """,
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error("No se pudo resolver la alerta {Clave}: {TipoExcepcion}: {Motivo}",
                clave, e.GetType().Name, e.Message);
        }
    }
}

/// <summary>
/// Registro de alertas que no hace nada. Existe para los tests que no están probando el registro de
/// alertas, que si no tendrían que montar la base de datos para nada.
/// </summary>
public sealed class RegistroAlertasNulo : IRegistroAlertas
{
    public Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ResolverAsync(string clave, CancellationToken ct) => Task.CompletedTask;
}