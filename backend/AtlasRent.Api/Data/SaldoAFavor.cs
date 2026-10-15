using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Saldo a favor de un contrato (F3-06, docs/ESPECIFICACION.md §3.10.1): dinero cobrado de más que
/// todavía no se ha aplicado a ninguna factura.
/// <para>
/// <b>Una fila por contrato y moneda, no una por cobro.</b> El saldo a favor es un saldo, no un
/// documento: se actualiza cuando un cobro deja excedente y cuando otro lo consume, y así la deuda
/// del cliente se lee en un sitio sin tener que sumar la historia entera.
/// </para>
/// <para>
/// La fila <b>no se borra</b> cuando llega a cero: sin <c>DELETE</c> para <c>rent_app</c> y porque
/// un saldo que se consumió es un hecho del historial del contrato, no una fila que sobre. Y no se
/// crea para un cobro que deja el saldo intacto: la tabla dice dónde hay crédito, no qué contratos
/// han cobrado alguna vez.
/// </para>
/// </summary>
public sealed class SaldoAFavor : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <summary>Contrato dueño del crédito. El saldo a favor es del contrato, no del cliente.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Moneda del crédito. Una factura nunca se convierte (D7), y el crédito tampoco.</summary>
    public Moneda Moneda { get; set; }

    /// <summary>Importe disponible. Cero o más: un saldo a favor negativo es un cobro, no un saldo.</summary>
    public decimal Importe { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}