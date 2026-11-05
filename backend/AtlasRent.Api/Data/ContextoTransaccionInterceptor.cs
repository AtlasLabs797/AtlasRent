using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtlasRent.Api.Data;

/// <summary>
/// Fija <c>app.usuario_id</c> y <c>app.rol</c> en cada transacción, como pide
/// docs/DOC_TECNICA.md §4.3 para que RLS tenga contexto.
/// <para>
/// Es un interceptor de transacción y no de comandos porque el <c>set_config</c> tiene que
/// ejecutarse <em>dentro</em> de la transacción y una sola vez.
/// </para>
/// </summary>
public sealed class ContextoTransaccionInterceptor(ContextoSesion sesion) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result)
    {
        using var comando = CrearComando(connection, result);
        comando.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var comando = CrearComando(connection, result);
        await comando.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private DbCommand CrearComando(DbConnection connection, DbTransaction transaction)
    {
        var comando = connection.CreateCommand();
        comando.Transaction = transaction;
        // El tercer parámetro (true) deja el valor ligado a la transacción: al hacer
        // rollback desaparece y no se filtra a la siguiente petición de la misma conexión.
        comando.CommandText = "SELECT set_config('app.usuario_id', @usuario_id, true), set_config('app.rol', @rol, true)";

        var usuario = comando.CreateParameter();
        usuario.ParameterName = "@usuario_id";
        usuario.Value = sesion.UsuarioId?.ToString() ?? string.Empty;
        comando.Parameters.Add(usuario);

        var rol = comando.CreateParameter();
        rol.ParameterName = "@rol";
        rol.Value = sesion.Rol ?? string.Empty;
        comando.Parameters.Add(rol);

        return comando;
    }
}