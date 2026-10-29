using System.Text.Json.Nodes;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lectura de la auditoría (docs/ESPECIFICACION.md §2: admin y gerente). La base ya lo restringe con
/// RLS (<c>auditoria_lectura</c>); el endpoint exige además <c>auditoria.ver</c>.
/// <para>
/// No devuelve la firma ni la firma anterior ni la secuencia: son la cadena de integridad, que
/// verifica el trabajo diario. Y <c>Datos</c> sale con enmascarados los campos cuyo nombre indica un
/// secreto o un documento de identidad: la regla es que la auditoría no los lleve, y esto es la
/// segunda barrera por si algún día uno se cuela.
/// </para>
/// </summary>
public sealed class ServicioLecturaAuditoria(AtlasRentDbContext contexto)
{
    public const string Enmascarado = "***";

    // Fragmentos de nombre de campo (sin guiones bajos, en minúsculas) cuyo valor no se devuelve.
    private static readonly string[] FragmentosSensibles =
    [
        "password", "contrasena", "hash", "secret", "token", "clave", "firma",
        "cedula", "rnc", "pasaporte", "apikey", "documentoidentidad", "numerodocumento",
    ];

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<Auditoria>, bool, IOrderedQueryable<Auditoria>>>
        Ordenes = new Dictionary<string, Func<IQueryable<Auditoria>, bool, IOrderedQueryable<Auditoria>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["fecha"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Secuencia)
                : q.OrderBy(a => a.CreatedAt).ThenBy(a => a.Secuencia),
            ["accion"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.Accion).ThenByDescending(a => a.Secuencia)
                : q.OrderBy(a => a.Accion).ThenBy(a => a.Secuencia),
            ["entidad"] = (q, desc) => desc
                ? q.OrderByDescending(a => a.Entidad).ThenByDescending(a => a.Secuencia)
                : q.OrderBy(a => a.Entidad).ThenBy(a => a.Secuencia),
        };

    /// <param name="desde">Primer día incluido, en la zona de negocio.</param>
    /// <param name="hasta">Último día incluido, en la zona de negocio.</param>
    public async Task<Pagina<AuditoriaFila>> ListarAsync(
        ConsultaLista paginacion,
        DateOnly? desde,
        DateOnly? hasta,
        Guid? usuarioId,
        string? entidad,
        string? entidadId,
        string? accion,
        CancellationToken ct)
    {
        // RLS lee app.rol, que el interceptor solo fija al abrir una transacción: sin ella la política
        // de auditoria no ve el rol y la lista sale siempre vacía.
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        IQueryable<Auditoria> consulta = contexto.Auditoria.AsNoTracking();

        if (desde is { } d)
        {
            var inicio = InicioDelDia(d);
            consulta = consulta.Where(a => a.CreatedAt >= inicio);
        }

        if (hasta is { } h)
        {
            var finExclusivo = InicioDelDia(h.AddDays(1));
            consulta = consulta.Where(a => a.CreatedAt < finExclusivo);
        }

        if (usuarioId is { } u)
        {
            consulta = consulta.Where(a => a.UsuarioId == u);
        }

        if (!string.IsNullOrWhiteSpace(entidad))
        {
            var e = entidad.Trim();
            consulta = consulta.Where(a => a.Entidad == e);
        }

        if (!string.IsNullOrWhiteSpace(entidadId))
        {
            var e = entidadId.Trim();
            consulta = consulta.Where(a => a.EntidadId == e);
        }

        if (!string.IsNullOrWhiteSpace(accion))
        {
            var porAccion = accion.Trim();
            consulta = consulta.Where(a => a.Accion == porAccion);
        }

        var pagina = await ConsultaPaginada.PaginarAsync(consulta, Ordenes, "fecha", paginacion, a => a, ct);

        var ids = pagina.Data.Where(a => a.UsuarioId != null).Select(a => a.UsuarioId!.Value).Distinct().ToList();
        var nombres = await contexto.Usuario.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);

        await transaccion.CommitAsync(ct);

        return new Pagina<AuditoriaFila>(
            [.. pagina.Data.Select(a => new AuditoriaFila(
                a.Id,
                a.CreatedAt,
                a.UsuarioId,
                a.UsuarioId is { } id && nombres.TryGetValue(id, out var nombre) ? nombre : null,
                a.Ip,
                a.Accion,
                a.Entidad,
                a.EntidadId,
                a.Resultado,
                a.Origen,
                a.Datos is null ? null : Enmascarar(JsonNode.Parse(a.Datos.RootElement.GetRawText()))))],
            pagina.Total,
            pagina.Page,
            pagina.PageSize,
            pagina.TotalPages);
    }

    private static DateTimeOffset InicioDelDia(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TiempoNegocio.Zona.GetUtcOffset(local)).ToUniversalTime();
    }

    internal static JsonNode? Enmascarar(JsonNode? nodo)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                // Se recorre en sitio: reasignar un nodo a su propio padre lanza "already has a parent".
                foreach (var clave in objeto.Select(p => p.Key).ToList())
                {
                    if (EsSensible(clave))
                    {
                        objeto[clave] = JsonValue.Create(Enmascarado);
                    }
                    else
                    {
                        Enmascarar(objeto[clave]);
                    }
                }

                return objeto;
            case JsonArray lista:
                foreach (var elemento in lista)
                {
                    Enmascarar(elemento);
                }

                return lista;
            default:
                return nodo;
        }
    }

    private static bool EsSensible(string clave)
    {
        var normal = clave.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return FragmentosSensibles.Any(normal.Contains);
    }
}
