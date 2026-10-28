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
    /// <c>modificadas</c>, <c>noEnviadas</c>, <c>envioFallido</c>, <c>modificadasTrasEnvio</c>,
    /// <c>incluirProformas</c>, <c>incluirFianzas</c>, <c>desde</c>, <c>hasta</c>, <c>inmuebleId</c>,
    /// <c>propietariaId</c> y <c>facturadoraId</c>.
    /// </para>
    /// <para>
    /// <b>F5-01 añadió los cinco últimos</b> (§3.15), que son los que necesitan los informes: sin rango
    /// de emisión ni por quién se factura, un informe solo se puede pedir entero. Los tres de unidad
    /// leen los de <c>unidad</c>, y la propietaria la de la unidad o, si no tiene, la que hereda del
    /// inmueble (§3.1). Todos son opcionales: sin ellos la vista devuelve exactamente lo que
    /// devolvía antes.
    /// </para>
    /// <para>
    /// <b>F4-02 añadió los tres de envío</b> (<c>noEnviadas</c>, <c>envioFallido</c> y
    /// <c>modificadasTrasEnvio</c>). Hasta que existió la tabla <c>envio</c> (§3.14) no se aceptaban,
    /// y estaba escrito por qué: aceptarlos y devolver filas sin filtrar es peor que no tenerlos.
    /// Ahora filtran, y cada fila trae su <c>estadoEnvio</c>.
    /// </para>
    /// <para>
    /// Un filtro que una fianza no puede expresar —<c>serieId</c>, <c>estado</c>, <c>estadoCobro</c>,
    /// <c>vencidas</c>, <c>modificadas</c>, los tres de envío y los cinco de F5-01— deja las fianzas
    /// fuera, en vez de devolverlas sin tocar.
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
        [FromQuery] bool? noEnviadas,
        [FromQuery] bool? envioFallido,
        [FromQuery] bool? modificadasTrasEnvio,
        [FromQuery] bool? incluirProformas,
        [FromQuery] bool? incluirFianzas,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] Guid? inmuebleId,
        [FromQuery] Guid? propietariaId,
        [FromQuery] Guid? facturadoraId,
        [FromQuery] Guid? contratoId,
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
                noEnviadas ?? false,
                envioFallido ?? false,
                modificadasTrasEnvio ?? false,
                incluirProformas ?? false,
                incluirFianzas ?? true,
                ct,
                desde,
                hasta,
                inmuebleId,
                propietariaId,
                facturadoraId,
                contratoId));
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