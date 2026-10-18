using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Qué clase de fila es la de la vista de cuentas (F3-10, docs/ESPECIFICACION.md §3.13).
/// <para>
/// La insignia de tipo va en la fila y no se deduce del contenido: una fianza no tiene serie ni
/// número, pero tampoco los tiene una factura que todavía no se ha emitido, y quien lee la tabla no
/// debería tener que saberlo para saber qué está mirando.
/// </para>
/// </summary>
public enum TipoFilaCuenta
{
    /// <summary>Factura emitida. Es la fila normal de la vista.</summary>
    Factura = 1,

    /// <summary>
    /// Proforma: presupuesto que no genera deuda (§3.8). Sale solo con <c>incluirProformas=true</c>.
    /// </summary>
    Proforma = 2,

    /// <summary>Fianza de un contrato (§3.11), que en esta vista es una fila de dinero.</summary>
    Fianza = 3,
}

/// <summary>
/// Una fila de la vista única de cuentas y cobros (F3-10, docs/ESPECIFICACION.md §3.13). Las
/// columnas son las que pide §3.13, tal cual, más lo que hace falta para pintar la insignia de tipo y
/// para que el panel de cobro sepa qué factura se ha marcado.
/// <para>
/// <b>Lo que no aplica viene en <c>null</c> y no en cero.</b> Cero es un dato —«esta factura está
/// pagada»— y un cero donde no hay dato se lee como una deuda que no existe o como una retención que
/// no se retuvo. Por eso <see cref="Cobrado"/>, <see cref="Pendiente"/> y
/// <see cref="Retencion"/> son anulables, y por eso una proforma no trae ni vencido ni días de mora.
/// </para>
/// <para>
/// <b>F4-02: <see cref="EstadoEnvio"/> ya sale real</b> — es el estado del último envío del documento
/// (F4-02, §3.14) y viene en <c>null</c> si nunca se mandó. Y en <c>null</c> en una fila de fianza,
/// porque una fianza no es un documento que se mande: no hay PDF que enviar. Antes de F4-02 esta
/// columna era un <c>null</c> constante y el comentario que la acompañaba decía que no había todavía
/// de dónde sacarlo.
/// </para>
/// </summary>
public sealed record CuentaFila(
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoFilaCuenta>))] TipoFilaCuenta Tipo,
    Guid Id,
    Guid? SerieId,
    int? SerieNumero,
    int? Numero,
    string? ComprobanteTipo,
    int? ComprobanteNumero,
    DateOnly? Fecha,
    Guid ClienteId,
    string? Cliente,
    Guid? UnidadId,
    string? Unidad,
    Guid? ContratoId,
    string? Concepto,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal? Total,
    decimal? Retencion,
    decimal? Cobrado,
    decimal? Pendiente,
    decimal? SaldoADevolver,
    DateOnly? Vencimiento,
    int? DiasVencida,
    bool? Vencida,
    string Estado,
    bool? Modificada,
    bool TieneReservaConAnticipo,
    string? EstadoEnvio,

    /// <summary>
    /// Si a la factura se la tocó <b>después</b> de que saliera su correo (F4-02, §3.14 y F3-08). Es
    /// <c>null</c> en una fianza, como el resto de lo que una fianza no puede expresar: no hay
    /// documento que mandar ni documento que se pueda cambiar después de mandarlo.
    /// </summary>
    bool? ModificadaTrasEnvio,
    bool Cobrable);

/// <summary>
/// Suma de una moneda en el pie de la vista (F3-10, §3.13 «Totales al pie: solo gerente/admin, por
/// moneda»).
/// <para>
/// <b>Nunca se convierte entre monedas</b>: consolidar en dólares es de F5-01 con D9 y el tipo de
/// cambio congelado. Aquí cada moneda se suma consigo misma, que es lo único que se puede sumar sin
/// mentir.
/// </para>
/// </summary>
/// <param name="Total">
/// Suma de <c>total</c>: el total de las facturas y el importe exigido de las fianzas.
/// </param>
/// <param name="Retencion">Suma de retenciones. Las fianzas no retienen y no aportan.</param>
/// <param name="Cobrado">Suma de dinero cobrado de verdad, en cualquiera de los dos destinos.</param>
/// <param name="Pendiente">
/// Suma del pendiente de las facturas más lo que falta por exigir de las fianzas
/// (<c>exigido − cobrado</c>). No es lo mismo que el saldo a devolver de una fianza, que va al revés:
/// lo pendiente se pide, el saldo se devuelve.
/// </param>
public sealed record TotalesCuenta(
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    decimal Total,
    decimal Retencion,
    decimal Cobrado,
    decimal Pendiente);

/// <summary>
/// Respuesta de la vista única de cuentas (F3-10, §3.13). Es una lista paginada como las demás,
/// <b>más</b> los totales, que son la única diferencia con un listado normal.
/// <para>
/// <b><paramref name="Totales"/> viene en <c>null</c> para el empleado</b>, y no con un objeto de
/// ceros: el criterio de aceptación de la tarea es literalmente «Empleado no recibe totales por API».
/// Un objeto con ceros es un total —0,00 cobrado, 0,00 pendiente— que alguien puede sumar, imprimir o
///ansas en una auditoría. Un nulo no se puede sumar.
/// </para>
/// </summary>
/// <param name="Filtro">
/// Los filtros con los que se ha respondido la vista, tal y como llegaron. §3.13 pide que el estado
/// del filtro viva en la URL; esto es solo la vuelta que la interfaz puede leer para pintar los
/// controles sin tener que reconstruirlos.
/// </param>
public sealed record CuentasRespuesta(
    IReadOnlyList<CuentaFila> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<TotalesCuenta>? Totales,
    FiltroCuentas Filtro);

/// <summary>
/// Los filtros que se aplicaron, ya normalizados (F3-10, §3.13).
/// <para>
/// <b>F4-02 añadió los tres de envío y ahora sí filtran.</b> Hasta que existió <c>envio</c>
/// (docs/ESPECIFICACION.md §3.14) no se aceptaban: un filtro que se acepta y se ignora es peor que
/// uno que no existe, porque devuelve filas que parecen filtradas. La columna
/// <see cref="CuentaFila.EstadoEnvio"/> también era un <c>null</c> constante por el mismo motivo y
/// ahora sale real.
/// </para>
/// <para>
/// La semántica de los tres, que es la que dice <c>docs/DOC_TECNICA.md §6.7</c>:
/// <list type="bullet">
/// <item><c>noEnviadas</c>: <b>nadie lo intentó</b> — no hay ninguna fila de envío.</item>
/// <item><c>envioFallido</c>: <b>se intentó y no salió</b> — hay un envío <c>Fallido</c> y ninguno
/// que saliera.</item>
/// <item><c>modificadasTrasEnvio</c>: se mandó y después alguien lo tocó (F3-08).</item>
/// </list>
/// Las dos primeras no se solapan a propósito: «no enviada» y «envío fallido» son preguntas
/// distintas —«¿se le llegó a mandar?» y «¿se intentó?»— y si la primera significara «no llegó a
/// salir» serían la misma lista con dos nombres.
/// </para>
/// </summary>
public sealed record FiltroCuentas(
    int? Anio,
    Guid? SerieId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda? Moneda,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura? Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoCobroFactura>))] EstadoCobroFactura? EstadoCobro,
    Guid? ClienteId,
    Guid? UnidadId,
    bool Vencidas,
    bool Modificadas,
    bool NoEnviadas,
    bool EnvioFallido,
    bool ModificadasTrasEnvio,
    bool IncluirProformas,
    bool IncluirFianzas);