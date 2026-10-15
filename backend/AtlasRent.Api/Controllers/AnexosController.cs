using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Catálogo de anexos del contrato (F2-07, docs/ESPECIFICACION.md §3.5: "Anexos: catálogo, uno
/// activo por tipo").
/// <para>
/// Los permisos son los mismos que en las plantillas y por el mismo motivo:
/// <c>cliente.ver</c> para leer (el anexo va con el contrato que se firma), <c>contrato.borrador</c>
/// para que el empleado lo prepare (es un documento legal que va a un cliente) y
/// <c>contrato.estado</c> para desactivarlos, que es una decisión de negocio de gerente.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/plantillas-anexo")]
public sealed class AnexosController(
    ServicioPlantillas plantillas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// El catálogo. Por defecto solo los anexos activos, que son los que se pueden elegir;
    /// <c>todos=true</c> enseña también los desactivados, que no se borran nunca.
    /// </summary>
    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] bool todos,
        CancellationToken ct) =>
        await EnvoltorioAsync(plantillas.ListarAnexosAsync(
            Paginacion(page, pageSize, sortBy, sortDir), todos, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => plantillas.LeerAnexoAsync(id, ct));

    /// <summary>
    /// Alta de anexo. Solo entra si su tipo no tiene ya uno activo: es la regla del catálogo y la
    /// base la vuelve a comprobar con su índice único parcial.
    /// </summary>
    [HttpPost]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Crear(PlantillaAnexoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/plantillas-anexo");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.CrearAnexoAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct),
            creado: true);
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Editar(
        Guid id,
        PlantillaAnexoEdicionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/plantillas-anexo/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.EditarAnexoAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Retira el anexo del catálogo. No borra nada: el siguiente anexo del mismo tipo entra
    /// directamente y los contratos que ya lo tienen congelado no se enteran.
    /// </summary>
    [HttpPost("{id:guid}/desactivacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/plantillas-anexo/{id:D}/desactivacion");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.DesactivarAnexoAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    private async Task<IActionResult> GuardarAsync<TResultado>(
        Func<Task<TResultado>> operacion,
        bool creado = false)
    {
        try
        {
            var resultado = await operacion();
            return creado ? StatusCode(StatusCodes.Status201Created, resultado) : Ok(resultado);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (InvalidOperationException conflicto)
        {
            return Conflicto(conflicto.Message);
        }
        catch (DbUpdateException)
        {
            return Conflicto("No se pudo guardar el cambio.");
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