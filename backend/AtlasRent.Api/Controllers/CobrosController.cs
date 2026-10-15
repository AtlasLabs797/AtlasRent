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
/// Cobros (F3-06, docs/ESPECIFICACION.md §3.10.1).
/// <para>
/// Los permisos son los que ya existen, sin crear ninguno: los GET usan <c>factura.ver</c> —ver un
/// cobro es ver el documento que salda una factura— y todo lo que mueve dinero usa
/// <c>cobro.registrar</c>, que D15 deja desactivado al empleado. El servicio repite la comprobación
/// porque las tareas de Hangfire entran por el servicio y no por HTTP.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio, igual que en <see cref="FacturasController"/>: asignar más de lo
/// pendiente, asignar a una factura de otro contrato, cobrar en una moneda sin tipo de cambio del día
/// o anular un cobro ya anulado.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/cobros")]
public sealed class CobrosController(
    ServicioCobros cobros,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Listado paginado con filtros de cliente, contrato, moneda, estado y rango de fechas efectivas.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? contratoId,
        [FromQuery] Moneda? moneda,
        [FromQuery] EstadoCobro? estado,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct) =>
        await EnvoltorioAsync(cobros.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir),
            clienteId,
            contratoId,
            moneda,
            estado,
            desde,
            hasta,
            ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => cobros.LeerAsync(id, ct));

    /// <summary>
    /// Registro de un cobro. Las asignaciones son opcionales: si no vienen, se propone la FIFO del
    /// contrato; si vienen, son las que se aplican.
    /// <para>
    /// <b>No hay endpoint de recibo en PDF</b> (TODO(F4-01)): el recibo es de la fase 4, con QuestPDF
    /// y las plantillas por serie. Un PDF provisional de dinero es un documento que alguien puede
    /// imprimir y archivar, así que no se sirve uno.
    /// </para>
    /// </summary>
    [HttpPost]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> Registrar(CobroPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/cobros");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => cobros.RegistrarAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Propuesta de asignación de un cobro <b>sin escribir nada</b>: mismas validaciones y mismo FIFO
    /// que el registro, más la deuda del contrato en otra moneda que este cobro no va a cubrir
    /// (§3.10.2, F3-07). Es lo que la interfaz enseña antes de que alguien confirme el cobro.
    /// </summary>
    [HttpPost("simular")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> Simular(CobroPeticion peticion, CancellationToken ct) =>
        await ResponderAsync(() => cobros.SimularAsync(peticion, ct));

    /// <summary>
    /// Anulación con motivo. <b>No borra</b>: el cobro queda anulado con su motivo, sus asignaciones
    /// dejan de contar sin desaparecer y el crédito vuelve al contrato (§3.10.1, AGENTS.md §6).
    /// </summary>
    [HttpPost("{id:guid}/anular")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> Anular(
        Guid id,
        CobroAnulacionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/cobros/{id:D}/anular");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => cobros.AnularAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Traduce los fallos de la mutación. El 409 cubre la regla de negocio y la restricción de la
    /// tabla, que es la que salta si algo escribe por fuera de la API (dos cobros que crean a la vez
    /// la fila de saldo a favor del mismo contrato).
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