using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Tipos de cambio (F3-01, docs/ESPECIFICACION.md §3.12, D9 y D22).
/// <para>
/// <b>Los permisos son los que ya existen.</b> Ver el histórico usa <c>factura.ver</c>, que en la
/// matriz de §2 es "Ver facturas individuales" y es también lo que van a necesitar las pantallas de
/// factura y cobro para congelar el cambio. Cargar un valor a mano usa <c>configuracion.editar</c>:
/// es configuración de la casa y de su contador, no una tarea del día a día, así que el empleado no
/// la tiene. Ninguna acción nueva en <see cref="Acciones"/> ni en la base.
/// </para>
/// <para>
/// <b>Solo se carga a mano lo que la fuente no pudo traer.</b> El endpoint no pregunta de dónde
/// viene el valor: es siempre manual, y el histórico guarda el origen de cada fila.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio: un valor no positivo, el dólar (que vale 1 por definición) o
/// una moneda sin tipo de cambio para esa fecha.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/tipos-cambio")]
public sealed class TiposCambioController(
    ServicioTiposCambio tiposCambio,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] Moneda? moneda,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct) =>
        await EnvoltorioAsync(tiposCambio.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), moneda, desde, hasta, ct));

    /// <summary>
    /// Valor de una moneda en una fecha. Sin <c>fecha</c>, hoy en Santo Domingo: es lo que va a
    /// pedir la factura al emitirse.
    /// </summary>
    [HttpGet("actual")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Actual(
        [FromQuery] Moneda moneda,
        [FromQuery] DateOnly? fecha,
        CancellationToken ct) =>
        await ResponderAsync(() =>
            tiposCambio.ActualAsync(moneda, fecha ?? tiposCambio.FechaDeHoy(), ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => tiposCambio.LeerAsync(id, ct));

    /// <summary>
    /// Carga manual (§3.12). Sin fecha, la de hoy: lo normal es tapar un día que la fuente no trajo.
    /// </summary>
    [HttpPost]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> Cargar(TipoCambioPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/tipos-cambio";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => tiposCambio.RegistrarAsync(
            peticion.Fecha ?? tiposCambio.FechaDeHoy(),
            peticion.Moneda,
            peticion.Valor,
            FuentesTipoCambio.Manual,
            UsuarioDeLaPeticion(),
            Ip(),
            mutacion,
            ct));
    }

    private async Task<IActionResult> GuardarAsync<TResultado>(Func<Task<TResultado>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }

    private async Task<IActionResult> ResponderAsync<T>(Func<Task<T>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
    }
}
