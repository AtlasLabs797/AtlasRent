using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Alta y edición de la cabecera de una factura (F3-02, docs/ESPECIFICACION.md §3.8).
/// <para>
/// Las líneas van aparte, por su propio endpoint: son las que se dan de alta, se editan y se borran
/// mientras el documento está en borrador, y metidas aquí obligarían a reescribir la factura entera
/// para corregir un descuento.
/// </para>
/// <para>
/// Las reglas de negocio no van aquí como atributos sino en el servicio, que devuelve el conflicto
/// en 409; los atributos solo cubren lo que el modelo puede rechazar sin abrir la base de datos.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaPeticion
{
    /// <summary>Cliente al que se factura. Obligatorio: una factura siempre es para alguien.</summary>
    [Required]
    public required Guid ClienteId { get; init; }

    /// <summary>Unidad facturada, si la factura viene de una unidad concreta (§3.8).</summary>
    public Guid? UnidadId { get; init; }

    /// <summary>Contrato de origen, si la factura viene de un contrato. Nulo si es manual.</summary>
    public Guid? ContratoId { get; init; }

    /// <summary>
    /// Serie de numeración (§3.7). Es la que fija la moneda cuando la tiene fija y la que decide si
    /// lo que sale es una factura normal, fiscal o una proforma.
    /// </summary>
    [Required]
    public required Guid SerieId { get; init; }

    /// <summary>
    /// Moneda única de todo el documento (D7). No hay conversión en ningún sitio de la factura.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Moneda>))]
    public required Moneda Moneda { get; init; }

    /// <summary>Día de vencimiento, en día de negocio (§3.13).</summary>
    [Required]
    public required DateOnly FechaVencimiento { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Concepto { get; init; }
}

/// <summary>
/// Línea de una factura. El importe y su ITBIS los calcula la aplicación, no quien la escribe
/// (§3.8, D27). En una línea de retención también (§3.9, D14).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaLineaPeticion
{
    [JsonConverter(typeof(JsonStringEnumConverter<TipoLineaFactura>))]
    [Required]
    public required TipoLineaFactura Tipo { get; init; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public required string Concepto { get; init; }

    /// <summary>
    /// Cantidad facturable, con cuatro decimales. En una línea de tipo <see cref="TipoLineaFactura.Descuento"/>
    /// el importe sale en negativo: lo que decide el signo es la cantidad o el precio, no el tipo.
    /// </summary>
    public decimal Cantidad { get; init; }

    /// <summary>
    /// Precio unitario en la moneda de la factura. <b>En una línea de retención se ignora</b>: el
    /// importe sale de la base y el porcentaje que dice el tipo, no de lo que se escriba aquí.
    /// </summary>
    public decimal PrecioUnitario { get; init; }

    /// <summary>
    /// Tipo de ITBIS de la línea. Nulo si la línea no lleva impuesto: el ITBIS es opcional por línea
    /// (§3.8). El porcentaje se copia a la línea al guardarla, y de ahí no vuelve a cambiar.
    /// </summary>
    public Guid? TipoItbisId { get; init; }

    /// <summary>
    /// Tipo de retención de la línea, obligatorio si <see cref="Tipo"/> es
    /// <see cref="TipoLineaFactura.Retencion"/> y prohibido en cualquier otro caso (§3.9). De aquí
    /// salen el porcentaje y la base; el importe lo calcula la aplicación.
    /// </summary>
    public Guid? TipoRetencionId { get; init; }
}

/// <summary>
/// Referencia de la e-factura externa que se teclea a mano (D5, docs/ESPECIFICACION.md §3.7).
/// <para>
/// Sin validación ni lógica: es un texto que luego se imprime en el PDF. Se acepta vacía para
/// borrarla, y eso no es un caso raro sino el normal de una factura que no tiene e-factura.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReferenciaEfacturaPeticion
{
    /// <summary>Referencia tecleada. Vacía o ausente deja la factura sin referencia.</summary>
    [StringLength(120)]
    public string? Referencia { get; init; }
}

/// <summary>
/// Fila de la lista: la cabecera y los totales, sin las líneas. La lista se pagina y traerlas
/// todas en cada fila no sirve de nada.
/// <para>
/// <b>Sin importes agregados</b> ni totales por periodo: los informes son de §3.15 y solo gerente y
/// administrador los ven (F3-10).
/// </para>
/// <para>
/// <b>Cobrado, pendiente y estado de cobro son derivados</b> (§3.8) y los rellena
/// <c>ServicioCobros</c> con la suma de las asignaciones vigentes de la página, en una consulta
/// agregada y no una por factura. No son columnas de la factura: dependen de los cobros, que
/// cambian aunque la factura no se toque.
/// </para>
/// <para>
/// F3-04 añade <see cref="Retencion"/>, <see cref="NetoACobrar"/> y el indicador
/// <see cref="RetencionSinComprobante"/>. Los dos primeros son columnas de la factura; el tercero
/// se rellena con una consulta de los comprobantes pendientes de la página, igual que el derivado
/// de cobro.
/// </para>
/// </summary>
public sealed record FacturaResumen(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    Guid? ContratoId,
    Guid SerieId,
    int SerieNumero,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie TipoSerie,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly? FechaEmision,
    DateOnly FechaVencimiento,
    string Concepto,
    decimal Subtotal,
    decimal Itbis,
    decimal Total,
    decimal Retencion,
    decimal NetoACobrar,
    int Lineas,
    DateTimeOffset CreatedAt)
{
    /// <summary>Lo cobrado de esta factura, en su moneda, por asignaciones vigentes (F3-06).</summary>
    public decimal Cobrado { get; init; }

    /// <summary>
    /// Cerrado por ajustes por diferencia de cambio vigentes (F3-07, §3.10.2). <b>No se suma dentro
    /// de <see cref="Cobrado"/> a propósito</b>: el ajuste no es dinero que entró en la caja, es el
    /// céntimo que dejó el cambio. Mezclarlos haría que un informe de caja dijera que se recibieron
    /// más dólares de los que entraron. Los dos juntos cierran la factura, y eso lo dicen
    /// <see cref="Pendiente"/> y <see cref="EstadoCobro"/>.
    /// </summary>
    public decimal Ajustado { get; init; }

    /// <summary>Neto a cobrar menos lo cobrado. Nunca negativo: cobrar de más es saldo a favor (§3.10.1).</summary>
    public decimal Pendiente { get; init; }

    /// <summary>Estado derivado de §3.8: pendiente, parcial o pagada.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoCobroFactura>))]
    public EstadoCobroFactura EstadoCobro { get; init; }

    /// <summary>Pendiente mayor que cero con el vencimiento ya pasado (día de negocio de Santo Domingo).</summary>
    public bool Vencida { get; init; }

    /// <summary>
    /// La factura retiene y su comprobante sigue pendiente (§3.9, F3-04). Es el aviso de que hay que
    /// pedirle el <c>607</c> al cliente.
    /// <para>
    /// Es falso en las tres cosas que se parecen y no son esto: una factura sin retención, una que
    /// todavía está en borrador (el comprobante se crea al emitir, así que antes de eso no hay
    /// nada pendiente) y una que ya lo tiene recibido.
    /// </para>
    /// </summary>
    public bool RetencionSinComprobante { get; init; }

    /// <summary>
    /// Referencia de la e-factura externa tecleada a mano (D5). Nula en la mayoría de las facturas:
    /// solo la lleva la que alguien ha enlazado a una e-factura emitida fuera.
    /// </summary>
    public string? ReferenciaEfactura { get; init; }
}

/// <summary>
/// Factura con sus líneas, que es como la lee el detalle, el PDF y la vista de cuentas (§3.13).
/// <para>
/// <b>Cobrado, pendiente y estado de cobro los aporta F3-06</b>, que es de donde salen: no son
/// columnas de la factura sino la suma de las asignaciones vigentes que apuntan a ella (§3.8).
/// </para>
/// <para>
/// F3-04 añade la retención y el neto a cobrar, que son columnas de la factura (§3.9), y el indicador
/// <see cref="RetencionSinComprobante"/>, que es derivado y sale de <c>comprobante_retencion</c>.
/// </para>
/// </summary>
public sealed record FacturaRespuesta(
    Guid Id,
    Guid ClienteId,
    string? ClienteNombre,
    Guid? UnidadId,
    string? UnidadNombre,
    Guid? ContratoId,
    Guid SerieId,
    int SerieNumero,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))] EstadoFactura Estado,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoSerie>))] TipoSerie TipoSerie,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Moneda>))] Moneda Moneda,
    DateOnly? FechaEmision,
    DateOnly FechaVencimiento,
    string Concepto,
    decimal Subtotal,
    decimal Itbis,
    decimal Total,
    decimal Retencion,
    decimal NetoACobrar,
    decimal? TipoCambioValor,
    DateOnly? TipoCambioFecha,
    Guid? TipoCambioId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<FacturaLineaRespuesta> Lineas)
{
    /// <summary>Lo cobrado de esta factura, en su moneda, por asignaciones vigentes (F3-06).</summary>
    public decimal Cobrado { get; init; }

    /// <summary>
    /// Cerrado por ajustes por diferencia de cambio vigentes (F3-07, §3.10.2). <b>No se suma dentro
    /// de <see cref="Cobrado"/> a propósito</b>: el ajuste no es dinero que entró en la caja, es el
    /// céntimo que dejó el cambio. Mezclarlos haría que un informe de caja dijera que se recibieron
    /// más dólares de los que entraron. Los dos juntos cierran la factura, y eso lo dicen
    /// <see cref="Pendiente"/> y <see cref="EstadoCobro"/>.
    /// </summary>
    public decimal Ajustado { get; init; }

    /// <summary>Neto a cobrar menos lo cobrado. Nunca negativo: cobrar de más es saldo a favor (§3.10.1).</summary>
    public decimal Pendiente { get; init; }

    /// <summary>Estado derivado de §3.8: pendiente, parcial o pagada.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EstadoCobroFactura>))]
    public EstadoCobroFactura EstadoCobro { get; init; }

    /// <summary>Pendiente mayor que cero con el vencimiento ya pasado (día de negocio de Santo Domingo).</summary>
    public bool Vencida { get; init; }

    /// <summary>
    /// La factura retiene y su comprobante sigue pendiente (§3.9, F3-04). Es el aviso de que hay que
    /// pedirle el <c>607</c> al cliente.
    /// <para>
    /// Es falso en las tres cosas que se parecen y no son esto: una factura sin retención, una que
    /// todavía está en borrador (el comprobante se crea al emitir, así que antes de eso no hay
    /// nada pendiente) y una que ya lo tiene recibido.
    /// </para>
    /// </summary>
    public bool RetencionSinComprobante { get; init; }

    /// <summary>
    /// Referencia de la e-factura externa tecleada a mano (D5). Nula en la mayoría de las facturas:
    /// solo la lleva la que alguien ha enlazado a una e-factura emitida fuera.
    /// </summary>
    public string? ReferenciaEfactura { get; init; }

    /// <summary>
    /// Comprobante fiscal asignado al emitir (tipo y número, como en el PDF). Nulo si la serie no lo
    /// genera. Se rellena en el detalle y en todas las respuestas de escritura.
    /// </summary>
    public string? ComprobanteFiscal { get; init; }

    /// <summary>Id del comprobante fiscal, si hay.</summary>
    public Guid? ComprobanteFiscalId { get; init; }

    /// <summary>
    /// Alguien tocó la factura después del último envío que salió (mismo criterio que la vista de
    /// cuentas). Se rellena en el detalle y en las respuestas de escritura.
    /// </summary>
    public bool ModificadaTrasEnvio { get; init; }

    /// <summary>Id del comprobante de retención de la factura, si retiene. Se rellena en el detalle y en las respuestas de escritura.</summary>
    public Guid? ComprobanteRetencionId { get; init; }
}

/// <summary>
/// Línea tal y como se guarda y tal y como se imprime. El porcentaje de ITBIS va porque es el dato
/// congelado de la línea, no una referencia a la configuración de hoy; en una línea de retención,
/// además del porcentaje y la base, van porque de eso depende lo retenido.
/// </summary>
public sealed record FacturaLineaRespuesta(
    Guid Id,
    int Posicion,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoLineaFactura>))] TipoLineaFactura Tipo,
    string Concepto,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    Guid? TipoItbisId,
    string? TipoItbisNombre,
    decimal? PorcentajeItbis,
    decimal ItbisImporte,
    Guid? TipoRetencionId,
    string? TipoRetencionNombre,
    decimal? PorcentajeRetencion,
    // Nulo en toda línea que no sea de retención, así que aquí hace falta el conversor que además
    // entiende el nulo: el genérico de <see cref="EstadoFactura"/> no sirve para un enum nullable.
    [property: JsonConverter(typeof(JsonStringEnumConverter))] BaseRetencion? BaseRetencion);

// ─────────────────────────────── F3-08 ───────────────────────────────
// Cerrar el ciclo de vida del documento: modificar lo que ya se emitió (nunca su número), anular,
// duplicar, convertir una proforma en factura y rehacer en otra moneda (§3.8, D6, D7, D28).

/// <summary>
/// Modificación de una factura ya emitida (F3-08). Solo las líneas: la cabecera y el número están
/// fuera de alcance porque una factura emitida no se reescribe, se anula y se vuelve a emitir.
/// <para>
/// Se mandan las líneas que cambian, no todas. Cada entrada reemplaza los campos editables de esa
/// línea y las líneas que no van en la lista se quedan como están; añadir o quitar líneas de una
/// factura emitida no tiene sentido porque cambiaría el total sin que nadie lo haya decidido como
/// documento nuevo.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaModificacionPeticion
{
    /// <summary>Líneas a cambiar, en el orden que tengan.</summary>
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<FacturaLineaModificacionPeticion> Lineas { get; init; }
}

/// <summary>
/// Campos editables de una línea de factura emitida. Es la línea entera menos su <c>Id</c>, que es lo
/// único que no cambia nunca.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaLineaModificacionPeticion
{
    /// <summary>La línea a cambiar.</summary>
    [Required]
    public required Guid LineaId { get; init; }

    /// <summary>Renta, servicio o retención. No se puede pasar de renta o servicio a retención (F3-04).</summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter<TipoLineaFactura>))]
    public required TipoLineaFactura Tipo { get; init; }

    /// <summary>Texto que se imprime y se ve en la factura.</summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Concepto { get; init; }

    /// <summary>
    /// Cantidad facturable, con cuatro decimales. Tiene que ser mayor que cero, y eso lo comprueba
    /// el servicio con el mismo criterio que una línea de borrador, no un atributo.
    /// </summary>
    public decimal Cantidad { get; init; }

    /// <summary>
    /// Precio unitario en la moneda de la factura. No negativo, por el mismo motivo que arriba.
    /// </summary>
    public decimal PrecioUnitario { get; init; }

    /// <summary>Tipo de ITBIS configurado, o nulo si la línea no lleva.</summary>
    public Guid? TipoItbisId { get; init; }

    /// <summary>Tipo de retención configurado, o nulo si la línea no lleva.</summary>
    public Guid? TipoRetencionId { get; init; }
}

/// <summary>Anulación de una factura emitida (F3-08, §3.8). El motivo es lo único que se pide.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FacturaAnulacionPeticion
{
    /// <summary>
    /// Por qué se anula. Obligatorio y no vacío, por el mismo motivo que en los cobros: sin motivo la
    /// anulación es un borrado con otro nombre, y este documento ya salió con un número que no
    /// vuelve a liberarse (D28).
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Motivo { get; init; }
}

/// <summary>
/// Cómo quedaría la anulación si se aceptara, sin escribir nada (F3-08). Es lo que necesita la
/// pantalla de confirmación: no basta con decir «se va a anular», hay que enseñar el dinero que se
/// toca antes de que alguien lo toque.
/// </summary>
/// <param name="FacturaId">La factura que se anularía.</param>
/// <param name="Numero">Su número, que se conserva aunque se anule (D28).</param>
/// <param name="Estado">Su estado ahora mismo.</param>
/// <param name="NetoACobrar">Lo que estaba pendiente de cobrar.</param>
/// <param name="ComprobanteFiscalSeAnula">Si su comprobante fiscal pasaría a anulado con ella.</param>
/// <param name="NumeroSeConserva">
/// Siempre <c>true</c>. Está aquí para que quien llama a la API lo vea escrito en el contrato: el
/// número no se libera nunca.
/// </param>
/// <param name="CobrosQueSeAnulan">Cobros que se quedarían sin ninguna asignación viva.</param>
/// <param name="CobrosQueSeToCan">Cobros que solo dejan de pagar esta factura y siguen cobrando otra.</param>
public sealed record FacturaAnulacionSimulacion(
    Guid FacturaId,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))]
    EstadoFactura Estado,
    decimal NetoACobrar,
    bool ComprobanteFiscalSeAnula,
    bool NumeroSeConserva,
    IReadOnlyList<CobroAfectadoPorFactura> CobrosQueSeAnulan,
    IReadOnlyList<CobroAfectadoPorFactura> CobrosQueSeToCan);

/// <summary>
/// Un cobro que la anulación de una factura toca. Sirve igual para los que se anulan enteros y para
/// los que solo pierden esta factura, porque el usuario necesita ver el importe en los dos casos; lo
/// que los distingue es <see cref="SeAnulaEntero"/>.
/// </summary>
/// <param name="CobroId">El cobro.</param>
/// <param name="FechaEfectiva">Su fecha de valor.</param>
/// <param name="Moneda">Moneda del cobro.</param>
/// <param name="ImporteRecibido">Cuánto entró.</param>
/// <param name="ImporteDeEstaFactura">Cuánto de ese importe estaba asignado a esta factura.</param>
/// <param name="ImporteQueSigueVigente">
/// Lo que el cobro sigue cubriendo con otras facturas. Cero si se anula entero.
/// </param>
/// <param name="CambioDelSaldoAFavor">
/// Cuánto cambiaría el saldo a favor del contrato (§3.10.1), con su signo: vuelve el crédito que el
/// cobro consumió y sale el excedente que dejó. Cero si el cobro no se anula entero.
/// </param>
/// <param name="SeAnulaEntero">Si el cobro se queda sin ninguna asignación viva y se anula con ella.</param>
public sealed record CobroAfectadoPorFactura(
    Guid CobroId,
    DateOnly FechaEfectiva,
    Moneda Moneda,
    decimal ImporteRecibido,
    decimal ImporteDeEstaFactura,
    decimal ImporteQueSigueVigente,
    decimal CambioDelSaldoAFavor,
    bool SeAnulaEntero);

/// <summary>Resultado de duplicar una factura (F3-08).</summary>
/// <param name="Factura">El borrador nuevo, con su identificador.</param>
/// <param name="Similares">
/// Facturas del mismo contrato, periodo y concepto que ya existían. No bloquean nada: el aviso está
/// para que el operador mire, y puede que la otra sea una factura anterior correcta.
/// </param>
public sealed record FacturaDuplicacionRespuesta(
    FacturaResumen Factura,
    IReadOnlyList<FacturaDuplicadaParecida> Similares);

/// <summary>Factura casi idéntica a la duplicada, que ya existía (F3-08).</summary>
public sealed record FacturaDuplicadaParecida(
    Guid FacturaId,
    int? Numero,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoFactura>))]
    EstadoFactura Estado,
    DateOnly? PeriodoInicio,
    DateOnly? PeriodoFin,
    decimal Total,
    string Concepto);

/// <summary>
/// Conversión de una proforma en factura (F3-08, D7). Solo la serie destino: la moneda y los
/// importes no se tocan.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProformaConversionPeticion
{
    /// <summary>
    /// Serie en la que se emite la factura nueva. Si no se manda, la primera fiscal activa, que es
    /// la 3 de la instalación normal (§3.7); el usuario puede cambiarla porque la fiscal activa
    /// puede no ser la que le sirve.
    /// </summary>
    public Guid? SerieId { get; init; }
}

/// <summary>Resultado de convertir una proforma en factura (F3-08).</summary>
/// <param name="Factura">La factura nueva.</param>
/// <param name="ProformaId">La proforma de origen, que queda <c>Convertida</c> y deja de contar.</param>
/// <param name="SerieIdUsada">La serie en la que se emitió, para que la interfaz pueda explicarlo.</param>
public sealed record ProformaConversionRespuesta(
    FacturaRespuesta Factura,
    Guid ProformaId,
    Guid SerieIdUsada);

/// <summary>
/// Rehacer una factura anulada en otra moneda (F3-08). El importe va tecleado a propósito: el tipo de
/// cambio con el que se calcula el importe nuevo lo pone quien lo teclea, y esa es una decisión
/// comercial que la aplicación no puede tomar por su cuenta.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RehacerMonedaPeticion
{
    /// <summary>Moneda de la factura nueva. Obligatoria: es lo que se está rehaciendo.</summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Moneda Moneda { get; init; }

    /// <summary>Serie de la factura nueva. Obligatoria: se elige a mano porque la serie fija la moneda.</summary>
    [Required]
    public required Guid SerieId { get; init; }

    /// <summary>Total de la factura nueva, ya en la moneda pedida. Tiene que ser mayor que cero.</summary>
    public decimal Total { get; init; }

    /// <summary>
    /// Por qué se anula la original. Obligatorio: la original queda anulada y su número no vuelve a
    /// liberarse (D28), y eso se explica siempre.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Motivo { get; init; }
}

/// <summary>Resultado de rehacer en otra moneda (F3-08).</summary>
/// <param name="FacturaAnulada">La factura que quedó anulada.</param>
/// <param name="NumeroAnulado">Su número, que se conserva aunque se anule (D28).</param>
/// <param name="Nueva">El borrador nuevo, en la moneda pedida.</param>
public sealed record FacturaRehacerRespuesta(
    Guid FacturaAnulada,
    int? NumeroAnulado,
    FacturaResumen Nueva);
