namespace AtlasRent.Api.Dominio;

/// <summary>
/// Situación de cobro de una factura: lo cobrado, lo que queda y en qué estado está.
/// </summary>
/// <param name="Cobrado">
/// Suma de las asignaciones vigentes que apuntan a la factura, en la moneda de la factura.
/// </param>
/// <param name="Pendiente">Neto a cobrar menos lo cobrado, nunca negativo.</param>
/// <param name="Estado">Derivado de lo anterior: §3.8.</param>
/// <param name="Vencida">Pendiente mayor que cero y hoy ya pasó el vencimiento.</param>
/// <param name="DiasVencida">Días desde el vencimiento; cero si no está vencida.</param>
public sealed record SituacionCobroFactura(
    decimal Cobrado,
    decimal Pendiente,
    EstadoCobroFactura Estado,
    bool Vencida,
    int DiasVencida);

/// <summary>
/// Saldo y estado de cobro de una factura (F3-06, docs/ESPECIFICACION.md §3.8 y §3.9).
/// <para>
/// <b>El pendiente es el total menos las retenciones menos lo cobrado</b>, y el estado sale de
/// comparar ese pendiente con cero. Vive aquí y no en el servicio porque lo leen tres tareas
/// distintas —F3-04 (retenciones), F3-07 (cobros en otra moneda) y F3-10 (la vista de cuentas)— y
/// porque es un cálculo de verdad: si se duplica en cada uno de ellos, un día los tres dirán cosas
/// distintas sobre la misma factura.
/// </para>
/// <para>
/// <b>La retención cuenta como cobrada a efectos de estado y no como dinero.</b> §3.9 lo dice: la
/// factura está Pagada cuando cobros más retenciones llegan al total. Por eso la retención entra
/// como parámetro y no como algo que este cálculo busque, y por eso
/// <paramref name="cobrado"/> es solo dinero de cobros: el dinero que la empresa retiene y luego
/// entrega no ha pasado por esta caja.
/// </para>
/// <para>
/// El pendiente se acota a cero a propósito. Un pendiente negativo significaría que se ha cobrado de
/// más, y eso es un <b>saldo a favor</b> (§3.10.1), que vive en otra tabla y con otras reglas. Si
/// esta función devolviera un negativo, cualquier suma de pendientes de la vista de cuentas
/// descuadraría contra el dinero real, que es justo el descuadre que este cálculo no puede
/// permitir.
/// </para>
/// </summary>
public static class CalculoCobro
{
    /// <summary>
    /// Situación de cobro de una factura a partir de sus totales y de lo que tiene cobrado.
    /// </summary>
    /// <param name="total">Total de la factura, ITBIS incluido.</param>
    /// <param name="retencion">
    /// Suma de las retenciones de la factura (§3.9). Hoy siempre cero, porque la columna de
    /// retención es de F3-04; el parámetro existe desde ahora para que F3-04 no tenga que cambiar
    /// la firma ni las reglas de este cálculo.
    /// </param>
    /// <param name="cobrado">Suma de las asignaciones vigentes de cobros a esta factura.</param>
    /// <param name="vencimiento">Día de vencimiento en día de negocio.</param>
    /// <param name="hoy">Hoy en Santo Domingo; lo pone el reloj de la aplicación, no el servidor UTC.</param>
    public static SituacionCobroFactura Calcular(
        decimal total,
        decimal retencion,
        decimal cobrado,
        DateOnly vencimiento,
        DateOnly hoy)
    {
        // El neto a cobrar es lo que la factura dice que hay que ingresar (docs/ESPECIFICACION.md
        // §3.9). El redondeo es el del sistema (D27) y se aplica una sola vez, al total menos la
        // retención, que es donde el céntimo puede aparecer.
        var neto = Redondeo.Importe(total - retencion);
        var pendiente = Redondeo.Importe(Math.Max(0m, neto - cobrado));

        var estado = pendiente <= 0m
            ? EstadoCobroFactura.Pagada
            : cobrado > 0m
                ? EstadoCobroFactura.Parcial
                : EstadoCobroFactura.Pendiente;

        var vencida = pendiente > 0m && TiempoNegocio.EstaVencida(vencimiento, hoy);

        return new SituacionCobroFactura(
            Redondeo.Importe(cobrado),
            pendiente,
            estado,
            vencida,
            vencida ? TiempoNegocio.DiasDeVencida(vencimiento, hoy) : 0);
    }
}