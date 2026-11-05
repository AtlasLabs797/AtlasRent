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
/// Cobros (F3-06, docs/ESPECIFICACION.md §3.10.1) y ajuste por diferencia de cambio (F3-07, §3.10.2).
/// <para>
/// Los permisos son los que ya existen, sin crear ninguno: los GET usan <c>factura.ver</c> —ver un
/// cobro es ver el documento que salda una factura— y todo lo que mueve dinero, incluido el ajuste,
/// usa <c>cobro.registrar</c>, que D15 deja desactivado al empleado. El servicio repite la
/// comprobación porque las tareas de Hangfire entran por el servicio y no por HTTP.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio, igual que en <see cref="FacturasController"/>: asignar más de lo
/// pendiente, asignar a una factura de otro contrato, cobrar en una moneda sin tipo de cambio del día,
/// anular un cobro ya anulado o intentar ajustar una diferencia mayor que la tolerancia.
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

    // ---------------------------------------------------------------- recibo en PDF (F4-01)

    /// <summary>
    /// Recibo del cobro con el diseño de <c>docs/DESIGN.md §7</c> (F4-01, §3.10.1).
    /// <para>
    /// Usa <c>factura.ver</c> y no <c>cobro.registrar</c>: es el papel de un cobro que ya existe, y lo
    /// que lo descarga es quien lo está leyendo, igual que el PDF de la factura (§3.14).
    /// </para>
    /// <para>
    /// <b>Un cobro anulado se descarga</b>, con la marca de agua «ANULADO» y el motivo al pie: el papel
    /// marcado es lo que necesita el expediente del cliente. No lleva <c>Idempotency-Key</c> ni
    /// <c>Mutar</c> porque no muta nada.
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        try
        {
            var pdf = await cobros.PdfAsync(id, ct);
            return File(pdf, "application/pdf", $"recibo-{id:D}.pdf");
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (Exception invalida) when (invalida is InvalidOperationException or ArgumentException)
        {
            // 409: el mismo caso que el PDF de la factura —una plantilla que no está en el catálogo—,
            // que solo se da con datos escritos por fuera de la API. Omitir el documento entero no es
            // una opción: el dinero ya entró, y el papel que lo prueba es este.
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.PlantillaDesconocida,
                "No se puede generar el recibo",
                invalida.Message);
        }
    }

    /// <summary>
    /// Registro de un cobro. Las asignaciones son opcionales: si no vienen, se propone la FIFO del
    /// contrato; si vienen, son las que se aplican.
    /// <para>
    /// El recibo en PDF es <c>GET /api/v1/cobros/{id}/pdf</c> (F4-01), y lo pide quien registra el cobro
    /// cuando lo necesita: es opcional y no se genera solo (§3.10.1).
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

    // ---------------------------------------------------------------- ajuste por diferencia de cambio

    /// <summary>
    /// Cierra por ajuste el pendiente que dejó una diferencia de cambio (F3-07, §3.10.2).
    /// <para>
    /// <b>No se pide importe</b>: es el pendiente real de la factura, y solo se acepta si cabe en la
    /// tolerancia configurada. Con una diferencia mayor el 409 dice las dos salidas que da §3.10.2 —
    /// que el cliente pague la diferencia, o que se corrija el cambio del cobro anterior.
    /// </para>
    /// <para>
    /// El permiso es <c>cobro.registrar</c>, el mismo que registrar y anular un cobro: los tres
    /// mueven el saldo que la factura tiene pendiente. No se crea un permiso nuevo porque tocar la
    /// matriz de permisos por un solo endpoint es más riesgo del que aporta.
    /// </para>
    /// </summary>
    [HttpPost("ajustes-diferencia-cambio")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> RegistrarAjuste(
        AjusteDiferenciaCambioPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/cobros/ajustes-diferencia-cambio");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => cobros.RegistrarAjusteAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Anulación con motivo. <b>No borra</b>: la fila queda anulada con su motivo y su fecha y deja de
    /// contar, y la factura recupera el pendiente que tenía (AGENTS.md §6).
    /// </summary>
    [HttpPost("ajustes-diferencia-cambio/{id:guid}/anular")]
    [Accion(Acciones.CobroRegistrar)]
    public async Task<IActionResult> AnularAjuste(
        Guid id,
        AjusteDiferenciaCambioAnulacionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/cobros/ajustes-diferencia-cambio/{id:D}/anular");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => cobros.AnularAjusteAsync(
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