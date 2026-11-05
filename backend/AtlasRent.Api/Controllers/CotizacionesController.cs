using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Cotizaciones (docs/ESPECIFICACION.md §3.3, D18): borrador → enviada → aceptada o rechazada.
/// <para>
/// Los permisos son los que ya existen, sin crear ninguno. Los GET usan <c>factura.ver</c> y las
/// escrituras usan <c>factura.borrador</c> (su comentario ya dice "facturas, proformas y
/// cotizaciones en borrador"), y <c>cotizacion.enviar</c> para todo cambio de estado, que D15 deja
/// desactivado al empleado.
/// </para>
/// <para>
/// <b>Los GET llevaban <c>factura.borrador</c> y se han cambiado a <c>factura.ver</c>.</b> Leer no es
/// escribir: con un permiso de escritura en un GET, un usuario al que se le recorta la capacidad de
/// redactar deja de poder ver sus propias cotizaciones, sin que conste en ninguna parte. La fila 7
/// de §2 es "Ver facturas individuales", y una cotización es el borrador de una factura, así que el
/// identificador que corresponde es ese. Los dos permisos están hoy en el techo de los tres roles,
/// así que el cambio no le quita nada a nadie: es lo que evita que se lo quiten sin querer.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio (cambiar de estado por un camino que no existe, editar lo que ya
/// salió, un documento que no se puede guardar), igual que en <see cref="ClientesController"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/cotizaciones")]
public sealed class CotizacionesController(
    ServicioCotizaciones cotizaciones,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? q,
        [FromQuery] EstadoCotizacion? estado,
        [FromQuery] bool incluirInactivas,
        CancellationToken ct) =>
        await EnvoltorioAsync(cotizaciones.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), q, estado, incluirInactivas, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => cotizaciones.LeerAsync(id, ct));

    [HttpPost]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Crear(CotizacionPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/cotizaciones";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => cotizaciones.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Actualizar(Guid id, CotizacionPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/cotizaciones/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => cotizaciones.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>Baja lógica y solo en borrador: una cotización enviada no se retira.</summary>
    [HttpDelete("{id:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/cotizaciones/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(async () =>
        {
            await cotizaciones.DesactivarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return true;
        });
    }

    /// <summary>
    /// Único camino de cambio de estado (D18). Solo <c>cotizacion.enviar</c> lo permite, también para
    /// aceptar y para rechazar: es la decisión del orquestador que está explicada en el servicio.
    /// </summary>
    [HttpPost("{id:guid}/estado")]
    [Accion(Acciones.CotizacionEnviar)]
    public async Task<IActionResult> CambiarEstado(Guid id, CotizacionEstadoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/cotizaciones/{id:D}/estado");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => cotizaciones.CambiarEstadoAsync(
            id, peticion.Estado, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Datos para el formulario de reserva. No crea la reserva: la devuelve F2-03 a partir de esto.
    /// </summary>
    [HttpGet("{id:guid}/para-reserva")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> ParaReserva(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => cotizaciones.ParaReservaAsync(id, ct));

    /// <summary>Datos para el formulario de contrato. La crea F2-05 a partir de esto.</summary>
    [HttpGet("{id:guid}/para-contrato")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> ParaContrato(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => cotizaciones.ParaContratoAsync(id, ct));

    /// <summary>PDF de la cotización con el diseño de <c>docs/DESIGN.md §7</c>.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        try
        {
            var pdf = await cotizaciones.PdfAsync(id, ct);
            return File(pdf, "application/pdf", $"cotizacion-{id:D}.pdf");
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos. Un 409 cubre tanto el rechazo previo de una regla
    /// como la restricción de la tabla, que es la que salta si algo escribe por fuera de la API.
    /// </summary>
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