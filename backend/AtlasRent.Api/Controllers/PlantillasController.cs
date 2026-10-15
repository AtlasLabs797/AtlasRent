using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Plantillas de contrato (F2-07, docs/ESPECIFICACION.md §3.5).
/// <para>
/// Permisos, sin crear ninguno nuevo:
/// <list type="bullet">
/// <item>
/// <c>cliente.ver</c> para leer. La fila 2 de §2 es literalmente "Ver clientes, unidades, reservas y
/// contratos", y una plantilla va con el contrato que se firma: quien puede ver contratos puede ver
/// el texto que se les va a entregar.
/// </item>
/// <item>
/// <c>contrato.borrador</c> ("crear y editar contratos en borrador", fila 5) para crear y editar. Es
/// el mismo permiso con el que el empleado prepara el borrador del contrato: una plantilla es un
/// documento legal que va a un cliente, y quien lo prepara es el mismo.
/// </item>
/// <item>
/// <c>contrato.estado</c> (fila 6, solo gerente y administrador) para <b>desactivar</b>. Retirar
/// una plantilla del catálogo es la misma decisión de negocio que aprobar o dar de baja, no una
/// edición de texto: decide qué pueden firmar los contratos que se aprueben a partir de ahora.
/// </item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/plantillas-contrato")]
public sealed class PlantillasController(
    ServicioPlantillas plantillas,
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
        CancellationToken ct) =>
        await EnvoltorioAsync(plantillas.ListarContratosAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => plantillas.LeerContratoAsync(id, ct));

    /// <summary>
    /// Alta de plantilla. El texto es contenido de usuario y se guarda tal cual: no hay campo que lo
    /// interprete (docs/DESIGN.md §7), y lo devuelve la API como secciones, no como algo ejecutable.
    /// </summary>
    [HttpPost]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Crear(PlantillaContratoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/plantillas-contrato");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.CrearContratoAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct),
            creado: true);
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.ContratoBorrador)]
    public async Task<IActionResult> Editar(
        Guid id,
        PlantillaContratoEdicionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/plantillas-contrato/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.EditarContratoAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Retira la plantilla del catálogo. Los contratos ya aprobados conservan su texto congelado y
    /// los nuevos no podrán usar esta plantilla.
    /// </summary>
    [HttpPost("{id:guid}/desactivacion")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/plantillas-contrato/{id:D}/desactivacion");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => plantillas.DesactivarContratoAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
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
            // El índice único de codigo (plantillas) y el de tipo activo (anexos) caen aquí, que es
            // justo para lo que están: que dos escrituras simultáneas no dejen dos igualados.
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