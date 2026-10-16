using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Vista única de cuentas y cobros (F3-10, docs/ESPECIFICACION.md §3.13).
/// <para>
/// Un solo endpoint de lectura. Todo lo que se puede hacer sobre una fila —ver, duplicar, modificar,
/// anular, el PDF, el envío— sigue siendo el endpoint de su propio documento (§3.13 «acciones por
/// fila»): una vista que además modificara, con permisos dentro de un listado, dejaría de ser una
/// lectura.
/// </para>
/// <para>
/// <b>El permiso es <c>factura.ver</c></b>, el mismo que el de las facturas y los cobros: es una
/// pantalla de lectura y el empleado la tiene. Lo que el empleado <b>no</b> recibe son los totales,
/// y no es cosa de este endpoint sino del servicio, que los devuelve en <c>null</c> si quien pregunta
/// no tiene <c>informe.ver</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/cuentas")]
public sealed class CuentasController(
    ServicioCuentas cuentas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// La vista única: facturas, proformas y fianzas con sus columnas, sus filtros y los totales por
    /// moneda del pie.
    /// <para>
    /// <b>Filtros aceptados:</b> <c>anio</c>, <c>serieId</c>, <c>moneda</c>, <c>estado</c> (estado de la
    /// factura), <c>estadoCobro</c> (derivado), <c>clienteId</c>, <c>unidadId</c>, <c>vencidas</c>,
    /// <c>modificadas</c>, <c>incluirProformas</c> y <c>incluirFianzas</c>.
    /// </para>
    /// <para>
    /// <b>Filtros que NO se aceptan</b> y por tanto no hay que adivinar qué devuelven: <c>noEnviadas</c>,
    /// <c>envioFallido</c> y <c>modificadasTrasEnvio</c> son de F4-01, que es quien va a saber si un
    /// documento se envió y con qué resultado. Aceptarlos hoy y devolver filas sin filtrar sería peor
    /// que no tenerlos.
    /// </para>
    /// <para>
    /// Un filtro que una fianza no puede expresar —<c>serieId</c>, <c>estado</c>, <c>estadoCobro</c>,
    /// <c>vencidas</c>, <c>modificadas</c>— deja las fianzas fuera, en vez de devolverlas sin tocar.
    /// </para>
    /// </summary>
    [HttpGet]
    [Accion(Acciones.FacturaVer)]
    public async Task<IActionResult> Listar(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? anio,
        [FromQuery] Guid? serieId,
        [FromQuery] Moneda? moneda,
        [FromQuery] EstadoFactura? estado,
        [FromQuery] EstadoCobroFactura? estadoCobro,
        [FromQuery] Guid? clienteId,
        [FromQuery] Guid? unidadId,
        [FromQuery] bool? vencidas,
        [FromQuery] bool? modificadas,
        [FromQuery] bool? incluirProformas,
        [FromQuery] bool? incluirFianzas,
        CancellationToken ct)
    {
        try
        {
            return Ok(await cuentas.ListarAsync(
                Paginacion(page, pageSize, sortBy, sortDir),
                anio,
                serieId,
                moneda,
                estado,
                estadoCobro,
                clienteId,
                unidadId,
                vencidas ?? false,
                modificadas ?? false,
                incluirProformas ?? false,
                incluirFianzas ?? true,
                ct));
        }
        catch (ArgumentException peticionInvalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Petición no válida",
                peticionInvalida.Message);
        }
    }
}