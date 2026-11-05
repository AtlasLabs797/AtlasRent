using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Ajuste por diferencia de cambio (F3-07, docs/ESPECIFICACION.md §3.10.2, D8).
/// <para>
/// <b>No es dinero recibido.</b> Es el movimiento con el que se cierra un pendiente que solo se
/// produjo porque el tipo de cambio no dividía exacto o porque se aplicó un cambio distinto al del
/// día. Por eso va en su propia tabla y no como una asignación de cobro: un cobro dice qué dinero
/// entró por dónde, y este fila dice que ese dinero no llegó a cubrir la factura por una diferencia
/// de cambio, que es otro hecho económico con otras reglas.
/// </para>
/// <para>
/// <b>La fila no se borra nunca</b> (AGENTS.md §6). Un ajuste equivocado se anula con motivo, que
/// deja la factura como estaba y conserva el rastro de por qué se creó.
/// </para>
/// <para>
/// El <see cref="CreatedBy"/> es quien lo registró y el <see cref="CreatedAt"/> cuándo; el
/// <see cref="Ip"/> viene de la petición y no de la auditoría, porque la fila tiene que poder
/// decir por sí sola desde dónde se corrigió el saldo.
/// </para>
/// </summary>
public sealed class AjusteDiferenciaCambio : IEntidadTrazada
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <summary>Factura cuyo pendiente cierra. Obligatoria: un ajuste sin factura no cierra nada.</summary>
    public Guid FacturaId { get; set; }

    public Factura? Factura { get; set; }

    /// <summary>
    /// Importe del ajuste, en la moneda de la factura (<see cref="Moneda"/>). Siempre positivo: es
    /// una diferencia que se cierra, no una devolución.
    /// </summary>
    public decimal Importe { get; set; }

    /// <summary>
    /// Moneda del importe. Siempre la de la factura (D7: una factura nunca se convierte); se guarda
    /// porque el ajuste es un hecho económico por sí mismo y un informe lo lee sin volver a la
    /// factura por cada fila.
    /// </summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Tipo de cambio realmente aplicado en el cobro que dejó el pendiente, <c>numeric(18,6)</c>.
    /// Es el número con el que se puede reconstruir por qué salió esa diferencia.
    /// </summary>
    public decimal TipoCambioValor { get; set; }

    /// <summary>
    /// Por qué se ajusta. Obligatorio (400 sin él): un euro de diferencia que nadie explica es
    /// exactamente el tipo de movimiento que después nadie sabe si fue un error.
    /// </summary>
    public string Motivo { get; set; } = string.Empty;

    /// <summary>IP desde la que se registró. Se guarda en la propia fila, además de en la auditoría.</summary>
    public string? Ip { get; set; }

    /// <summary>Cuándo se anuló. Nulo mientras el ajuste está vigente.</summary>
    public DateTimeOffset? AnuladoAt { get; set; }

    /// <summary>Quién lo anuló.</summary>
    public Guid? AnuladoPor { get; set; }

    /// <summary>Por qué se anuló. Obligatorio en la anulación, por la misma razón que en el alta.</summary>
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