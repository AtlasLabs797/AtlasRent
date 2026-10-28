using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Los avisos de pago y lo que se hace con ellos (F4-05, docs/ESPECIFICACION.md §3.14, D12).
/// <para>
/// El permiso es <c>mensajeria.masivo</c>, el de gerente y administrador, que es a quien §3.14 le da
/// el modo manual y la pausa por cliente. Un empleado no ve esta lista: los avisos dicen qué debe
/// cada cliente y a cuál no se le avisó, y esa es información de cobranza.
/// </para>
/// <para>
/// El trabajo diario no pasa por aquí. Entra por <c>ServicioRecordatorios.EjecutarAsync</c>, que es lo
/// que el planificador dispara a las 09:00.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/recordatorios")]
public sealed class RecordatoriosController(
    ServicioRecordatorios recordatorios,
    ServicioCatalogos catalogos,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Los avisos, con sus filtros: fecha, cliente, unidad y estado. Es la lista de §3.14 para
    /// revisar qué se mandó y a cuál se le saltó.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] DateOnly? fecha,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] EstadoRecordatorio? estado,
        CancellationToken ct) =>
        await EnvoltorioAsync(recordatorios.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), fecha, clienteId, unidadId, estado, ct));

    /// <summary>Modo vigente de los recordatorios (apagado, manual o automático), para la pantalla de avisos.</summary>
    [HttpGet("modo")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Modo(CancellationToken ct) =>
        Ok(await catalogos.ModoRecordatoriosAsync(ct));

    /// <summary>Un aviso con el envío al que dio lugar.</summary>
    [HttpGet("{id:guid}")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await recordatorios.LeerAsync(id, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>
    /// Confirma el envío de un aviso. Es lo que se pulsa en el modo manual, y es la mitad del criterio
    /// de aceptación: sin este <c>POST</c>, un aviso preparado no sale.
    /// <para>
    /// Acepta <c>Idempotency-Key</c>: un doble clic no manda dos correos.
    /// </para>
    /// <para>
    /// <b>Devuelve 200 con el aviso en <c>Omitido</c></b> si mientras tanto se pagó o si el cliente
    /// tiene la pausa puesta. No es un error: es el resultado que se pidió, que es no molestar a quien
    /// ya pagó.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/enviar")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> Enviar(Guid id, CancellationToken ct)
    {
        const string ruta = "/api/v1/recordatorios/enviar";

        // No hay cuerpo, así que la huella es el identificador: la misma clave con otro aviso es otra
        // mutación y se rechaza, en vez de devolver la respuesta del anterior.
        var mutacion = Mutar(new { id }, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await recordatorios.EnviarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
        catch (CorreoNoConfiguradoException sinCorreo)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.CorreoNoConfigurado,
                "No hay correo configurado",
                sinCorreo.QueFalta);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }
}