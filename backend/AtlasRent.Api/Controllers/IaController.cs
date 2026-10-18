using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Asistente de consultas de solo lectura (F6-02), con sesión normal de navegador.
/// <para>
/// Comparte servicio y contrato con <c>POST /api/v1/agente/consulta</c>: la diferencia entre las
/// dos rutas es la credencial, no los datos. Un token de agente no ve nada más por aquí que lo que
/// ve su propietario con la sesión, porque el doble techo de F6-01 y la comprobación de alcance del
/// servicio son los mismos en las dos.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/ia")]
public sealed class IaController(
    ServicioConsultaIa consultas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Carga los <c>{ pregunta, alcance }</c> del contrato (<c>frontend/src/ia/api.ts</c>) y devuelve
    /// los <c>{ respuesta, alcance, proveedor, modelo }</c>.
    /// <para>
    /// Los tres fallos que el frontend ya distingue tienen su camino propio: <b>400</b> si el alcance
    /// no existe, <b>403</b> si falta un permiso, <b>429</b> si se pasó un límite y <b>503</b> si el
    /// proveedor no está disponible. Nunca un 500 por una clave que falta.
    /// </para>
    /// </summary>
    [HttpPost("consulta")]
    [Accion(Acciones.IaConsultar)]
    public async Task<IActionResult> Consultar(ConsultaIaPeticion peticion, CancellationToken ct)
    {
        try
        {
            return Ok(await consultas.ConsultarAsync(peticion.Pregunta, peticion.Alcance, Ip(), ct));
        }
        catch (ArgumentException invalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                invalida.Message);
        }
        catch (PermisoDenegadoException)
        {
            // La excepción la lanza el servicio y llega hasta aquí sin pasar por el filtro global,
            // que solo convierte las suyas. La denegación ya está auditada.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para consultar este alcance.");
        }
        catch (LimiteIaExcedidoException pasado)
        {
            return Problemas.Construir(
                StatusCodes.Status429TooManyRequests,
                Problemas.LimiteConsultasIa,
                "Límite de consultas alcanzado",
                pasado.Message);
        }
        catch (ProveedorIaException noDisponible)
        {
            return Problemas.Construir(
                StatusCodes.Status503ServiceUnavailable,
                Problemas.ProveedorIaNoDisponible,
                "Asistente no disponible",
                noDisponible.Message);
        }
    }
}