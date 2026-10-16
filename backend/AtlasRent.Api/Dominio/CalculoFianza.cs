namespace AtlasRent.Api.Dominio;

/// <summary>
/// Las tres cuentas de una fianza y su estado, en un solo sitio (F3-09, docs/ESPECIFICACION.md §3.11).
/// <para>
/// <b>Están aquí y no en el servicio porque lo usan dos.</b> <c>ServicioFianzas</c> compone el estado
/// que ve el usuario, y <c>ServicioCobros</c> tiene que.rejectar un cobro que paga más que el saldo
/// de la fianza. Si cada uno tuviera su propia copia de la fórmula, un cambio en una dejaría la otra
/// diciendo otra cosa, y el síntoma sería un saldo que no cuadra sin ningún error visible.
/// </para>
/// <para>
/// <b>El saldo no se guarda.</b> Es <c>cobrado − devuelto − descontado</c> calculado en la consulta de
/// lectura, igual que el pendiente de una factura en F3-06: un importe guardado al lado del movimiento
/// que lo produce se desincroniza en cuanto el movimiento cambia.
/// </para>
/// </summary>
public static class CalculoFianza
{
    /// <summary>
    /// Lo que queda de la fianza por devolver, en su moneda. Lo que ya salió y lo que se retuvo no se
    /// cuentan dos veces: el descuento sale de la fianza igual que la devolución, y §3.11 los baja
    /// juntos.
    /// <para>
    /// <b>Nunca baja de cero</b>, y no solo porque el servicio no lo deje. Anular el cobro que pagó la
    /// fianza (F3-06) deja de contar lo cobrado, y si parte ya se había devuelto la resta da negativa;
    /// un saldo negativo no es un saldo, es un desajuste, y quien lo ve en la pantalla no puede saber
    /// qué hacer con él. Se muestra en cero y el movimiento que lo produjo sigue a la vista en
    /// <c>cobro</c>, que es donde se explica.
    /// </para>
    /// </summary>
    public static decimal Saldo(decimal cobrado, decimal devuelto, decimal descontado) =>
        Math.Max(0m, Redondeo.Importe(cobrado - devuelto - descontado));

    /// <summary>
    /// Cuánto falta por cobrar de la fianza: lo exigido menos lo cobrado, sin bajar de cero.
    /// <para>
    /// <b>No es el saldo, y la diferencia es el sentido de la pregunta.</b> El saldo dice cuánto hay
    /// que devolver y solo tiene sentido cuando ya se cobró; lo pendiente de cobro es lo contrario, y
    /// empieza en el importe exigido. Validar un cobro contra el saldo daría un409 siempre, porque en
    /// el momento de cobrar el saldo es cero por definición. Es la misma regla que el pendiente de una
    /// factura en F3-06: ni la una ni la otra admiten un sobrepago, y lo que sobra de más va al
    /// excedente del cobro y al saldo a favor del contrato (§3.10.1).
    /// </para>
    /// </summary>
    public static decimal PendienteDeCobro(decimal importeExigido, decimal cobrado) =>
        Math.Max(0m, Redondeo.Importe(importeExigido - cobrado));

    /// <summary>
    /// El estado que ve el usuario: la decisión del operador si la hay, y si no la que se deduce de lo
    /// cobrado.
    /// <para>
    /// La regla es que la columna solo guarda decisiones, así que si lo que tiene guardado no es una
    /// decisión —<see cref="EstadoFianza.Pendiente"/> o <see cref="EstadoFianza.Parcial"/>, los dos
    /// valores con los que nace una fianza— lo que hay que decidir es si se ha cobrado algo.
    /// </para>
    /// </summary>
    public static EstadoFianza Estado(EstadoFianza decision, decimal cobrado) =>
        SinDecision(decision) ? (cobrado <= 0m ? EstadoFianza.Pendiente : EstadoFianza.Parcial) : decision;

    /// <summary>
    /// Si la columna <c>estado</c> no guarda ninguna decisión del operador. Es lo que
    /// <see cref="Estado"/> sustituye por lo cobrado.
    /// </summary>
    public static bool SinDecision(EstadoFianza estado) =>
        estado is EstadoFianza.Pendiente or EstadoFianza.Parcial;

    /// <summary>
    /// Si a la fianza le queda algo por hacer: no está cerrada y no está en liquidación. Es lo que
    /// pregunta el trabajo diario de §3.16 para saber a quién hay que avisar.
    /// </summary>
    public static bool Pendiente(EstadoFianza estado) => !SinPendiente(estado);

    /// <summary>
    /// Si la fianza ya está cerrada y no vuelve a moverse: devuelta entera o retenida a medias (§3.11).
    /// No hay camino de vuelta desde aquí, y por eso devolverla o liquidarla otra vez es un 409.
    /// </summary>
    public static bool Cerrada(EstadoFianza estado) =>
        estado is EstadoFianza.Devuelta or EstadoFianza.RetenidaParcial;

    /// <summary>
    /// Si la fianza ya no admite dinero: está devuelta, retenida a medias o en liquidación. Cobrar a una
    /// fianza liquidada sería meter dinero en una cuenta que ya tiene dueño, y ni el saldo ni el
    /// informe de caja sabrían qué hacer con él.
    /// </summary>
    public static bool NoCobrable(EstadoFianza estado) =>
        Cerrada(estado) || estado is EstadoFianza.EnLiquidacion;

    /// <summary>
    /// Si el asunto de la fianza ya no está pendiente: se devolvió, se retuvo a medias o está en
    /// liquidación. Es lo que cierra el aviso de §3.16.
    /// <para>
    /// <see cref="EstadoFianza.RetenidaParcial"/> cuenta como resuelta porque devolver en parte es
    /// devolver, y porque una fianza retenida a propósito por faltantes no es un olvido: avisar de ella
    /// todos los días sería la forma de alerta que enseña a ignorar la bandeja.
    /// </para>
    /// </summary>
    public static bool SinPendiente(EstadoFianza estado) =>
        Cerrada(estado) || estado is EstadoFianza.EnLiquidacion;
}