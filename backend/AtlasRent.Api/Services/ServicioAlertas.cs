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
    /// Cerrar es poner <c>resuelta_at</c> y nada más: la fila sale del índice parcial
    /// (<c>WHERE resuelta_at IS NULL</c>), así que no puede chocar con nada, y el <c>UPDATE</c> lleva
    /// <c>AND resuelta_at IS NULL</c> para que resolver dos veces no pise la fecha de la primera.
    /// </para>
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
/// instante se pisan—, así que se hace un <c>INSERT … ON CONFLICT DO UPDATE</c> sobre el índice
/// único. Es la misma razón por la que el MFA no gasta el token asignando la propiedad en memoria:
/// leer y luego escribir deja hueco entre los dos.
/// </para>
/// <para>
/// <b>Alerta persistente, sin duplicar (F2-09, BUG-008).</b> La alerta de un contrato finalizado sin
/// liquidar tiene que seguir viva mientras el problema siga vivo (§3.5: "alerta persistente hasta
/// liquidar"), y eso NO es crear una alerta nueva cada día: cien filas de lo mismo no son una alerta
/// sino ruido que nadie mira. Lo que hay es una fila que el <c>ON CONFLICT DO UPDATE</c> refresca: le
/// sube el contador de repeticiones y le reescribe el mensaje con el estado actual, así que el texto
/// dice "lleva N días" y no "vio esto aquel día".
/// </para>
/// <para>
/// <b>Una alerta cerrada vuelve a levantarse si el problema regresa.</b> Por eso el índice es único
/// <b>parcial</b>, sobre <c>(tipo, clave)</c> y filtrado a <c>WHERE resuelta_at IS NULL</c>: la
/// restricción solo rige las alertas vivas. Con el único de <c>clave</c> a secas, el
/// <c>ON CONFLICT … DO UPDATE … WHERE resuelta_at IS NULL</c> que se usaba antes no insertaba nada ni
/// resucitaba nada cuando la fila ya estaba cerrada —un numerador que se quedaba corto, se ampliaba y
/// volvía a quedarse corto no volvía a avisar— y eso es justo lo contrario de §3.16 y de AGENTS.md §6.
/// Cerrar sigue siendo poner <c>resuelta_at</c>, nunca borrar: la fila dice que pasó, cuándo y
/// cuántas veces se vio.
/// </para>
/// </summary>
public sealed class ServicioAlertas(AtlasRentDbContext contexto, TimeProvider reloj) : IRegistroAlertas
{
    public async Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct)
    {
        var ocurrido = reloj.GetUtcNow();

        try
        {
            // Paso 1 — reabrir la fila cerrada con esa misma (tipo, clave), si la hay.
            //
            // Una fila cerrada está FUERA del índice parcial, así que el `ON CONFLICT` de abajo no la ve
            // y no podría resucitarla: sin este paso, una condición que aparece, se resuelve y vuelve
            // a aparecer no volvería a avisar (BUG-008). Va antes del INSERT a propósito, y el INSERT
            // es el que sube el contador, para que en cada uno de los tres casos posibles el número de
            // veces que se ha visto suba exactamente una vez y ni una más.
            //
            // Por qué reabre **la misma fila** y no crea otra: la clave es la identidad del asunto
            // («este numerador se está quedando sin números»), no el número de un aviso. Dos filas para
            // el mismo asunto obligan a leerlas todas para saber si hay un problema vivo, y el contador
            // de repeticiones —que es lo que dice "lleva tres días fallando"— se partiría en dos. Y no
            // se borra nada: reabrir una fila no pierde el rastro, lo que se conserva es que pasó,
            // cuándo y cuántas veces se vio, con `ocurrido_at` apuntando a esta última vez.
            //
            // El `NOT EXISTS` sobre la alerta viva es lo que hace segura la reapertura: sin él, si
            // hubiera una fila abierta, reabrir la cerrada daría dos filas vivas con la misma clave y
            // el índice parcial la rechazaría. Y `ORDER BY ocurrido_at DESC LIMIT 1` reabre solo la
            // más reciente: con el índice parcial la historia puede tener más de una fila cerrada con
            // la misma clave, y reabrirlas todas a la vez sería el mismo choque.
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE alerta
                 SET resuelta_at = NULL,
                     ocurrido_at = {ocurrido},
                     mensaje = {mensaje}
                 WHERE id = (
                     SELECT previa.id
                     FROM alerta previa
                     WHERE previa.tipo = {tipo}
                       AND previa.clave = {clave}
                       AND previa.resuelta_at IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1
                           FROM alerta viva
                           WHERE viva.tipo = {tipo}
                             AND viva.clave = {clave}
                             AND viva.resuelta_at IS NULL)
                     ORDER BY previa.ocurrido_at DESC
                     LIMIT 1)
                 """,
                ct);

            // Paso 2 — insertar si no había ninguna fila, refrescar la que hubiera. El destino del
            // conflicto es el índice parcial, con su filtro: sin el `WHERE resuelta_at IS NULL` detrás
            // del `ON CONFLICT`, PostgreSQL no reconocería el índice y la sentencia no compilaría.
            // Este es el único punto que suma una repetición, tanto si choca (ya estaba viva o se ha
            // reabierto en el paso 1) como si no (no había fila y se inserta con 1).
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO alerta (id, clave, tipo, mensaje, ocurrido_at, repeticiones)
                 VALUES ({Guid.NewGuid()}, {clave}, {tipo}, {mensaje}, {ocurrido}, 1)
                 ON CONFLICT (tipo, clave) WHERE resuelta_at IS NULL DO UPDATE
                 SET repeticiones = alerta.repeticiones + 1,
                     ocurrido_at = {ocurrido},
                     mensaje = {mensaje}
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