namespace AtlasRent.Api.Data;

/// <summary>
/// Una devolución de fianza (F3-09, docs/ESPECIFICACION.md §3.11 y §3.6).
/// <para>
/// <b>Es un movimiento de salida con dos partes, no dos movimientos.</b> <see cref="Importe"/> es lo
/// que sale de la empresa hacia el cliente y <see cref="ImporteDescontado"/> es lo que se queda
/// (§3.6: los descuentos se aplican contra la fianza). Meterlos en dos tablas obligaría a cuadrar dos
/// saldos que siempre son del mismo hecho; aquí se guardan juntos y el saldo de la fianza baja por la
/// suma de los dos, que es lo que §3.11 dice.
/// </para>
/// <para>
/// <b>El motivo es obligatorio cuando hay descuento</b>, y también cuando la devolución no deja la
/// fianza a cero. No es un requisito de forma: un descuento es dinero que el cliente no recibe y por
/// eso tiene que decir por qué; y devolver menos de lo cobrado deja saldo retenido, y alguien tiene
/// que poder leer por qué.
/// </para>
/// <para>
/// <b>La fila no se borra nunca</b> (AGENTS.md §6). Una devolución mal hecha no se corrige borrando: el
/// servicio rechaza la segunda devolución de una fianza ya devuelta, que es un estado terminal.
/// </para>
/// </summary>
public sealed class FianzaDevolucion : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    public Guid FianzaId { get; set; }

    public Fianza Fianza { get; set; } = null!;

    /// <summary>
    /// Lo que se devuelve al cliente, en la moneda de la fianza (<c>numeric(18,2)</c>). Puede ser cero
    /// cuando todo lo que sale de la fianza se retiene; no puede ser negativo.
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>
    /// Lo que se retiene de la fianza y no se devuelve, normalmente el importe de los faltantes de
    /// inventario (§3.6). Puede ser cero; con descuento, el motivo es obligatorio.
    /// </summary>
    public decimal ImporteDescontado { get; set; }

    /// <summary>
    /// Por qué se devuelve, y por qué se retiene lo retenido. Nulo solo cuando la devolución deja la
    /// fianza a cero y no hay nada retenido; en cualquier otro caso el servicio lo rechaza.
    /// </summary>
    public string? Motivo { get; set; }

    /// <summary>Día de negocio en que se devuelve (D16), no un instante técnico.</summary>
    public DateOnly Fecha { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}