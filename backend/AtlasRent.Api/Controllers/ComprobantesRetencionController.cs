using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Comprobantes de retención de las facturas de cliente agente de retención (F3-04,
/// docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// Va en su propio controlador, y no como un endpoint más de <see cref="FacturasController"/>, por
/// lo que hace: anotar la recepción de un papel. No es tocar el borrador de una factura ni su
/// emisión, es otra cosa y con otro ritmo —una factura emitida ya no se toca, pero su comprobante de
/// retención llega días después, cuando el cliente entrega el papel.
/// </para>
/// <para>
/// El permiso es <c>factura.emitir</c>, el mismo que la emisión y el mismo que la referencia de
/// e-factura: quien anota lo que el cliente entregó está haciendo la misma gestión sobre un
/// documento ya emitido, y tocar la matriz de permisos por esto sería desproporcionado. El empleado no
/// lo tiene, así que recibe 403.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/comprobantes-retencion")]
public sealed class ComprobantesRetencionController(
    ServicioRetenciones retenciones,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Lista de comprobantes, más nuevos primero. Filtros: <c>facturaId</c> y <c>pendiente</c>
    /// (true = sin recibir). Es lo que permite a la interfaz saber el id del comprobante de una factura.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] Guid? facturaId,
        [FromQuery] bool? pendiente,
        CancellationToken ct) =>
        await EnvoltorioAsync(retenciones.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), facturaId, pendiente, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await GuardarAsync(() => retenciones.LeerAsync(id, ct));

    /// <summary>
    /// Marca el comprobante como recibido con su número y su fecha, y resuelve el aviso que estaba
    /// vivo. Un 409 si ya se había recibido: lo que llegó no se reescribe, y un número distinto es
    /// otro documento, que es lo que se resuelve anulando la factura (F3-08).
    /// </summary>
    [HttpPut("{id:guid}/recibir")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> Recibir(
        Guid id,
        ComprobanteRetencionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/comprobantes-retencion/{id:D}/recibir");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => retenciones.RecibirAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

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
    }
}