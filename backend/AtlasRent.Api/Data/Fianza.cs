namespace AtlasRent.Api.Data;

/// <summary>
/// La fianza de un contrato (F3-09, docs/ESPECIFICACION.md §3.11).
/// <para>
/// <b>Es una fila de la vista única de cuentas</b> (§3.13), con su insignia de tipo: aparece junto a las
/// facturas y los cobros porque es dinero del mismo contrato, y el cliente la ve como una de sus
/// cuentas.
/// </para>
/// <para>
/// <b>Lo cobrado y lo que queda no son columnas.</b> Es lo que hace F3-06 con las facturas, y el
/// motivo es el mismo: un importe guardado al lado del movimiento que lo produce se desincroniza en
/// cuanto el movimiento cambia —un cobro anulado, una devolución con descuento— y entonces la
/// pantalla muestra un número que la base no tiene. Aquí <c>cobrado</c> sale de sumar las asignaciones
/// vivas de <c>cobro_asignacion</c> que apuntan a esta fianza, y <c>saldo</c> sale de restar las
/// devoluciones. Por eso la entidad tiene su <see cref="Id"/>: sin él no habría forma de preguntar a
/// la base cuánto se ha cobrado de esta fianza concreta.
/// </para>
/// <para>
/// <b>La columna <see cref="Estado"/> solo guarda la decisión del operador</b> —retenida, en
/// liquidación, devuelta o retenida parcialmente— y en ninguno de los dos casos en que se escribe
/// (<see cref="Dominio.EstadoFianza.Pendiente"/> y <see cref="Dominio.EstadoFianza.Parcial"/>) la
/// decisión es "todavía no he decidido", que es exactamente lo que significa que siga sin escribir.
/// El estado que ve el usuario lo compone <c>ServicioFianzas</c>.
/// </para>
/// <para>
/// <b>Una fianza por contrato y moneda.</b> Dos fianzas del mismo contrato y la misma moneda no se
/// distinguen en nada que el negocio pueda decir: serían dos filas con el mismo saldo que hay que
/// cuadrar a mano. La regla es de lectura, no de negocio resuelto: si el negocio necesita más de una
/// (una fianza de garantía y otra de servicios, por ejemplo), hay que decidirlo, y hasta entonces el
/// alta de la segunda se rechaza.
/// </para>
/// <para>
/// <b>Nada se borra</b> (AGENTS.md §6): una fianza devuelta sigue aquí, con su estado, y las
/// devoluciones siguen en <c>fianza_devolucion</c>.
/// </para>
/// </summary>
public sealed class Fianza : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <summary>
    /// Contrato al que pertenece la fianza. Obligatorio: una fianza sin contrato no tiene a quién
    /// devolverle el dinero ni en cuya cuenta aparece (§3.13).
    /// </summary>
    public Guid ContratoId { get; set; }

    public Contrato Contrato { get; set; } = null!;

    /// <summary>
    /// Cuánto se exige en total, en la moneda de la fianza (<c>numeric(18,2)</c>, AGENTS.md §6).
    /// <para>
    /// Va aquí y no se recalcula desde las facturas porque el §3.11 no dice que la fianza sea la suma
    /// de nada: es lo que el contrato exige. Si mañana el negocio dice que es un porcentaje, este
    /// importe es el que lo refleja sin tocar nada de lo que ya se cobró.
    /// </para>
    /// </summary>
    public decimal ImporteExigido { get; set; }

    /// <summary>
    /// Moneda en que se exige y se devuelve la fianza. Va fijada en la fila y no se deduce del
    /// contrato porque es el dato que decide si un cobro puede aplicarla (§3.10.2).
    /// </summary>
    public Dominio.Moneda Moneda { get; set; }

    /// <summary>
    /// Cuántas fianzas cubre este depósito. §3.11 lo pide por separado del importe porque un contrato
    /// puede exigir "un mes de renta" o "dos meses": es la misma cantidad en dinero y son dos
    /// fianzas que se devuelven por separado cuando el plazo se cumple.
    /// <para>
    /// <b>TODO(A#)</b> el modelo no reparte el saldo entre ellas: <c>fianza</c> es una sola fila con un
    /// número, y la devolución es un único movimiento con su importe y su descuento. Si el negocio
    /// quiere devolverlas una a una, hay que decidir qué se devuelve primero y qué queda retenido, y esa
    /// regla no está escrita.
    /// </para>
    /// </summary>
    public int NumeroFianzas { get; set; }

    /// <summary>
    /// La decisión del operador sobre la fianza (§3.11). Solo se escribe cuando hay decisión; el
    /// estado que ve el usuario lo compone <c>ServicioFianzas</c> con lo cobrado.
    /// </summary>
    public Dominio.EstadoFianza Estado { get; set; }

    /// <summary>Cuándo se decidió retener la fianza entera. Nula mientras no se haya decidido.</summary>
    public DateTimeOffset? RetenidaAt { get; set; }

    /// <summary>Cuándo empezó la liquidación. Nula mientras no se haya decidido.</summary>
    public DateTimeOffset? EnLiquidacionAt { get; set; }

    /// <summary>Cuándo se devolvió. Nula mientras no se haya devuelto.</summary>
    public DateTimeOffset? DevueltaAt { get; set; }

    /// <summary>
    /// Devoluciones registradas, con su importe y su descuento. Las filas no se borran: una fianza
    /// devuelta dos veces es un error que el servicio impide, pero si llegara a existir, el rastro
    /// tiene que estar.
    /// </summary>
    public List<FianzaDevolucion> Devoluciones { get; set; } = [];

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}