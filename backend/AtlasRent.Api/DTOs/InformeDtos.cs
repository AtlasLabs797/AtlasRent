using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Los filtros del resumen y del dashboard (F5-01, docs/ESPECIFICACION.md §3.15).
/// <para>
/// Son los mismos que acepta la vista única de §3.13 más el rango de fechas, que no existía allí, y
/// se aplican a la vista única por la misma vía: el informe no tiene una consulta de documentos propia.
/// </para>
/// </summary>
/// <param name="Desde">Primer día de emisión, inclusive.</param>
/// <param name="Hasta">Último día de emisión, inclusive. Es también la fecha de corte de lo vencido.</param>
/// <param name="ContratoId">
/// No es filtro de la vista única, así que se aplica sobre las filas —que sí traen la columna—.
/// </param>
public sealed record FiltroInformes(
    DateOnly? Desde,
    DateOnly? Hasta,
    Guid? SerieId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? Moneda,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura? Estado,
    Guid? ClienteId,
    Guid? UnidadId,
    Guid? ContratoId,
    Guid? InmuebleId,
    Guid? PropietariaId,
    Guid? FacturadoraId);

/// <summary>
/// El formato de la exportación de §3.15 (F5-02). Va como <c>formato=xlsx</c> o <c>formato=csv</c>:
/// un valor que no sea ninguno de los dos es una petición no válida y entra por el 400 de validación
/// del modelo, no por una excepción.
/// </summary>
public enum FormatoInforme
{
    /// <summary>Libro de Excel (una hoja de detalle y otra de resumen).</summary>
    Xlsx = 1,

    /// <summary>CSV en UTF-8 con BOM y separador <c>;</c>, que es lo que espera Excel en español.</summary>
    Csv = 2,
}

/// <summary>
/// El fichero de la exportación, ya generado en memoria: bytes, tipo MIME y nombre para la cabecera
/// <c>Content-Disposition</c>.
/// </summary>
/// <param name="NombreArchivo">Sin espacios ni tildes y con la fecha de generación.</param>
public sealed record ArchivoExportado(byte[] Contenido, string TipoContenido, string NombreArchivo);

/// <summary>
/// Un bloque de dinero del informe, con las dos monedas por separado y el consolidado en dólares
/// (F5-01, §3.15 y D9).
/// <para>
/// <b>El consolidado usa el tipo de cambio congelado de cada documento</b> —<c>factura.tipo_cambio_valor</c>
/// y <c>cobro.tipo_cambio_valor</c>—, nunca el del día del informe: un informe que se recalcula con el
/// cambio de hoy dice que el mes pasado se facturó otra cantidad.
/// </para>
/// <para>
/// Un documento <b>nunca se convierte</b> (D7): esto solo suma para el informe. Por eso los importes van
/// enteros y el consolidado se redondea al final, una vez, y no documento a documento.
/// </para>
/// </summary>
public sealed record ImporteInforme(decimal Usd, decimal Dop, decimal ConsolidadoUsd);

/// <summary>
/// El bloque «Cobrado» de §3.15, que pregunta por el dinero cobrado <b>según a dónde fue</b>: a una
/// factura, a una fianza o quedó como saldo a favor del contrato.
/// </summary>
/// <param name="SaldoAFavor">
/// El crédito que los contratos del informe tienen sin aplicar. <b>Es una foto de hoy, no un
/// movimiento del periodo</b>: la fila es un saldo por contrato y moneda y no tiene fecha de negocio
/// (§3.10.1), así que se enseña siempre pero, con rango de fechas, no suma en el total —mira
/// <see cref="InformeResumenRespuesta.SaldoAFavorFueraDePeriodo"/>.
/// </param>
/// <param name="Total">
/// Los tres sumados <b>salvo que el saldo a favor quede fuera del periodo</b>, que es lo que dice
/// <see cref="InformeResumenRespuesta.SaldoAFavorFueraDePeriodo"/>. Sin esa salvedad,
/// <c>facturas + fianzas</c> es exactamente lo que la vista única enseña en su pie de totales; el saldo
/// a favor no sale ahí porque no es una fila de documento.
/// </param>
public sealed record BloqueCobradoInforme(
    ImporteInforme Facturas,
    ImporteInforme Fianzas,
    ImporteInforme SaldoAFavor,
    ImporteInforme Total);

/// <summary>
/// El bloque «Retenido» de §3.15, con el desglose que lo hace útil: lo retenido cuyo comprobante ya
/// está recibido y lo retenido que todavía no lo está (F3-04, §3.9).
/// </summary>
public sealed record BloqueRetenidoInforme(
    ImporteInforme ConComprobante,
    ImporteInforme SinComprobante,
    ImporteInforme Total);

/// <summary>
/// El bloque «Pendiente» de §3.15. Sale partido por facturas y fianzas porque la vista única cuenta
/// las dos como dinero y el cuadre del informe con el pie de la tabla es por el total.
/// </summary>
public sealed record BloquePendienteInforme(
    ImporteInforme Facturas,
    ImporteInforme Fianzas,
    ImporteInforme Total);

/// <summary>
/// Un mes del gráfico del dashboard. La fecha va en <c>AAAA-MM</c> y el importe ya está consolidado en
/// dólares.
/// </summary>
public sealed record SerieMensualInforme(string Mes, decimal FacturadoUsd, decimal CobradoUsd);

/// <summary>
/// El resumen económico de §3.15: los ocho bloques, la serie mensual y los filtros con los que salió.
/// </summary>
/// <param name="FechaCorte">
/// El día contra el que se mide lo vencido: <c>hasta</c> si se dio, y si no el día de hoy de la
/// aplicación. Es un dato del informe y no un filtro, así que se devuelve para que quien lo lee sepa
/// contra qué fecha se calculó lo vencido.
/// </param>
/// <param name="DocumentosSinTipoCambioCongelado">
/// Documentos en pesos que se han quedado <b>fuera del consolidado en dólares</b> porque no tienen
/// cambio congelado. Se cuentan en lugar de convertirlos con el cambio de hoy o de omitirlos en
/// silencio: §3.12 exige el congelado y un consolidado al que le faltan documentos tiene que decirlo.
/// </param>
/// <param name="SaldoAFavorFueraDePeriodo">
/// <c>true</c> cuando el informe lleva rango de fechas y había saldo a favor: ese crédito es un saldo
/// corriente sin fecha de negocio, así que <b>no se suma al <c>cobrado.total</c></b> y el total sí es la
/// suma de sus dos líneas. Es <c>false</c> sin rango de fechas —el informe es del histórico entero y ahí
/// el saldo sí es la foto del momento— y también cuando no había saldo a favor que dejar fuera.
/// </param>
public sealed record InformeResumenRespuesta(
    DateOnly? Desde,
    DateOnly? Hasta,
    DateOnly FechaCorte,
    ImporteInforme Proformado,
    ImporteInforme Facturado,
    BloqueCobradoInforme Cobrado,
    BloqueRetenidoInforme Retenido,
    BloquePendienteInforme Pendiente,
    ImporteInforme Vencido,
    ImporteInforme FianzasRetenidas,
    ImporteInforme DiferenciasCambio,
    IReadOnlyList<SerieMensualInforme> SerieMensual,
    int DocumentosSinTipoCambioCongelado,
    bool SaldoAFavorFueraDePeriodo);

/// <summary>
/// Las unidades en el día de corte (§3.15). Los cuatro grupos son excluyentes: una unidad está en uno.
/// <para>
/// <b>«Ocupada» y «reservada» no son campos</b> (§3.1): salen de los bloqueos por fechas de
/// <c>ocupacion_rango</c> que están vigentes ese día. «En mantenimiento» sí es un dato, y también
/// cuenta un bloqueo de origen <c>Mantenimiento</c>.
/// </para>
/// </summary>
public sealed record IndicadoresUnidadesInforme(
    int Disponibles,
    int Reservadas,
    int Ocupadas,
    int Mantenimiento);

/// <summary>
/// El dashboard del gerente y el administrador (F5-01, §3.15): los indicadores de ocupación del día y
/// los mismos bloques económicos del resumen, con los mismos filtros.
/// </summary>
public sealed record DashboardRespuesta(
    DateOnly Fecha,
    IndicadoresUnidadesInforme Unidades,
    int ContratosActivos,
    InformeResumenRespuesta Resumen);