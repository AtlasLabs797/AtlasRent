namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado de una fianza (F3-09, docs/ESPECIFICACION.md §3.11).
/// <para>
/// Son los mismos estados que §3.11 escribe, pero el enum se lleva por separado del
/// <see cref="EstadoCobro"/> de las facturas porque responden a cosas distintas: el de la factura dice
/// cuánto de la deuda de un documento está saldado, y este dice qué se ha decidido hacer con un
/// depósito que ya está cobrado.
/// </para>
/// <para>
/// <b>Los valores son explícitos y no van por su posición</b>, como en
/// <see cref="DecisionFianza"/>: la columna guarda el número y renumerar el enum un día no puede
/// querer decir que la devolución de ayer con "retenida" hoy se lea como "parcial".
/// </para>
/// <para>
/// <b>Qué se guarda y qué se deriva.</b> De estos seis, <b>solo cuatro se escriben</b> —
/// <see cref="Retenida"/>, <see cref="EnLiquidacion"/>, <see cref="Devuelta"/> y
/// <see cref="RetenidaParcial"/>— porque son los cuatro que alguien decide a mano.
/// <see cref="Pendiente"/> y <see cref="Parcial"/> los deduce <c>ServicioFianzas</c> de lo cobrado y
/// no se guardan nunca: si se guardaran, bastaría un cobro anulado que no actualizara la columna para
/// que el estado mentira (es el mismo motivo por el que la tabla <c>fianza</c> no guarda ni
/// <c>cobrado</c> ni <c>saldo</c>, y por el que <c>cobro_asignacion</c> no guarda el total aplicado).
/// </para>
/// </summary>
public enum EstadoFianza
{
    /// <summary>
    /// No se ha cobrado nada de la fianza. <b>Derivado</b> de <c>cobrado == 0</c>.
    /// </summary>
    Pendiente = 1,

    /// <summary>
    /// Hay dinero de la fianza cobrado y todavía no se ha decidido su destino.
    /// <b>Derivado</b> de <c>cobrado &gt; 0</c> mientras la columna no diga otra cosa.
    /// <para>
    /// <b>TODO(A#)</b> con la cadena de §3.11 (<c>Pendiente → Parcial → Retenida → En liquidación →
    /// Devuelta / Retenida parcial</c>) el nombre <c>Parcial</c> no llega a describir el caso de una
    /// fianza cobrada del todo y pendiente solo de devolver, que es el más común. Se deriva como
    /// <c>Parcial</c> porque es lo mínimo y no inventa un estado que la especificación no tiene; si
    /// el negocio quiere distinguir "cobrada entera" de "cobrada a medias", hace falta un estado más y
    /// esa decisión es suya.
    /// </para>
    /// </summary>
    Parcial = 2,

    /// <summary>
    /// La fianza se retiene entera: no se devuelve nada de ella.
    /// <para>
    /// Es la decisión de §3.6 ("los descuentos se aplican contra la fianza solo con gerente/admin"), y
    /// como se toma a mano se guarda con su fecha en <c>fianza.retenida_at</c>.
    /// </para>
    /// </summary>
    Retenida = 3,

    /// <summary>
    /// La fianza está en liquidación: ya se decidió qué hacer con ella y el movimiento está en curso.
    /// <para>
    /// Es el estado que da nombre a la decisión de <see cref="DecisionFianza"/> de F2-09, que es lo que
    /// dejó escrita la liquidación del contrato y lo que esta fila viene a ejecutar.
    /// </para>
    /// </summary>
    EnLiquidacion = 4,

    /// <summary>
    /// La fianza se ha devuelto entera. Terminal: no se vuelve a mover (§3.11).
    /// </summary>
    Devuelta = 5,

    /// <summary>
    /// Se devolvió una parte y el resto se retuvo, normalmente por faltantes de inventario (§3.6).
    /// Terminal por la misma razón que <see cref="Devuelta"/>.
    /// </summary>
    RetenidaParcial = 6,
}