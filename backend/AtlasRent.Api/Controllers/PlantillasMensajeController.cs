using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Plantillas de correo por tipo de mensaje e idioma (F4-02, docs/ESPECIFICACION.md §3.14), bajo
/// <c>/api/v1/config</c> porque son configuración y no datos de un cliente.
/// <para>
/// <b>Guardar es <c>plantilla.editar</c>, que es solo administrador</b> (§2). Listar usa
/// <c>configuracion.editar</c>, como todos los demás endpoints de <c>/api/v1/config</c>: son
/// configuración, no datos de un cliente, y el mismo rol los lee y los cambia.
/// </summary>
[ApiController]
[Route("api/v1/config/plantillas-mensaje")]
public sealed class PlantillasMensajeController(
    ServicioPlantillasMensaje plantillas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] TipoMensajeEnvio? tipo,
        CancellationToken ct) =>
        await EnvoltorioAsync(plantillas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), tipo, ct));

    /// <summary>
    /// Guarda la plantilla de un tipo de mensaje y un idioma. Si no existe la crea y si existe la
    /// edita, así que el mismo endpoint sirve para las dos cosas y no hay dos formas de escribir lo
    /// mismo.
    /// </summary>
    [HttpPut]
    [Accion(Acciones.PlantillaEditar)]
    public async Task<IActionResult> Guardar(PlantillaMensajePeticion peticion, CancellationToken ct)
    {
        var ruta = $"/api/v1/config/plantillas-mensaje/{peticion.Tipo}/{peticion.Idioma}";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await plantillas.GuardarAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (PermisoDenegadoException)
        {
            // El servicio vuelve a comprobar el permiso y desde ahí su excepción llega hasta aquí; el
            // filtro global solo convierte las suyas.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }
}