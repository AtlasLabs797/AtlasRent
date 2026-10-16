using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Facturación periódica de un mes natural (F3-05, docs/ESPECIFICACION.md §3.8 «Periódica»).
/// <para>
/// Dos endpoints, y la separación es el requisito: uno <b>simula</b> y no escribe nada, el otro
/// <b>ejecuta</b>. El que ejecuta no es una segunda implementación del que simula: es el mismo
/// servicio con la última línea puesta, así que lo enseñado y lo emitido no pueden separarse.
/// </para>
/// <para>
/// Los dos exigen <c>factura.emitir</c>, y no <c>factura.ver</c> como la simulación de un informe:
/// la simulación enseña exactamente lo que va a salir —importes, vencimientos, serie y
/// comprobante— de una factura que va a existir. Verla sin poder emitir es dejar el contenido
/// fiscal de la operación al alcance de quien no puede ejecutarla. Además, el servicio vuelve a
/// comprobarlo por su cuenta, como hace F3-02, porque el permiso no es solo del filtro HTTP.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio, igual que en <see cref="FacturasController"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/facturas/periodicas")]
public sealed class FacturasPeriodicasController(
    ServicioFacturacionPeriodica periodicas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Simulación de un mes: qué facturas saldrían, con qué importe, con qué prorrateo, qué día
    /// vencerían, qué serie y comprobante tocarían, y cuáles ya existen o no se pueden facturar.
    /// <para>
    /// <b>No escribe nada</b>, y no es una promesa: no tiene acceso a ninguna forma de guardar. Ni
    /// factura, ni número de serie, ni tipo de cambio, ni alerta.
    /// </para>
    /// </summary>
    [HttpPost("simular")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> Simular(SimulacionPeriodicaPeticion peticion, CancellationToken ct) =>
        await ResponderAsync(() => periodicas.SimularAsync(peticion, ct));

    /// <summary>
    /// Ejecución: emite las facturas de renta del mes. Un reintento con la misma
    /// <c>Idempotency-Key</c> devuelve lo que ya se emitió en vez de emitir otra vez.
    /// <para>
    /// Lo que se salta se devuelve con su motivo, no falla: una segunda ejecución del mismo mes es
    /// una operación normal y su respuesta tiene que decir «estas ya estaban».
    /// </para>
    /// </summary>
    [HttpPost("generar")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> Generar(GeneracionPeriodicaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/facturas/periodicas/generar");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => periodicas.GenerarAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>Traduce los fallos del servicio a los códigos de §6 de DOC_TECNICA.</summary>
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
            // La red de «doble ejecución no duplica» (§3.8): si dos ejecuciones se solapan y la
            // segunda llega antes que la comprobación previa, la base es la que avisa.
            return Conflicto(
                $"Alguien facturó este periodo mientras corría la ejecución ({duplicado.InnerException.Message}). "
                    + "Vuelve a simular para ver lo que falta.");
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