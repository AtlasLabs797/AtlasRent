namespace AtlasRent.Api.Data;

/// <summary>
/// Aplicación de un cobro a una factura (F3-06, docs/ESPECIFICACION.md §3.10.1; F3-07 añade el pago
/// en otra moneda, §3.10.2 y D8), o a una fianza (F3-09, §3.11).
/// <para>
/// <b>Desde F3-09 la fila tiene dos destinos y son excluyentes.</b> <see cref="FacturaId"/> y
/// <see cref="FianzaId"/> son nulos uno de los dos, nunca los dos y nunca ninguno: lo exige el
/// <c>CHECK ck_cobro_asignacion_destino_unico</c> de la tabla. No es una comodidad del modelo, es la
/// razón por la que cobrar una fianza es el mismo camino que cobrar una factura —un cobro con una
/// asignación— y por lo tanto aparece en los informes de caja (F5) y en la vista de cuentas (§3.13)
/// sin un segundo camino que mantener.
/// </para>
/// <para>
/// <b>Una asignación a una fianza no es deuda de factura y no toca ninguna factura.</b> Por eso ni el
/// pendiente de una factura ni su estado de cobro se enteran, y por eso la FIFO de F3-06 —que reparte
/// entre facturas del contrato— no propone fianzas: quien paga una fianza la señala.
/// </para>
/// <para>
/// <b>El importe va en la moneda de la factura</b>, que es la deuda que salda, y
/// <see cref="ImporteRecibido"/> es lo que salió de la caja en la moneda del cobro. Los dos se
/// guardan porque §3.10.2 lo dice y porque sin el par no se puede reconstruir un pago en pesos:
/// «1000 USD» sin «59.500 DOP a 59,50» no explica nada.
/// </para>
/// <para>
/// <b>En un cobro de la misma moneda, <see cref="ImporteRecibido"/> y
/// <see cref="TipoCambioValor"/> son nulos.</b> No es pereza: ahí no se aplicó ninguna conversión y
/// un cambio de 1 guardaría una conversión que no ocurrió. Es el mismo motivo por el que la fila
/// de <c>tipo_cambio</c> del dólar no existe (F3-01).
/// </para>
/// <para>
/// <b>La fila no se borra nunca.</b> Anular un cobro —o anular la factura que pagaba, que es D6 y es
/// F3-08— no borra la asignación: la deja sin vigor, y lo que no cuenta para nada es una asignación
/// de un cobro anulado. Borrarla perdería la trazabilidad de qué cobro pagó qué, que es exactamente
/// lo que §3.10.1 llama historial completo.
/// </para>
/// </summary>
public sealed class CobroAsignacion : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    public Guid CobroId { get; set; }

    public Cobro? Cobro { get; set; }

    /// <summary>
    /// Factura a la que se aplica este importe. <b>Nulo desde F3-09</b> cuando lo que se paga es una
    /// fianza; ver <see cref="FianzaId"/>. Exacto uno de los dos, siempre.
    /// </summary>
    public Guid? FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Fianza a la que se aplica este importe (F3-09, §3.11). Nulo cuando lo que se paga es una factura.
    /// Exacto uno de los dos, siempre.
    /// </summary>
    public Guid? FianzaId { get; set; }

    public Fianza? Fianza { get; set; }

    /// <summary>
    /// Importe aplicado a la factura o a la fianza, en la moneda de ese destino. Nunca supera lo
    /// pendiente de la factura (F3-06) ni el saldo de la fianza (F3-09), y el servicio lo comprueba
    /// contra el pendiente real, no contra el total.
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>
    /// Parte del cobro, en la moneda del cobro, que se aplicó a esta factura
    /// (<c>numeric(18,2)</c>, F3-07). Nulo cuando el cobro y la factura comparten moneda.
    /// </summary>
    public decimal? ImporteRecibido { get; set; }

    /// <summary>
    /// Tipo de cambio realmente aplicado en esta línea, <c>numeric(18,6)</c> (F3-07): el equivalente
    /// de <see cref="ImporteRecibido"/> en la moneda de la factura es
    /// <c>ImporteRecibido / TipoCambioValor</c>. Puede no ser el del día si el gerente fijó el que
    /// se usó de verdad en el mostrador; el del día sigue en <see cref="Cobro.TipoCambioValor"/>, que
    /// es lo que congela D9. Nulo cuando el cobro y la factura comparten moneda.
    /// </summary>
    public decimal? TipoCambioValor { get; set; }

    /// <summary>
    /// Si la asignación cuenta. Se pone en falso al anular el cobro (F3-06) y al anular la factura
    /// que pagaba (D6, F3-08). Por defecto verdadera: una asignación nueva siempre cuenta.
    /// </summary>
    public bool Vigente { get; set; } = true;

    /// <summary>
    /// Cuándo dejó de contar esta asignación (F3-08, D6). Nula mientras sigue viva.
    /// <para>
    /// Es lo que distingue las dos maneras de que una asignación deje de contar: anular el cobro
    /// (F3-06) y anular la factura que pagaba (F3-08). En el segundo caso
    /// <see cref="MotivoAnulacion"/> es el de la factura, y por eso la columna no es un adorno: sin
    /// ella no se puede saber por qué un cobro que sigue vigente dejó de pagar una factura.
    /// </para>
    /// </summary>
    public DateTimeOffset? AnuladaAt { get; set; }

    /// <summary>Quién la dejó sin vigor.</summary>
    public Guid? AnuladaPor { get; set; }

    /// <summary>Motivo, o el de la factura anulada cuando lo fue la factura lo que la dejó sin vigor.</summary>
    public string? MotivoAnulacion { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}