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
    IntegridadAuditoria integridad)
{
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

        return resultado;
    }
}