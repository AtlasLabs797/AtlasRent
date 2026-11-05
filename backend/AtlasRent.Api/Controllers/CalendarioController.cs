using System.ComponentModel.DataAnnotations;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Calendario de disponibilidad (docs/ESPECIFICACION.md §3.1, F2-04).
/// <para>
/// Es de solo lectura y por eso su permiso es <c>cliente.ver</c>, el mismo que
/// <c>GET /api/v1/reservas</c> y los contratos: en la matriz de §2 ver clientes, unidades, reservas
/// y contratos es una sola fila, y el calendario es una lectura de la cartera. No hay ninguna acción
/// nueva.
/// </para>
/// <para>
/// Se pide para una unidad o para un inmueble, nunca para los dos. El rango va con los dos extremos
/// incluidos, que es como se pide un calendario, y el servicio lo limita a un año; lo que se pase de
/// eso es un 400 con la explicación.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/calendario")]
public sealed class CalendarioController(
    ServicioCalendario calendario,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(
        [FromQuery] Guid? unidadId,
        [FromQuery] Guid? inmuebleId,
        // Nullable + [Required]: un DateOnly sin más tomaría 01/01/0001 si falta el parámetro (BUG-052).
        [FromQuery, Required] DateOnly? desde,
        [FromQuery, Required] DateOnly? hasta,
        CancellationToken ct)
    {
        try
        {
            return Ok(await calendario.LeerAsync(unidadId, inmuebleId, desde!.Value, hasta!.Value, ct));
        }
        catch (ArgumentException peticionInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                peticionInvalida.Message);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }
}