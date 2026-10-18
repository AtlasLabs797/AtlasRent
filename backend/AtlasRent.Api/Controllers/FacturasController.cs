using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Facturas y proformas (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Los permisos son los que ya existen, sin crear ninguno: los GET usan <c>factura.ver</c>, la
/// escritura en borrador usa <c>factura.borrador</c> y la emisión usa <c>factura.emitir</c>, que D15
/// deja desactivado al empleado — puede preparar una factura y no puede emitirla. El servicio repite
/// la comprobación porque F3-05 emite desde un trabajo, sin sesión HTTP ni filtro.
/// </para>
/// <para>
/// El 409 es el conflicto de negocio, igual que en <see cref="CotizacionesController"/>: emitir lo
/// que no está en borrador, cambiar lo que ya salió, una factura en una moneda que su serie no admite
/// o una emisión sin tipo de cambio del día.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/facturas")]
public sealed class FacturasController(
    ServicioFacturas facturas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Listado paginado con filtros de estado, serie, cliente, unidad, moneda y rango de fechas de
    /// emisión. Sin totales agregados: los informes son de §3.15 y solo gerente y administrador los
    /// ven (F3-10).
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] Guid? serieId,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] Moneda? moneda,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        CancellationToken ct) =>
        await EnvoltorioAsync(facturas.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir),
            estado,
            serieId,
            clienteId,
            unidadId,
            moneda,
            desde,
            hasta,
            ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => facturas.LeerAsync(id, ct));

    [HttpPost]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Crear(FacturaPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/facturas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.CrearAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> Actualizar(
        Guid id,
        FacturaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.ActualizarAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Alta de una línea. Devuelve la factura entera con los totales recalculados, porque quien
    /// acaba de añadir un descuento necesita ver el total nuevo sin pedirlo aparte.
    /// </summary>
    [HttpPost("{id:guid}/lineas")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> AgregarLinea(
        Guid id,
        FacturaLineaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/lineas");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.AgregarLineaAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}/lineas/{lineaId:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> EditarLinea(
        Guid id,
        Guid lineaId,
        FacturaLineaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/lineas/{lineaId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.EditarLineaAsync(
            id, lineaId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Baja de una línea, solo en borrador. En una factura emitida es un 409: lo que se hace con lo
    /// que ya salió es anularlo (F3-08), no borrar una línea y dejar el documento descuadrado.
    /// </summary>
    [HttpDelete("{id:guid}/lineas/{lineaId:guid}")]
    [Accion(Acciones.FacturaBorrador)]
    public async Task<IActionResult> BorrarLinea(Guid id, Guid lineaId, CancellationToken ct)
    {
        var mutacion = Mutar(new { id, lineaId }, $"/api/v1/facturas/{id:D}/lineas/{lineaId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.BorrarLineaAsync(
            id, lineaId, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Emisión: número de serie, fecha y tipo de cambio congelado, todo en una transacción (D28).
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve la factura ya emitida en vez de
    /// sacar un segundo número.
    /// </summary>
    [HttpPost("{id:guid}/emitir")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> Emitir(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/facturas/{id:D}/emitir");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.EmitirAsync(
            id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Referencia de la e-factura externa (D5, §3.7): un texto que teclea gerente o administrador y
    /// que luego se imprime en el PDF. Usa <c>factura.emitir</c> y no uno nuevo: es lo mismo que
    /// emitir —tocar el documento fiscal de una factura— y tocar la matriz de permisos por un campo
    /// de texto sería desproporcionado.
    /// </summary>
    [HttpPut("{id:guid}/referencia-efactura")]
    [Accion(Acciones.FacturaEmitir)]
    public async Task<IActionResult> ReferenciaEfactura(
        Guid id,
        ReferenciaEfacturaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/referencia-efactura");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.ReferenciaEfacturaAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- PDF (F4-01)

    /// <summary>
    /// PDF de la factura o la proforma con el diseño de <c>docs/DESIGN.md §7</c> (F4-01).
    /// <para>
    /// Usa <c>factura.ver</c> y no <c>informe.ver</c>: esto es una factura individual, y los
    /// totales agregados de §3.15 son otra cosa.
    /// </para>
    /// <para>
    /// <b>Una factura anulada se descarga.</b> El papel marcado «ANULADA» es lo que necesita el
    /// expediente del cliente; lo que no se puede es emitirla. No lleva <c>Idempotency-Key</c> ni
    /// <c>Mutar</c> porque no muta nada.
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        try
        {
            var pdf = await facturas.PdfAsync(id, ct);
            return File(pdf, "application/pdf", $"factura-{id:D}.pdf");
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (Exception invalida) when (invalida is InvalidOperationException or ArgumentException)
        {
            // 409: el nombre que tiene la serie en la base no está en el catálogo. Solo se llega aquí
            // con datos escritos por fuera de la API, porque la configuración lo rechaza al guardar.
            // Omitir el documento entero no es una opción —el cliente ya tiene una factura que se le
            // emitió—, así que se explica qué pasó.
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.PlantillaDesconocida,
                "No se puede generar el documento",
                invalida.Message);
        }
    }

    // ---------------------------------------------------------------- ciclo de vida (F3-08)

    /// <summary>
    /// Modificación de una factura ya emitida (F3-08, §3.8): solo sus líneas. El número, la fecha de
    /// emisión y el tipo de cambio congelado no se tocan, y con comprobante fiscal los importes tampoco.
    /// </summary>
    [HttpPut("{id:guid}/modificar")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> Modificar(
        Guid id,
        FacturaModificacionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/modificar");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.ModificarAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Anulación con motivo (F3-08, §3.8, D28). Conserva la factura, su número y su comprobante: los
    /// marca como anulados y les quita el efecto económico. Un reintento con la misma
    /// <c>Idempotency-Key</c> devuelve la factura ya anulada en vez de anularla otra vez.
    /// </summary>
    [HttpPost("{id:guid}/anular")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> Anular(
        Guid id,
        FacturaAnulacionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/anular");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.AnularAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Qué haría la anulación y a qué cobros tocaría, con sus importes, sin escribir nada. Va por
    /// <c>POST</c> y no por <c>GET</c> aunque no escriba, porque la pantalla de §3.8 la pide antes de
    /// confirmar y así el botón de confirmar y esta llamada hablan el mismo idioma.
    /// </summary>
    [HttpPost("{id:guid}/simular-anulacion")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> SimularAnulacion(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => facturas.SimularAnulacionAsync(
            id, UsuarioDeLaPeticion(), ct));

    /// <summary>
    /// Duplica la factura en borrador, sin número, sin comprobante y sin cobros (F3-08).
    /// </summary>
    [HttpPost("{id:guid}/duplicar")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> Duplicar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/facturas/{id:D}/duplicar");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.DuplicarAsync(
            id, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Convierte una proforma en factura conservando moneda e importes (F3-08, D7). La proforma queda
    /// convertida y deja de contar; la serie destino es la fiscal activa y se puede cambiar.
    /// </summary>
    [HttpPost("{id:guid}/convertir-proforma")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> ConvertirProforma(
        Guid id,
        ProformaConversionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/convertir-proforma");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.ConvertirProformaAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>
    /// Rehace la factura en otra moneda (F3-08): anula la original y deja un borrador en la moneda y con
    /// el importe que se tecleen aquí.
    /// </summary>
    [HttpPost("{id:guid}/rehacer-moneda")]
    [Accion(Acciones.FacturaAnular)]
    public async Task<IActionResult> RehacerMoneda(
        Guid id,
        RehacerMonedaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/facturas/{id:D}/rehacer-moneda");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(() => facturas.RehacerEnMonedaAsync(
            id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos. Un 409 cubre tanto el rechazo previo de una regla
    /// como la restricción de la tabla, que es la que salta si algo escribe por fuera de la API.
    /// <para>
    /// La emisión puede chocar con dos caras distintas de la misma carrera y se capturan las dos: la
    /// violación de unicidad (<c>UniqueViolation</c> dentro de un <see cref="DbUpdateException"/>) y
    /// el conflicto de concurrencia (<see cref="DbUpdateConcurrencyException"/>, que deriva del
    /// anterior pero no lleva el <see cref="PostgresException"/> dentro). Con una sola de las dos, la
    /// otra se escapes como un 500.
    /// </para>
    /// </summary>
    private async Task<IActionResult> GuardarAsync<TResultado>(Func<Task<TResultado>> operacion)
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
        catch (DbUpdateConcurrencyException concurrencia)
        {
            return Conflicto(
                $"El cambio choca con otra modificación ({concurrencia.Message}). Vuelve a leer y reintenta.");
        }
        catch (DbUpdateException duplicado) when (duplicado.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflicto($"Ya existe un documento con esos valores ({duplicado.InnerException.Message}).");
        }
        catch (DbUpdateException fallo)
        {
            return Conflicto(fallo.Message);
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
