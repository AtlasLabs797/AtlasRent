using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
}

/// <summary>
/// Guarda la alerta en la tabla <c>alerta</c>.
/// <para>
/// Insertar y después actualizar sería un problema de concurrencia —dos jobs que fallan en el mismo
/// instante se pisan—, así que se hace un <c>INSERT … ON CONFLICT DO UPDATE</c> sobre la clave
/// única. Es la misma razón por la que el MFA no gasta el token asignando la propiedad en memoria:
/// leer y luego escribir deja hueco entre los dos.
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
                 """,
                ct);
        }
        catch (Exception e) when (e is DbUpdateException or NpgsqlException or InvalidOperationException
            or OperationCanceledException)
        {
            // Perder la alerta es malo, pero tumbar el job por no poder guardarla es peor: el
            // respaldo falló igual, y eso ya está en el log y en el propio fallo del job.
            Log.Error("No se pudo registrar la alerta {Tipo}/{Clave}: {TipoExcepcion}: {Motivo}",
                tipo, clave, e.GetType().Name, e.Message);
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
}