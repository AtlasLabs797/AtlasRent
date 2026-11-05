using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>Lista paginada de <c>docs/DOC_TECNICA.md §6</c>.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Data, int Total, int Page, int PageSize, int TotalPages);

/// <summary>Parámetros de página y orden de una lista.</summary>
public sealed record ConsultaLista(int Page, int PageSize, string? SortBy, string SortDir)
{
    public const string OrdenAscendente = "asc";

    public const string OrdenDescendente = "desc";

    public const int PageSizePorDefecto = 25;

    public const int PageSizeMaximo = 200;

    /// <summary>
    /// Normaliza lo que llega por la cadena de consulta. Página y tamaño se ajustan al rango en vez
    /// de fallar: un <c>pageSize</c> enorme es un cliente que recorre la lista, no un error.
    /// Un campo de orden desconocido sí se rechaza, para que una errata no se pase por unnoticed.
    /// </summary>
    public static ConsultaLista Crear(int? page, int? pageSize, string? sortBy, string? sortDir) =>
        new(
            Math.Max(page ?? 1, 1),
            Math.Clamp(pageSize ?? PageSizePorDefecto, 1, PageSizeMaximo),
            string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim(),
            string.Equals(sortDir, OrdenDescendente, StringComparison.OrdinalIgnoreCase)
                ? OrdenDescendente
                : OrdenAscendente);

    /// <summary>Si el orden es de mayor a menor.</summary>
    public bool EsDescendente => SortDir == OrdenDescendente;
}

/// <summary>Paginación y orden de las listas de configuración.</summary>
public static class ConsultaPaginada
{
    /// <summary>Aplica el orden, corta la página y proyecta. El orden se valida aquí.</summary>
    /// <param name="ordenes">
    /// Campos por los que se puede ordenar. Lo que no esté en el diccionario se rechaza: ordenar
    /// por una columna que no existe no es un error silencioso que se pueda pasar por alto.
    /// </param>
    /// <param name="ordenPorDefecto">Clave de <paramref name="ordenes"/> usada si no se pide otra.</param>
    public static async Task<Pagina<TResultado>> PaginarAsync<TEntidad, TResultado>(
        IQueryable<TEntidad> consulta,
        IReadOnlyDictionary<string, Func<IQueryable<TEntidad>, bool, IOrderedQueryable<TEntidad>>> ordenes,
        string ordenPorDefecto,
        ConsultaLista paginacion,
        Func<TEntidad, TResultado> proyectar,
        CancellationToken ct)
    {
        var clave = paginacion.SortBy ?? ordenPorDefecto;
        if (!ordenes.TryGetValue(clave, out var ordenar))
        {
            throw new ArgumentException(
                $"No se puede ordenar por '{clave}'. Campos: {string.Join(", ", ordenes.Keys)}.",
                nameof(paginacion));
        }

        var total = await consulta.CountAsync(ct);

        var filas = await ordenar(consulta, paginacion.EsDescendente)
            .Skip((paginacion.Page - 1) * paginacion.PageSize)
            .Take(paginacion.PageSize)
            .ToListAsync(ct);

        return new Pagina<TResultado>(
            [.. filas.Select(proyectar)],
            total,
            paginacion.Page,
            paginacion.PageSize,
            (int)Math.Ceiling(total / (double)paginacion.PageSize));
    }
}