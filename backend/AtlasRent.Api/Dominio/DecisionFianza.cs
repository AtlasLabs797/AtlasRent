namespace AtlasRent.Api.Dominio;

/// <summary>
/// Qué se hace con la fianza del contrato al liquidarlo (F2-09, docs/ESPECIFICACION.md §3.5 y §3.6).
/// <para>
/// Son dos y no un número porque son dos hechos distintos y porque el segundo la firma quien puede:
/// §3.6 dice que "los descuentos se aplican contra la fianza solo con gerente/admin", y liquidar ya
/// exige <c>contrato.estado</c>, que es justo el permiso de esa fila de §2.
/// </para>
/// <para>
/// Los valores son explícitos y no van por su posición, como en
/// <see cref="EstadoRevisionInventario"/>: la decisión acaba en una columna de la base y volver a
/// numerar el enum un día no puede querer decir que la liquidación de ayer con "devuelta" hoy se lea
/// como "retenida".
/// </para>
/// <para>
/// <b>El movimiento de la fianza no está aquí.</b> Esta decisión es el dato de la liquidación; la
/// tabla <c>fianza</c> y la <c>fianza_devolucion</c> son de F3, y hasta que existan esto no se emite
/// ningún movimiento: es la liquidación la que dice qué hay que hacer, no la que lo hace.
/// </para>
/// </summary>
public enum DecisionFianza
{
    /// <summary>
    /// No hay nada que compensar contra la fianza: se devuelve entera. Es lo que pasa cuando el
    /// contrato no tenía inventario, o cuando la revisión de salida no propuso importe de faltante.
    /// </summary>
    Devuelta = 1,

    /// <summary>
    /// El importe del faltante se descuenta de la fianza (§3.6). Lo que se retiene de verdad es lo que
    /// quepa en la fianza: por encima, lo que falta es un cobro y el cobro es de F3-01.
    /// </summary>
    RetenidaPorFaltantes = 2,
}