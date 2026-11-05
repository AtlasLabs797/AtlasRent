using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Pantalla de configuración (docs/ESPECIFICACION.md §3.7, §3.9 y §3.10). Toda ella es del
/// administrador: el filtro global ya lo ha comprobado antes de llegar aquí.
/// <para>
/// Las listas usan el envoltorio de <c>docs/DOC_TECNICA.md §6</c> y las mutaciones sensibles
/// aceptan <c>Idempotency-Key</c>. Todas las mutaciones escriben auditoría en su transacción.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/config")]
public sealed class ConfigController(
    ServicioConfiguracion configuracion,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    // ---------------------------------------------------------------- series

    [HttpGet("series")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarSeries(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarSeriesAsync(Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("series")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearSerie(SeriePeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/series";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.CrearSerieAsync(
                peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (ArgumentException invalida)
        {
            // F4-01: nombre de plantilla que no existe. Es un 400 y no un 409 porque es un dato
            // equivocado de la petición, y el mensaje lleva la lista de las válidas.
            return PeticionInvalida(invalida.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }

    [HttpPut("series/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarSerie(
        Guid id,
        SeriePeticion peticion,
        CancellationToken ct)
    {
        var ruta = $"/api/v1/config/series/{id:D}";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarSerieAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (ArgumentException invalida)
        {
            // F4-01: nombre de plantilla que no existe (ver CrearSerie).
            return PeticionInvalida(invalida.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateConcurrencyException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }

    /// <summary>Da de baja la serie. No se borra: sus números emitidos se conservan.</summary>
    [HttpDelete("series/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarSerie(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/series/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarSerieAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ------------------------------------------------------- numeradores de comprobante

    /// <summary>
    /// Numeradores de comprobante fiscal (F3-03, §3.7). Son de configuración y no un recurso de
    /// negocio: los escribe el administrador, con el rango que le autoriza el organismo de los
    /// comprobantes, y no hay ninguno sembrado.
    /// </summary>
    [HttpGet("numeradores-fiscales")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarNumeradores(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarNumeradoresAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpGet("numeradores-fiscales/{serieId:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> LeerNumerador(Guid serieId, CancellationToken ct)
    {
        try
        {
            return Ok(await configuracion.LeerNumeradorAsync(serieId, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>
    /// Guarda el numerador de la serie: lo crea si no existe y lo cambia si ya está. Un solo
    /// endpoint para las dos cosas porque son la misma decisión —qué rango autorizado tiene esta serie—
    /// y separarlos obligaría a la pantalla a preguntar antes de escribir.
    /// </summary>
    [HttpPut("numeradores-fiscales/{serieId:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> GuardarNumerador(
        Guid serieId,
        NumeradorFiscalPeticion peticion,
        CancellationToken ct)
    {
        var ruta = $"/api/v1/config/numeradores-fiscales/{serieId:D}";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.GuardarNumeradorAsync(
                serieId, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateConcurrencyException or DbUpdateException)
        {
            return Conflicto(e.Message);
        }
    }

    // ---------------------------------------------------------------- ITBIS

    [HttpGet("tipos-itbis")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarTiposItbis(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarTiposItbisAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("tipos-itbis")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearTipoItbis(TipoItbisPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/tipos-itbis";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.CrearTipoItbisAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("tipos-itbis/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarTipoItbis(
        Guid id,
        TipoItbisPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/config/tipos-itbis/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarTipoItbisAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    [HttpDelete("tipos-itbis/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarTipoItbis(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/tipos-itbis/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarTipoItbisAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ------------------------------------------------------------- retenciones

    [HttpGet("tipos-retencion")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarTiposRetencion(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarTiposRetencionAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("tipos-retencion")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearTipoRetencion(TipoRetencionPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/tipos-retencion";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.CrearTipoRetencionAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("tipos-retencion/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarTipoRetencion(
        Guid id,
        TipoRetencionPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/config/tipos-retencion/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarTipoRetencionAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
        catch (DbUpdateException duplicado)
        {
            return Conflicto(duplicado.InnerException?.Message ?? duplicado.Message);
        }
    }

    [HttpDelete("tipos-retencion/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarTipoRetencion(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/tipos-retencion/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarTipoRetencionAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ------------------------------------------------------- formas de pago

    [HttpGet("formas-pago")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarFormasPago(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarFormasPagoAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("formas-pago")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearFormaPago(FormaPagoPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/formas-pago";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.CrearFormaPagoAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("formas-pago/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarFormaPago(
        Guid id,
        FormaPagoPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/config/formas-pago/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarFormaPagoAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    [HttpDelete("formas-pago/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarFormaPago(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/formas-pago/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarFormaPagoAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // --------------------------------------------------------------- cuentas

    [HttpGet("cuentas")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarCuentas(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarCuentasAsync(Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("cuentas")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearCuenta(CuentaReceptoraPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/cuentas";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.CrearCuentaAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("cuentas/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarCuenta(
        Guid id,
        CuentaReceptoraPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/config/cuentas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarCuentaAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    [HttpDelete("cuentas/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarCuenta(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/cuentas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarCuentaAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ---------------------------------------------------------- subempresas

    [HttpGet("subempresas")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ListarSubempresas(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct) =>
        await EnvoltorioAsync(configuracion.ListarSubempresasAsync(
            Paginacion(page, pageSize, sortBy, sortDir), ct));

    [HttpPost("subempresas")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CrearSubempresa(SubempresaPeticion peticion, CancellationToken ct)
    {
        const string ruta = "/api/v1/config/subempresas";
        var mutacion = Mutar(peticion, ruta);
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.CrearSubempresaAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    [HttpPut("subempresas/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarSubempresa(
        Guid id,
        SubempresaPeticion peticion,
        CancellationToken ct)
    {
        var mutacion = Mutar(peticion, $"/api/v1/config/subempresas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            return Ok(await configuracion.ActualizarSubempresaAsync(
                id, peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    [HttpDelete("subempresas/{id:guid}")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> DesactivarSubempresa(Guid id, CancellationToken ct)
    {
        var mutacion = Mutar(new { id }, $"/api/v1/config/subempresas/{id:D}");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        try
        {
            await configuracion.DesactivarSubempresaAsync(id, UsuarioDeLaPeticion(), Ip(), mutacion, ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    // ------------------------------------------ tolerancias y modo de recordatorios

    [HttpGet("ajustes")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> LeerAjustes(CancellationToken ct) =>
        Ok(await configuracion.LeerAjustesAsync(ct));

    [HttpPut("ajustes")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> ActualizarAjustes(AjusteGeneralPeticion peticion, CancellationToken ct)
    {
        var mutacion = Mutar(peticion, "/api/v1/config/ajustes");
        var repetida = await RepetidaAsync(mutacion, ct);
        if (repetida is not null)
        {
            return repetida;
        }

        return Ok(await configuracion.ActualizarAjustesAsync(
            peticion, UsuarioDeLaPeticion(), Ip(), mutacion, ct));
    }

    // ---------------------------------------------------------------- internos

    private static ConsultaLista Paginacion(int? page, int? pageSize, string? sortBy, string? sortDir) =>
        ConsultaLista.Crear(page, pageSize, sortBy, sortDir);

    /// <summary>La cabecera <c>Idempotency-Key</c> de esta petición, si viene.</summary>
    private string? ClaveDeIdempotencia() =>
        Request.Headers.TryGetValue("Idempotency-Key", out var valor) ? valor.ToString() : null;

    private MutacionIdempotente? Mutar(object cuerpo, string ruta) =>
        MutacionIdempotente.Crear(ClaveDeIdempotencia(), ruta, cuerpo);

    /// <summary>
    /// Respuesta ya guardada para esta clave, o <c>null</c> si es la primera vez. Va antes de
    /// tocar nada: un reintento no vuelve a ejecutar la mutación.
    /// </summary>
    private async Task<IActionResult?> RepetidaAsync(MutacionIdempotente? mutacion, CancellationToken ct)
    {
        try
        {
            var guardada = await idempotencia.ReplayAsync(UsuarioDeLaPeticion(), mutacion, ct);
            return guardada is null ? null : ContenidoJson(guardada);
        }
        catch (IdempotenciaEnConflictoException conflicto)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.IdempotenciaEnConflicto,
                "Clave de idempotencia en conflicto",
                conflicto.Message);
        }
        catch (ArgumentException claveInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Cabecera Idempotency-Key no válida",
                claveInvalida.Message);
        }
    }

    private static ContentResult ContenidoJson(string cuerpo) => new()
    {
        Content = cuerpo,
        ContentType = "application/json",
        StatusCode = StatusCodes.Status200OK,
    };

    private async Task<IActionResult> EnvoltorioAsync<T>(Task<Pagina<T>> pagina)
    {
        try
        {
            var resultado = await pagina;
            return Ok(new
            {
                data = resultado.Data,
                resultado.Total,
                resultado.Page,
                resultado.PageSize,
                resultado.TotalPages,
            });
        }
        catch (ArgumentException ordenInvalido)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Orden no válido",
                ordenInvalido.Message);
        }
    }

    private Guid UsuarioDeLaPeticion() => contextoSesion.UsuarioId!.Value;

    private string Ip() => resolutorIp.Resolver(HttpContext);

    private IActionResult NoEncontrado(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status404NotFound,
            Problemas.RecursoNoEncontrado,
            "Configuración no encontrada",
            detalle);

    private IActionResult Conflicto(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status409Conflict,
            Problemas.ValorDuplicado,
            "No se puede guardar el cambio",
            detalle);

    /// <summary>
    /// Petición con un dato que no existe. Es 400 y no 409 porque el arreglo es corregir lo que se
    /// envió, no esperar a que alguien lo cambie en la base (F4-01).
    /// </summary>
    private IActionResult PeticionInvalida(string detalle) =>
        Problemas.Construir(
            StatusCodes.Status400BadRequest,
            Problemas.Validacion,
            "Datos no válidos",
            detalle);
}