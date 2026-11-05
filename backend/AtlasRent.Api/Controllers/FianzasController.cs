using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Fianzas de contrato (F3-09, docs/ESPECIFICACION.md §3.11 y §3.6).
/// <para>
/// <b>No hay endpoint para cobrar una fianza</b>, y no es un olvido: cobrar es registrar un
/// <c>cobro</c> con una asignación a la fianza (<see cref="CobrosController"/>), porque el dinero entra
/// por la misma puerta y por eso sale por los informes de caja (F5) por el mismo camino. Esta API es la
/// del depósito: cuánto se exige, cuánto se ha cobrado, cuánto queda y qué se hace con él.
/// </para>
/// <para>
/// Los GET usan <c>factura.ver</c> —la fianza es una fila más de la cuenta del contrato, §3.13— y
/// todo lo que mueve dinero usa <c>fianza.mover</c>, que §2 deja en gerente y administrador. El
/// servicio repite la comprobación porque el trabajo diario de las fianzas entra por el servicio.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/fianzas")]
public sealed class FianzasController(
    ServicioFianzas fianzas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Listado paginado con filtros de contrato y moneda. Cada fila trae lo exigido, el número de
    /// fianzas, lo cobrado, lo devuelto, lo retenido y el saldo, que es lo que F3-10 pinta.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] Guid? contratoId,
        [FromQuery] Moneda? moneda,
        CancellationToken ct) =>
        await EnvoltorioAsync(fianzas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir),
            contratoId,
            moneda,
            ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => fianzas.LeerAsync(id, ct));

    /// <summary>
    /// Alta de la fianza de un contrato. Una por contrato y moneda: la segunda es un 409.
    /// </summary>
    [HttpPost]
    [Accion(Acciones.FianzaMover)]
    public async Task<IActionResult> Crear(FianzaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/fianzas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => fianzas.AltaAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Devolución, entera o en parte (F3-09, §3.11 y §3.6).
    /// <para>
    /// <b>Una devolución parcial exige importe descontado y motivo</b>: sin ellos es un 400 que lo dice,
    /// y devolver más de lo cobrado es un 409 con el saldo y lo pedido.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/devolver")]
    [Accion(Acciones.FianzaMover)]
    public async Task<IActionResult> Devolver(
        Guid id,
        FianzaDevolucionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/fianzas/{id:D}/devolver");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => fianzas.DevolverAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Retener la fianza entera (§3.6). No mueve dinero: solo deja la decisión y su fecha.
    /// <para>
    /// <b>Sin <c>Idempotency-Key</c></b> y a propósito: un reintento con la misma clave devolvería la
    /// primera decisión en vez de dar el 409 que dice "ya está retenida", que es justo lo que quien
    /// reintenta necesita ver.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/retener")]
    [Accion(Acciones.FianzaMover)]
    public async Task<IActionResult> Retener(Guid id, CancellationToken ct) =>
        await GuardarAsync(() => fianzas.RetenerAsync(id, UsuarioDeLaPeticion(), Ip(), null, ct));

    /// <summary>
    /// Poner la fianza en liquidación: es ejecutar lo que la liquidación del contrato decidió
    /// (F2-09, <see cref="DecisionFianza"/>).
    /// </summary>
    [HttpPost("{id:guid}/liquidar")]
    [Accion(Acciones.FianzaMover)]
    public async Task<IActionResult> Liquidar(Guid id, CancellationToken ct) =>
        await GuardarAsync(() => fianzas.LiquidarAsync(id, UsuarioDeLaPeticion(), Ip(), null, ct));

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Traduce los fallos de la mutación. El 409 es la regla de negocio y también la restricción de la
    /// tabla, que salta si algo escribe por fuera de la API (dos fianzas del mismo contrato y moneda
    /// creados a la vez).
    /// </summary>
    private async Task<IActionResult> GuardarAsync<TResultado>(Func<Task<TResultado>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (ArgumentException peticionInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Petición no válida",
                peticionInvalida.Message);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
        catch (DbUpdateConcurrencyException concurrencia)
        {
            return Conflicto(
                $"El cambio choca con otra modificación ({concurrencia.Message}). Vuelve a leer y reintenta.");
        }
        catch (DbUpdateException duplicado) when (duplicado.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflicto($"Ya existe un registro con esos valores ({duplicado.InnerException.Message}).");
        }
        catch (DbUpdateException fallo)
        {
            return Conflicto(fallo.Message);
        }
    }

    private async Task<IActionResult> ResponderAsync<T>(Func<Task<T>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (ArgumentException peticionInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Petición no válida",
                peticionInvalida.Message);
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