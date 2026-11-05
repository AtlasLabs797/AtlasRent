using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Lo que todas las APIs de la aplicación repiten: la cabecera <c>Idempotency-Key</c>, la respuesta
/// guardada para el reintento, el envoltorio de lista de <c>docs/DOC_TECNICA.md §6</c> y los
/// <see cref="Problemas"/> con su código de referencia.
/// </summary>
public abstract class ControladorApiBase(
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    private readonly ServicioIdempotencia _idempotencia = idempotencia;
    private readonly ContextoSesion _contextoSesion = contextoSesion;
    private readonly IResolutorIpPeticion _resolutorIp = resolutorIp;

    protected static ConsultaLista Paginacion(int? page, int? pageSize, string? sortBy, string? sortDir) =>
        ConsultaLista.Crear(page, pageSize, sortBy, sortDir);

    /// <summary>La cabecera <c>Idempotency-Key</c> de esta petición, si viene.</summary>
    protected string? ClaveDeIdempotencia() =>
        Request.Headers.TryGetValue("Idempotency-Key", out var valor) ? valor.ToString() : null;

    protected MutacionIdempotente? Mutar(object cuerpo, string ruta) =>
        MutacionIdempotente.Crear(ClaveDeIdempotencia(), ruta, cuerpo);

    /// <summary>
    /// Respuesta ya guardada para esta clave, o <c>null</c> si es la primera vez. Va antes de
    /// tocar nada: un reintento no vuelve a ejecutar la mutación.
    /// </summary>
    protected async Task<IActionResult?> RepetidaAsync(MutacionIdempotente? mutacion, CancellationToken ct)
    {
        try
        {
            var guardada = await _idempotencia.ReplayAsync(UsuarioDeLaPeticion(), mutacion, ct);
            return guardada is null ? null : ContenidoJson(guardada);
        }
        catch (IdempotenciaEnConflictoException conflicto)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.IdempotenciaEnConflicto,
                "Clave de idempotencia en conflicto",
                conflicto.Message);
        }
        catch (ArgumentException claveInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Cabecera Idempotency-Key no válida",
                claveInvalida.Message);
        }
    }

    protected static ContentResult ContenidoJson(string cuerpo) => new()
    {
        Content = cuerpo,
        ContentType = "application/json",
        StatusCode = StatusCodes.Status200OK,
    };

    /// <summary>
    /// Envuelve una lista en <c>{ data, total, page, pageSize, totalPages }</c>. Un campo de orden
    /// que no existe es un 400, no una lista sin criterio.
    /// </summary>
    protected async Task<IActionResult> EnvoltorioAsync<T>(Task<Pagina<T>> pagina)
    {
        try
        {
            var resultado = await pagina;
            return Ok(new
            {
                data = resultado.Data,
                resultado.Total,
                resultado.Page,
                resultado.PageSize,
                resultado.TotalPages,
            });
        }
        catch (ArgumentException ordenInvalido)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Orden no válido",
                ordenInvalido.Message);
        }
    }

    protected Guid UsuarioDeLaPeticion() => _contextoSesion.UsuarioId!.Value;

    protected string Ip() => _resolutorIp.Resolver(HttpContext);

    protected IActionResult NoEncontrado(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status404NotFound,
            Problemas.RecursoNoEncontrado,
            "No encontrado",
            detalle);

    /// <summary>
    /// 409 para un valor duplicado o para un cambio que choca con lo que hay. Cubre el índice
    /// único, que también salta bajo concurrencia.
    /// </summary>
    protected IActionResult Conflicto(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status409Conflict,
            Problemas.ValorDuplicado,
            "No se puede guardar el cambio",
            detalle);
}