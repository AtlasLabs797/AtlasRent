using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Prospectos (docs/ESPECIFICACION.md §3.2). Los ve el empleado con <c>cliente.ver</c> y los
/// puede crear y cambiar con <c>cliente.editar</c>.
/// </summary>
[ApiController]
[Route("api/v1/prospectos")]
public sealed class ProspectosController(
    ServicioProspectos prospectos,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? q,
        CancellationToken ct) =>
        await EnvoltorioAsync(prospectos.ListarAsync(Paginacion(page, pageSize, sortBy, sortDir), q, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await prospectos.LeerAsync(id, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    [HttpPost]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Crear(ProspectoPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/prospectos";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => prospectos.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Actualizar(Guid id, ProspectoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/prospectos/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => prospectos.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>Baja lógica: el prospecto puede tener cotizaciones detrás.</summary>
    [HttpDelete("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/prospectos/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(async () =>
        {
            await prospectos.DesactivarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return true;
        });
    }

    private async Task<IActionResult> GuardarAsync<TResultado>(Func<Task<TResultado>> operacion)
    {
        try
        {
            return Ok(await operacion());
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or DbUpdateException)
        {
            return e is KeyNotFoundException ? NoEncontrado(e.Message) : Conflicto(e.Message);
        }
    }
}