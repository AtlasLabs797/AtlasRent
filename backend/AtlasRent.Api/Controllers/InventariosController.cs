using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Inventario: el de la unidad, el que se congela en el contrato y la revisión de salida (F2-08,
/// docs/ESPECIFICACION.md §3.1, §3.5 y §3.6).
/// <para>
/// Permisos, sin crear ninguno nuevo:
/// <list type="bullet">
/// <item>
/// <c>cliente.ver</c> para leer las tres cosas. La fila 2 de §2 es literalmente "Ver clientes, unidades,
/// reservas y contratos", y el inventario del contrato y su revisión son parte de lo que el cliente
/// firmó.
/// </item>
/// <item>
/// <c>cliente.editar</c> (fila 3) para el inventario base de la unidad. Es la fila donde §2 agrupa
/// "unidades" y "reservas", y es el mismo permiso con el que el empleado prepara el contrato que va a
/// usar esa unidad.
/// </item>
/// <item>
/// <c>contrato.estado</c> (fila 6, solo gerente y administrador) para <b>firmar la revisión de salida</b>.
/// Ningún otro permiso: la firma de esa revisión es la que desbloquea la liquidación y la que va al
/// cliente, así que la firma el mismo rol que aprueba el contrato y lo liquida.
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>No hay ningún <c>DELETE</c>.</b> Un elemento del inventario base que sobra se desactiva; la copia
/// congelada del contrato no tiene ni endpoint de escritura; y una revisión firmada se vuelve a
/// firmar con 409.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class InventariosController(
    ServicioInventario inventario,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    // ---------------------------------------------------------------- inventario base de la unidad

    [HttpGet("unidades/{unidadId:guid}/inventario-base")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> ListarBase(Guid unidadId, CancellationToken ct) =>
        await ResponderAsync(() => inventario.ListarBaseAsync(unidadId, ct));

    [HttpPost("unidades/{unidadId:guid}/inventario-base")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> CrearBase(
        Guid unidadId,
        InventarioBaseItemPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/unidades/{unidadId:D}/inventario-base");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inventario.CrearBaseAsync(
                unidadId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct),
            creado: true);
    }

    [HttpPut("unidades/{unidadId:guid}/inventario-base/{itemId:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> EditarBase(
        Guid unidadId,
        Guid itemId,
        InventarioBaseItemPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/unidades/{unidadId:D}/inventario-base/{itemId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inventario.EditarBaseAsync(
                unidadId, itemId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Retira un elemento del inventario base. Los contratos ya activos conservan su copia congelada:
    /// esto solo afecta a los que se activen en adelante.
    /// </summary>
    [HttpPost("unidades/{unidadId:guid}/inventario-base/{itemId:guid}/desactivacion")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> DesactivarBase(Guid unidadId, Guid itemId, CancellationToken ct)
    {
        var mutacion = Mutar(new { unidadId, itemId }, $"/api/v1/unidades/{unidadId:D}/inventario-base/{itemId:D}/desactivacion");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inventario.DesactivarBaseAsync(
                unidadId, itemId, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- inventario congelado del contrato

    [HttpGet("contratos/{contratoId:guid}/inventario")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> ListarContrato(Guid contratoId, CancellationToken ct) =>
        await ResponderAsync(() => inventario.ListarContratoAsync(contratoId, ct));

    // ---------------------------------------------------------------- revisión de salida

    [HttpGet("contratos/{contratoId:guid}/revision-inventario")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> LeerRevision(Guid contratoId, CancellationToken ct) =>
        await ResponderAsync(() => inventario.LeerRevisionAsync(contratoId, ct));

    /// <summary>
    /// Firma la revisión de salida. Se firma <b>una sola vez</b>: el segundo intento es 409 con el
    /// motivo, igual que el incremento ya aplicado de F2-06. No borra el inventario congelado del
    /// contrato: lo conserva porque es la contra que se acaba de firmar.
    /// </summary>
    [HttpPost("contratos/{contratoId:guid}/revision-inventario")]
    [Accion(Acciones.ContratoEstado)]
    public async Task<IActionResult> FirmarRevision(
        Guid contratoId,
        RevisionInventarioPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/contratos/{contratoId:D}/revision-inventario");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inventario.FirmarRevisionAsync(
                contratoId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct),
            creado: true);
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
            // El índice único de contrato_id sobre la revisión cae aquí, que es justo para lo que está:
            // que dos firmas simultáneas no dejen dos revisiones del mismo contrato.
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