using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Envío de documentos por correo y consulta de lo que salió (F4-02,
/// docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>El permiso va en el atributo y también en el servicio.</b> En el atributo porque la
/// peticion HTTP no llega nunca a un usuario sin permiso; en el servicio porque los trabajos de
/// Hangfire de W4 y W5 entran por ahí y no tienen sesión de la que sacarlo.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/envios")]
public sealed class EnviosController(
    ServicioEnvios envios,
    ServicioEnviosMasivos masivo,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Manda un documento a un destinatario. Acepta <c>Idempotency-Key</c>: un reintento con la
    /// misma clave devuelve el envío ya hecho en vez de mandarlo dos veces.
    /// </summary>
    /// <remarks>
    /// El atributo es <c>cotizacion.enviar</c> y el envío sale con <c>documento.enviar</c>, y eso no
    /// es una errata: §2 da a los tres roles uno u otro y hay que elegir el más alto de los dos como
    /// puerta del endpoint, porque un atributo no puede mirar qué tipo de documento trae el cuerpo.
    /// <c>cotizacion.enviar</c> es el más alto (los dos roles que tienen <c>documento.enviar</c> lo
    /// tienen también), así que el atributo no le cierra la puerta a nadie que la matriz le abra y el
    /// permiso exacto lo comprueba el servicio, con el usuario que se le pase.
    /// </remarks>
    [HttpPost]
    [Accion(Acciones.CotizacionEnviar)]
    public async Task<IActionResult> Enviar(EnvioPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/envios";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await envios.EnviarAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (PermisoDenegadoException)
        {
            // El permiso lo comprueba el servicio, y desde ahí la excepción llega hasta aquí: el
            // filtro global solo convierte las suyas, no las de un servicio. Es el mismo camino que
            // siguen ContratosController y el resto de servicios que comprueban permisos por su cuenta.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
        catch (CorreoNoConfiguradoException sinCorreo)
        {
            // 409 y no 503: no es un fallo pasajero del servidor de correo, es que nadie ha escrito
            // la configuración, y eso solo lo arregla alguien de dentro. El detalle dice exactamente
            // qué escribir, con los nombres de las variables.
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.CorreoNoConfigurado,
                "No hay correo configurado",
                sinCorreo.QueFalta);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }

    /// <summary>Envíos, filtrables por tipo de documento, por estado y por documento.</summary>
    /// <remarks>
    /// El atributo es <c>factura.ver</c> como puerta de pelo grueso, y el motivo es que un atributo
    /// <b>solo ve el verbo y la ruta</b>: no puede mirar el tipo de documento ni el contenido del
    /// adjunto. La fila que sale aquí es metadato —a quién, cuándo, en qué estado y con qué nombre de
    /// archivo— y ningún importe, así que <c>factura.ver</c> no deja pasar ningún total agregado. El
    /// dinero va en el archivo, y ese se descarga por otro endpoint.
    /// </remarks>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] TipoDocumentoEnvio? tipo,
        [FromQuery] EstadoEnvio? estado,
        [FromQuery] Guid? documentoId,
        CancellationToken ct) =>
        await EnvoltorioAsync(envios.ListarAsync(
            Paginacion(page, pageSize, sortBy, sortDir), tipo, estado, documentoId, UsuarioDeLaPeticion(), ct));

    /// <summary>
    /// La copia exacta del PDF que salió por el correo, no una regeneración. Si se regenerara, el
    /// archivo cambiaría en cuanto cambiara el documento y dejaría de ser la copia de lo que el
    /// cliente recibió.
    /// </summary>
    /// <remarks>
    /// <b>El atributo <c>factura.ver</c> es la puerta de pelo grueso y no la comprobación fina.</b>
    /// Un atributo no puede mirar el adjunto —la ruta solo lleva el identificador del envío— y el
    /// contenido cambia con cada fila: la copia de una factura es una factura, pero la copia de un
    /// estado de cuenta <b>es</b> el estado de cuenta, con sus filas, sus totales por moneda y su
    /// saldo pendiente. Por eso el permiso exacto lo decide
    /// <see cref="ServicioEnvios.AccionesDeContenido"/> en el servicio, con el usuario de la
    /// petición: un documento individual con la lectura de su recurso, y lo que lleva totales
    /// agregados con <c>informe.ver</c>, que el empleado no tiene por ningún endpoint (AGENTS.md §6).
    /// <para>
    /// Por eso el atributo se queda y no se disfraza de otra cosa: es la puerta que no depende del
    /// contenido, y el servicio es la que sí depende.
    /// </para>
    /// </remarks>
    [HttpGet("{id:guid}/pdf")]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        try
        {
            var (pdf, nombre) = await envios.CopiaAsync(id, UsuarioDeLaPeticion(), ct);

            return File(pdf, "application/pdf", nombre);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (PermisoDenegadoException)
        {
            // La excepción la lanza el servicio y llega hasta aquí sin pasar por el filtro global, que
            // solo convierte las suyas. Es el mismo camino que siguen los envíos y el envío masivo.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para ver este documento.");
        }
    }

    // ---------------------------------------------------------------- envío masivo (F4-03)

    /// <summary>
    /// Lo que se mandaría con el filtro, con su vista previa y con los excluidos y su motivo.
    /// <b>No manda nada</b> (§3.14: pantalla de resumen antes que confirmación).
    /// </summary>
    /// <remarks>
    /// Acepta <c>Idempotency-Key</c>: la respuesta es larga y repetirla no tiene por qué costar
    /// otro recorrido entero de la vista de cuentas.
    /// </remarks>
    [HttpPost("masivo/preparar")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> PrepararMasivo(EnvioMasivoPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/envios/masivo/preparar";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await EjecutarMasivoAsync(
            () => masivo.PrepararAsync(peticion, UsuarioDeLaPeticion(), mutacion, ct), ct);
    }

    /// <summary>
    /// Confirma el lote y lo encola: escribe una fila <c>Pendiente</c> por documento y un trabajo por
    /// fila. <b>Confirmar tampoco manda nada</b>: lo manda el trabajo de la cola, después.
    /// </summary>
    [HttpPost("masivo/confirmar")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> ConfirmarMasivo(EnvioMasivoPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/envios/masivo/confirmar";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await EjecutarMasivoAsync(
            () => masivo.ConfirmarAsync(peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct), ct);
    }

    /// <summary>Cuánto ha salido del lote, cuánto se ha quedado sin salir y qué falló.</summary>
    [HttpGet("masivo/{loteId:guid}")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> ProgresoMasivo(Guid loteId, CancellationToken ct) =>
        await EjecutarMasivoAsync(() => masivo.ProgresoAsync(loteId, UsuarioDeLaPeticion(), ct), ct);

    /// <summary>
    /// Vuelve a encolar <b>solo los fallidos</b> del lote. Un envío que ya salió no se toca ni se
    /// vuelve a mandar (§3.14).
    /// </summary>
    [HttpPost("masivo/{loteId:guid}/reintentar")]
    [Accion(Acciones.MensajeriaMasivo)]
    public async Task<IActionResult> ReintentarMasivo(Guid loteId, CancellationToken ct)
    {
        const string ruta = "/api/v1/envios/masivo/reintentar";
        var mutacion = Mutar(new { loteId }, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return await EjecutarMasivoAsync(
            () => masivo.ReintentarAsync(loteId, UsuarioDeLaPeticion(), Ip(), mutacion, ct), ct);
    }

    /// <summary>
    /// Los cuatro endpoints del envío masivo comparten los mismos tres finales: el permiso que
    /// comprueba el servicio, el lote que no existe y un orden de filtro inválido. El 403 lo
    /// construye el servicio y llega hasta aquí por el mismo camino que el envío suelto, porque el
    /// filtro global solo convierte las suyas.
    /// </summary>
    private static async Task<IActionResult> EjecutarMasivoAsync<T>(Func<Task<T>> accion, CancellationToken ct)
    {
        try
        {
            return new OkObjectResult(await accion());
        }
        catch (KeyNotFoundException noExiste)
        {
            return new NotFoundObjectResult(Problemas.Construir(
                StatusCodes.Status404NotFound,
                Problemas.RecursoNoEncontrado,
                "No encontrado",
                noExiste.Message));
        }
        catch (PermisoDenegadoException)
        {
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Tu usuario no tiene permiso para esta operación.");
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.ValorDuplicado,
                "No se puede guardar el cambio",
                e.Message);
        }
    }
}