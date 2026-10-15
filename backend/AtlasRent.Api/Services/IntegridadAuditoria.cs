using System.Data;
using System.Data.Common;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>Resultado de recorrer la cadena de firmas.</summary>
/// <param name="TotalRegistros">Cuántos registros se han recorrido (cadena entera si todo cuadra).</param>
/// <param name="SecuenciaRota">Secuencia del primer registro cuya firma no cuadra, o <c>null</c>.</param>
/// <param name="IdRota">Id de ese registro, o <c>null</c>.</param>
public sealed record ResultadoIntegridad(int TotalRegistros, long? SecuenciaRota, Guid? IdRota)
{
    public bool Integra => SecuenciaRota is null;
}

/// <summary>
/// Recorre la cadena de firmas en orden y devuelve el primer registro manipulado.
/// Solo lectura; lo ejecuta el job diario (docs/DOC_TECNICA.md §8).
/// <para>
/// Para ver todas las filas hace falta <c>app.rol</c> de admin o gerente, porque la lectura de
/// <c>auditoria</c> está restringida por RLS.
/// </para>
/// </summary>
public sealed class IntegridadAuditoria(AtlasRentDbContext contexto)
{
    private const string ConsultaOrdenada = """
        SELECT secuencia, id, usuario_id, accion, entidad, entidad_id, datos::text AS datos,
               ip, resultado, origen, created_at, firma, firma_anterior
        FROM auditoria
        ORDER BY secuencia
        """;

    /// <param name="ct">Cancelación.</param>
    /// <param name="transaccion">
    /// Transacción en la que se lee. hace falta para el trabajo diario: el <c>app.rol</c> que
    /// desbloquea la RLS lo fija <see cref="ContextoTransaccionInterceptor"/> al abrir una
    /// transacción, y su valor vale solo dentro de ella. Sin <paramref name="transaccion"/> la
    /// lectura va suelta y, si el rol no está puesto, <c>auditoria</c> no devuelve ni una fila.
    /// </param>
    public async Task<ResultadoIntegridad> VerificarAsync(
        CancellationToken ct = default,
        DbTransaction? transaccion = null)
    {
        var conexion = contexto.Database.GetDbConnection();
        if (conexion.State != ConnectionState.Open)
        {
            await conexion.OpenAsync(ct);
        }

        await using var comando = conexion.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText = ConsultaOrdenada;
        await using var lector = await comando.ExecuteReaderAsync(ct);

        var anterior = HuellaAuditoria.FirmaGenesis;
        var total = 0;

        while (await lector.ReadAsync(ct))
        {
            total++;

            var fila = new Auditoria
            {
                Secuencia = lector.GetInt64(0),
                Id = lector.GetGuid(1),
                UsuarioId = lector.IsDBNull(2) ? null : lector.GetGuid(2),
                Accion = lector.GetString(3),
                Entidad = lector.IsDBNull(4) ? null : lector.GetString(4),
                EntidadId = lector.IsDBNull(5) ? null : lector.GetString(5),
                Ip = lector.GetString(7),
                Resultado = lector.IsDBNull(8) ? null : lector.GetString(8),
                Origen = lector.IsDBNull(9) ? null : lector.GetString(9),
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(lector.GetDateTime(10), DateTimeKind.Utc)),
                Firma = lector.GetString(11),
                FirmaAnterior = lector.GetString(12),
            };
            var datosTexto = lector.IsDBNull(6) ? null : lector.GetString(6);

            // Dos cosas pueden romperse: que la fila apunte a otra firma anterior (una fila se
            // movió de sitio) o que el contenido no case con su propia firma (una fila se editó).
            var esperada = HuellaAuditoria.Calcular(anterior, fila, datosTexto);
            var encadenada = string.Equals(anterior, fila.FirmaAnterior, StringComparison.Ordinal);
            if (!encadenada || !string.Equals(esperada, fila.Firma, StringComparison.Ordinal))
            {
                return new ResultadoIntegridad(total, fila.Secuencia, fila.Id);
            }

            anterior = fila.Firma;
        }

        return new ResultadoIntegridad(total, null, null);
    }
}