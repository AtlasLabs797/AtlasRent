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
/// Facturas y proformas (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Los permisos son los que ya existen, sin crear ninguno: los GET usan <c>factura.ver</c>, la
/// escritura en borrador usa <c>factura.borrador</c> y la emisión usa <c>factura.emitir</c>, que D15
/// deja desactivado al empleado — puede preparar una factura y no puede emitirla. El servicio repite
/// la comprobación porque F3-05 emite desde un trabajo, sin sesión HTTP ni filtro.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio, igual que en <see cref="CotizacionesController"/>: emitir lo
/// que no está en borrador, cambiar lo que ya salió, una factura en una moneda que su serie no admite
/// o una emisión sin tipo de cambio del día.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/facturas")]
public sealed class FacturasController(
    ServicioFacturas facturas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Listado paginado con filtros de estado, serie, cliente, unidad, moneda y rango de fechas de
    /// emisión. Sin totales agregados: los informes son de §3.15 y solo gerente y administrador los
    /// ven (F3-10).
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] Guid? serieId,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] Moneda? moneda,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct) =>
        await EnvoltorioAsync(facturas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir),
            estado,
            serieId,
            clienteId,
            unidadId,
            moneda,
            desde,
            hasta,
            ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => facturas.LeerAsync(id, ct));

    [HttpPost]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Crear(FacturaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/facturas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.CrearAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Actualizar(
        Guid id,
        FacturaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.ActualizarAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Alta de una línea. Devuelve la factura entera con los totales recalculados, porque quien
    /// acaba de añadir un descuento necesita ver el total nuevo sin pedirlo aparte.
    /// </summary>
    [HttpPost("{id:guid}/lineas")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> AgregarLinea(
        Guid id,
        FacturaLineaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/lineas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.AgregarLineaAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}/lineas/{lineaId:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> EditarLinea(
        Guid id,
        Guid lineaId,
        FacturaLineaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/lineas/{lineaId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.EditarLineaAsync(
            id, lineaId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Baja de una línea, solo en borrador. En una factura emitida es un 409: lo que se hace con lo
    /// que ya salió es anularlo (F3-08), no borrar una línea y dejar el documento descuadrado.
    /// </summary>
    [HttpDelete("{id:guid}/lineas/{lineaId:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> BorrarLinea(Guid id, Guid lineaId, CancellationToken ct)
    {
        var mutacion = Mutar(new { id, lineaId }, $"/api/v1/facturas/{id:D}/lineas/{lineaId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.BorrarLineaAsync(
            id, lineaId, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Emisión: número de serie, fecha y tipo de cambio congelado, todo en una transacción (D28).
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve la factura ya emitida en vez de
    /// sacar un segundo número.
    /// </summary>
    [HttpPost("{id:guid}/emitir")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> Emitir(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/facturas/{id:D}/emitir");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.EmitirAsync(
            id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos. Un 409 cubre tanto el rechazo previo de una regla
    /// como la restricción de la tabla, que es la que salta si algo escribe por fuera de la API.
    /// <para>
    /// La emisión puede chocar con dos caras distintas de la misma carrera y se capturan las dos: la
    /// violación de unicidad (<c>UniqueViolation</c> dentro de un <see cref="DbUpdateException"/>) y
    /// el conflicto de concurrencia (<see cref="DbUpdateConcurrencyException"/>, que deriva del
    /// anterior pero no lleva el <see cref="PostgresException"/> dentro). Con una sola de las dos, la
    /// otra se escapes como un 500.
    /// </para>
    /// </summary>
    private async Task<IActionResult> GuardarAsync<TResultado>(Func<Task<TResultado>> operacion)
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
        catch (DbUpdateConcurrencyException concurrencia)
        {
            return Conflicto(
                $"El cambio choca con otra modificación ({concurrencia.Message}). Vuelve a leer y reintenta.");
        }
        catch (DbUpdateException duplicado) when (duplicado.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflicto($"Ya existe un documento con esos valores ({duplicado.InnerException.Message}).");
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
