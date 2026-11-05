namespace AtlasRent.Api.Dominio;

/// <summary>
/// Situación de cobro de una factura: lo cobrado, lo ajustado, lo que queda y en qué estado está.
/// </summary>
/// <param name="Cobrado">
/// Suma de las asignaciones vigentes que apuntan a la factura, en la moneda de la factura.
/// </param>
/// <param name="Ajustado">
/// Suma de los ajustes por diferencia de cambio vigentes de la factura (§3.10.2, F3-07). <b>Va
/// aparte de <paramref name="Cobrado"/> y no se suma en la respuesta de la factura</b> porque no es
/// dinero que entró en caja: es el céntimo que dejó el redondeo o el cambio. Mezclarlos haría que un
/// informe dijera que la caja recibió dinero que nadie recibió.
/// </param>
/// <param name="Pendiente">Neto a cobrar menos lo cobrado menos lo ajustado, nunca negativo.</param>
/// <param name="Estado">Derivado de lo anterior: §3.8.</param>
/// <param name="Vencida">Pendiente mayor que cero y hoy ya pasó el vencimiento.</param>
/// <param name="DiasVencida">Días desde el vencimiento; cero si no está vencida.</param>
public sealed record SituacionCobroFactura(
    decimal Cobrado,
    decimal Ajustado,
    decimal Pendiente,
    EstadoCobroFactura Estado,
    bool Vencida,
    int DiasVencida);

/// <summary>
/// Saldo y estado de cobro de una factura (F3-06, docs/ESPECIFICACION.md §3.8 y §3.9; F3-07 añade
/// el ajuste por diferencia de cambio de §3.10.2; F3-04 hace que la retención llegue con valor).
/// <para>
/// <b>El pendiente es el total menos las retenciones menos lo cobrado menos lo ajustado</b>, y el
/// estado sale de comparar ese pendiente con cero. Vive aquí y no en el servicio porque lo leen
/// cuatro tareas distintas —F3-04 (retenciones), F3-07 (ajuste), F3-08 (anulación de factura) y
/// F3-10 (la vista de cuentas)— y porque es un cálculo de verdad: si se duplica en cada una de
/// ellas, un día las cuatro dirán cosas distintas sobre la misma factura.
/// </para>
/// <para>
/// <b>La retención cuenta como cobrada a efectos de estado y no como dinero</b>, y por lo mismo el
/// <b>ajuste por diferencia de cambio</b> cierra la factura sin ser dinero cobrado (§3.9 y §3.10.2).
/// Los dos entran como parámetros y no como algo que este cálculo busque: quien sabe qué
/// retenciones y qué ajustes existen son los servicios, no esta función.
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
    /// Suma de las retenciones de la factura (§3.9, F3-04), en positivo. El parámetro existía desde
    /// F3-06 para que F3-04 no tuviera que cambiar la firma ni las reglas de este cálculo; desde
    /// F3-04 llega con el valor real de la columna <c>factura.retencion</c>.
    /// </param>
    /// <param name="cobrado">Suma de las asignaciones vigentes de cobros a esta factura.</param>
    /// <param name="ajustado">
    /// Suma de los ajustes por diferencia de cambio vigentes (§3.10.2, F3-07). Cero hasta que
    /// exista alguno.
    /// </param>
    /// <param name="vencimiento">Día de vencimiento en día de negocio.</param>
    /// <param name="hoy">Hoy en Santo Domingo; lo pone el reloj de la aplicación, no el servidor UTC.</param>
    /// <param name="estadoFactura">
    /// Estado del documento (F3-08). Una factura anulada no tiene pendiente: su número se conserva pero
    /// la deuda desapareció, y sumarla en el aging haría que el cliente debiera dinero por un documento
    /// que ya no vale. Por eso entra en este cálculo y no se resuelve en quien llama: cuatro tareas
    /// distintas leen estos números y todas tienen que ver lo mismo.
    /// </param>
    public static SituacionCobroFactura Calcular(
        decimal total,
        decimal retencion,
        decimal cobrado,
        decimal ajustado,
        DateOnly vencimiento,
        DateOnly hoy,
        EstadoFactura estadoFactura = EstadoFactura.Emitida)
    {
        if (estadoFactura == EstadoFactura.Anulada)
        {
            return new SituacionCobroFactura(0m, 0m, 0m, EstadoCobroFactura.Anulada, false, 0);
        }

        // El neto a cobrar es lo que la factura dice que hay que ingresar (docs/ESPECIFICACION.md
        // §3.9). El redondeo es el del sistema (D27) y se aplica una sola vez, al total menos la
        // retención, que es donde el céntimo puede aparecer.
        var neto = Redondeo.Importe(total - retencion);

        // Retención y ajuste cierran la factura sin haber pasado por la caja, así que los dos
        // restan del pendiente; el dinero que sí entró es <paramref name="cobrado"/>, y por eso el
        // estado se deriva de la suma de los tres y no de lo cobrado solo.
        var pendiente = Redondeo.Importe(Math.Max(0m, neto - cobrado - ajustado));

        var cubierto = cobrado + ajustado;

        var estado = pendiente <= 0m
            ? EstadoCobroFactura.Pagada
            : cubierto > 0m
                ? EstadoCobroFactura.Parcial
                : EstadoCobroFactura.Pendiente;

        var vencida = pendiente > 0m && TiempoNegocio.EstaVencida(vencimiento, hoy);

        return new SituacionCobroFactura(
            Redondeo.Importe(cobrado),
            Redondeo.Importe(ajustado),
            pendiente,
            estado,
            vencida,
            vencida ? TiempoNegocio.DiasDeVencida(vencimiento, hoy) : 0);
    }
}