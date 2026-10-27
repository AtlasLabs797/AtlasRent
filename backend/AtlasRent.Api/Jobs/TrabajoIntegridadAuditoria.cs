using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Verificación diaria de la cadena de firmas de <c>auditoria</c> (docs/DOC_TECNICA.md §8).
/// <para>
/// <strong>El rol no es un detalle de este trabajo: es la condición para que sirva.</strong>
/// La política <c>auditoria_lectura</c> de la migración inicial deja ver las filas a
/// <c>rent_app</c> solo si <c>app.rol</c> es <c>admin</c> o <c>gerente</c>. Con cualquier otro rol
/// (o sin rol) la consulta no da error: devuelve cero filas. Una cadena de firmas que ha sido
/// manipulada se seguiría comprobando como "íntegra" porque el registro roto ni se lee, así que el
/// trabajoaría en verde sobre una auditoría manipulada, que es justo lo que existe para detectar.
/// Por eso aquí el rol se fija a mano en <see cref="ContextoSesion"/> y la lectura va dentro de una
/// transacción, que es lo único donde <c>set_config</c> con <c>app.rol</c> tiene efecto.
/// </para>
/// <para>
/// No se impostura a un usuario: <c>app.usuario_id</c> se queda vacío, porque este trabajo no es
/// la acción de nadie. El ámbito de sesión de Hangfire es uno por ejecución, así que el rol no se
/// escapa a las peticiones web.
/// </para>
/// </summary>
public sealed class TrabajoIntegridadAuditoria(
    ContextoSesion sesion,
    AtlasRentDbContext contexto,
    IntegridadAuditoria integridad,
    OpcionesLimites limites,
    IRegistroAlertas alertas)
{
    /// <summary>Clave de la alerta de cadena rota: una sola fila viva, con contador de repeticiones.</summary>
    public const string ClaveAlerta = "integridad-auditoria";

    public const string TipoAlerta = "auditoria";

    /// <summary>Rol con el que tiene que correr. Ver el comentario de la clase antes de tocarlo.</summary>
    public const string RolRequisito = Roles.Admin;

    /// <summary>
    /// Recorre la cadena y devuelve el resultado. Hangfire lo ignora (a un trabajo no le importa lo
    /// que devuelva), pero así una prueba puede comprobar lo que vio sin leer el registro.
    /// </summary>
    public async Task<ResultadoIntegridad> EjecutarAsync(CancellationToken ct)
    {
        sesion.Rol = RolRequisito;

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var resultado = await integridad.VerificarAsync(ct, transaccion.GetDbTransaction());
        await transaccion.CommitAsync(ct);

        await PurgarIntentosLoginAsync(ct);

        if (resultado.Integra)
        {
            Log.Information("Cadena de auditoría íntegra: {Registros} registros recorridos", resultado.TotalRegistros);
            return resultado;
        }

        Log.Error(
            "Cadena de auditoría ROTA en la secuencia {Secuencia} (registro {Id}); {Recorridos} "
            + "registros recorridos antes de encontrarlo",
            resultado.SecuenciaRota,
            resultado.IdRota,
            resultado.TotalRegistros);

        // Un log no despierta a nadie: la cadena rota queda tambien en la tabla de alertas (BUG-045).
        // La clave es fija, asi que cada ejecucion diaria sube el contador de la misma fila viva.
        await alertas.RegistrarAsync(
            TipoAlerta,
            ClaveAlerta,
            $"La cadena de firmas de auditoria esta rota en la secuencia {resultado.SecuenciaRota} "
            + $"(registro {resultado.IdRota}). Revisar si alguien ha tocado la tabla por debajo de la aplicacion.",
            ct);

        return resultado;
    }

    /// <summary>
    /// SEC-030: borra los intentos de login más viejos que la retención configurada, con la función
    /// <c>purgar_intentos_login</c> (la API solo puede leer e insertar en la tabla). Un fallo aquí se
    /// registra pero no tumba la comprobación de integridad, que es lo que este trabajo existe para hacer.
    /// </summary>
    private async Task PurgarIntentosLoginAsync(CancellationToken ct)
    {
        try
        {
            var dias = limites.DiasConservarIntentosLogin;
            var borrados = await contexto.Database
                .SqlQuery<long>($"SELECT purgar_intentos_login({dias}) AS \"Value\"")
                .SingleAsync(ct);
            Log.Information("Purga de intentos de login: {Borrados} filas de más de {Dias} días", borrados, dias);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error(e, "No se pudo purgar intento_login");
        }
    }
}
