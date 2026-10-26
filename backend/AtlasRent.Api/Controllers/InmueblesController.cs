using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Inmuebles, sus unidades y los documentos PDF de propiedad (docs/ESPECIFICACION.md §3.1).
/// <para>
/// Ver y mantener inmuebles y unidades es <c>cliente.ver</c> y <c>cliente.editar</c>: el empleado
/// los ve y los da de alta. Los documentos de propiedad son <c>inmueble.documento</c>, que es de
/// gerente y administrador (docs/ESPECIFICACION.md §2).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/inmuebles")]
public sealed class InmueblesController(
    ServicioInmuebles inmuebles,
    ServicioDocumentosInmueble documentos,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    // ---------------------------------------------------------------- inmuebles

    [HttpGet]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? q,
        [FromQuery] TipoInmueble? tipo,
        [FromQuery] bool incluirInactivos,
        CancellationToken ct) =>
        await EnvoltorioAsync(inmuebles.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), q, tipo, incluirInactivos, ct));

    [HttpGet("{id:guid}")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> Leer(Guid id, CancellationToken ct) =>
        await ResponderAsync(() => inmuebles.LeerAsync(id, ct));

    [HttpPost]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Crear(InmueblePeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/inmuebles");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inmuebles.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Actualizar(Guid id, InmueblePeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/inmuebles/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inmuebles.ActualizarAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    /// <summary>Baja lógica: un inmueble con contratos o facturas detrás no se borra nunca.</summary>
    [HttpDelete("{id:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> Desactivar(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/inmuebles/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(async () =>
        {
            await inmuebles.DesactivarAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return true;
        });
    }

    // ---------------------------------------------------------------- unidades

    [HttpGet("{id:guid}/unidades")]
    [Accion(Acciones.ClienteVer)]
    public async Task<IActionResult> ListarUnidades(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] EstadoUnidad? estado,
        CancellationToken ct) =>
        await EnvoltorioAsync(inmuebles.ListarUnidadesAsync(
            id, Paginacion(page, pageSize, sortBy, sortDir), estado, ct));

    [HttpPost("{id:guid}/unidades")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> CrearUnidad(Guid id, UnidadPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/inmuebles/{id:D}/unidades");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inmuebles.CrearUnidadAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("unidades/{unidadId:guid}")]
    [Accion(Acciones.ClienteEditar)]
    public async Task<IActionResult> ActualizarUnidad(
        Guid unidadId,
        UnidadPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/inmuebles/unidades/{unidadId:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await GuardarAsync(
            () => inmuebles.ActualizarUnidadAsync(unidadId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- documentos

    [HttpGet("{id:guid}/documentos")]
    [Accion(Acciones.InmuebleDocumento)]
    public async Task<IActionResult> ListarDocumentos(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] CategoriaDocumento? categoria,
        CancellationToken ct) =>
        await EnvoltorioAsync(documentos.ListarAsync(
            id, Paginacion(page, pageSize, sortBy, sortDir), categoria, ct));

    /// <summary>
    /// Subida de un PDF de propiedad. El CSRF lo comprueba el middleware del proyecto
    /// (<c>X-CSRF-Token</c>), no el antiforgery de MVC, que no se usa aquí.
    /// </summary>
    [HttpPost("{id:guid}/documentos")]
    [Accion(Acciones.InmuebleDocumento)]
    [IgnoreAntiforgeryToken]
    // El límite del servidor lleva un margen sobre el de la aplicación: así el rechazo con el
    // mensaje de la aplicación llega antes que el corte de ASP.NET, que no explica nada. El de
    // ASP.NET queda como red para un cuerpo enorme.
    [RequestFormLimits(MultipartBodyLengthLimit = AlmacenDocumentos.TamanoMaximoBytes + (1024 * 1024))]
    public async Task<IActionResult> SubirDocumento(
        Guid id,
        IFormFile archivo,
        [FromForm] CategoriaDocumento categoria,
        CancellationToken ct)
    {
        try
        {
            var documento = await documentos.SubirAsync(
                id, archivo, categoria, UsuarioDeLaPeticion(), Ip(), ct);
            return Ok(documento);
        }
        catch (DocumentoInvalidoException invalido)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Documento no válido",
                invalido.Message);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>
    /// Descarga del PDF. Va por la API y con permiso: el archivo está fuera del webroot, así que no
    /// se puede pedir por una URL estática aunque se conozca el nombre en disco.
    /// </summary>
    [HttpGet("{id:guid}/documentos/{documentoId:guid}/archivo")]
    [Accion(Acciones.InmuebleDocumento)]
    public async Task<IActionResult> DescargarDocumento(Guid id, Guid documentoId, CancellationToken ct)
    {
        try
        {
            var archivo = await documentos.ArchivoAsync(id, documentoId, UsuarioDeLaPeticion(), Ip(), ct);
            if (!System.IO.File.Exists(archivo.Ruta))
            {
                return NoEncontrado("El archivo del documento no está en la carpeta de almacenamiento.");
            }

            // enableRangeProcessing para que el visor del navegador pueda pedir solo los bytes que
            // necesita de un PDF grande.
            return PhysicalFile(archivo.Ruta, archivo.TipoContenido, archivo.Nombre, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (DocumentoInvalidoException)
        {
            return Problemas.Construir(
                StatusCodes.Status404NotFound,
                Problemas.RecursoNoEncontrado,
                "No encontrado",
                "El archivo del documento no está en la carpeta de almacenamiento.");
        }
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Ejecuta la mutación y traduce sus fallos: 400 para una regla que no se cumple (una tarifa de
    /// una modalidad que la unidad no ofrece), 404 para lo que no existe y 409 para un choque con lo
    /// que ya hay, incluido el índice único bajo concurrencia.
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
        catch (ArgumentException regla)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest, Problemas.Validacion, "Datos no válidos", regla.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
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