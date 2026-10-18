using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Clientes y sus contactos (docs/ESPECIFICACION.md §3.2). Leer es <c>cliente.ver</c> y escribir es
/// <c>cliente.editar</c>: el empleado ve la cartera y puede darla de alta.
/// <para>
/// El documento de identidad se ve completo en la ficha (decisión D16) y está cifrado en el
/// reposo. No va en la lista ni en las respuestas de alta y cambio, porque esas se guardan para el
/// reintento idempotente y ahí el documento se quedaría en claro.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/clientes")]
public sealed class ClientesController(
    ServicioClientes clientes,
    ServicioContactos contactos,
    ServicioCuentas cuentas,
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
        [FromQuery] string? documento,
        [FromQuery] TipoCliente? tipo,
        [FromQuery] bool incluirInactivos,
        CancellationToken ct) =>
        await EnvoltorioAsync(clientes.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), q, documento, tipo, incluirInactivos, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => clientes.LeerAsync(id, ct));

    [HttpPost]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Crear(ClientePeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/clientes";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => clientes.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Actualizar(Guid id, ClientePeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/clientes/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => clientes.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>Baja lógica: un cliente con facturas no se borra nunca.</summary>
    [HttpDelete("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/clientes/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(async () =>
        {
            await clientes.DesactivarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return true;
        });
    }

    // ---------------------------------------------------------------- recordatorios (F4-05)

    /// <summary>
    /// Pausa o levanta los avisos de pago de este cliente (§3.14, D12). Con <c>pausados</c> en
    /// <c>true</c> el trabajo diario deja de avisarle y el envío manual de un aviso pendiente también
    /// se salta: la pausa manda sobre las dos cosas.
    /// </summary>
    [HttpPut("{id:guid}/recordatorios-pausados")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> PausarRecordatorios(
        Guid id,
        RecordatoriosPausadosPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/clientes/{id:D}/recordatorios-pausados");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => clientes.PausarRecordatoriosAsync(
            id, peticion.Pausados, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- estado de cuenta (F4-01)

    /// <summary>
    /// Estado de cuenta del cliente con el diseño de <c>docs/DESIGN.md §7</c> (F4-01, §3.14): las filas
    /// de la vista única y los totales por moneda.
    /// <para>
    /// <b>Usa <c>informe.ver</c> y no <c>cliente.ver</c> ni <c>factura.ver</c>.</b> El documento lleva
    /// totales agregados de dinero de un cliente, y AGENTS.md §6 dice que el empleado no los recibe por
    /// ningún endpoint: con la ficha de un cliente no se puede pedir su deuda total.
    /// </para>
    /// <para>
    /// El <c>anio</c> es opcional y es el mismo filtro que tiene la vista única; sin él sale todo lo que
    /// tenga el cliente. No lleva <c>Idempotency-Key</c> ni <c>Mutar</c>: es lectura.
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}/estado-de-cuenta.pdf")]
    [Accion(Acciones.InformeVer)]
    public async Task<IActionResult> EstadoDeCuenta(
        Guid id,
        [FromQuery] int? anio,
        CancellationToken ct)
    {
        try
        {
            var pdf = await cuentas.PdfAsync(id, anio, UsuarioDeLaPeticion(), ct);
            return File(pdf, "application/pdf", $"estado-de-cuenta-{id:D}.pdf");
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (Exception invalida) when (invalida is InvalidOperationException or ArgumentException)
        {
            // 409: una plantilla que no está en el catálogo, que solo se da con datos escritos por fuera
            // de la API. Sin este papel el cliente no puede saber qué debe.
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.PlantillaDesconocida,
                "No se puede generar el estado de cuenta",
                invalida.Message);
        }
    }

    // ---------------------------------------------------------------- contactos

    [HttpGet("{id:guid}/contactos")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> ListarContactos(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => contactos.ListarAsync(id, ct));

    [HttpPost("{id:guid}/contactos")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> CrearContacto(Guid id, ContactoPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/clientes/{id:D}/contactos");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => contactos.CrearAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}/contactos/{contactoId:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> ActualizarContacto(
        Guid id,
        Guid contactoId,
        ContactoPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/clientes/{id:D}/contactos/{contactoId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => contactos.ActualizarAsync(
            id, contactoId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpDelete("{id:guid}/contactos/{contactoId:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> EliminarContacto(Guid id, Guid contactoId, CancellationToken ct)
    {
        var mutacion = Mutar(new { contactoId }, $"/api/v1/clientes/{id:D}/contactos/{contactoId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => contactos.EliminarAsync(
            id, contactoId, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos. Un 409 cubre tanto el aviso previo de duplicado
    /// como el índice único, que es el que salta si dos altas llegan a la vez.
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
    }
}