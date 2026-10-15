using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Lo que se decidió al liquidar un contrato (F2-09, docs/ESPECIFICACION.md §3.5: "la liquidación
/// cierra inventario de salida, fianza y saldos").
/// <para>
/// <b>Esto no es un cobro.</b> La tabla <c>cobro</c> es de F3-01 y todavía no existe, y las de
/// fianza también. Aquí queda escrito <i>lo que se decidió</i>: cuándo se liquidó, quién lo hizo, qué
/// importe propuso la revisión de salida por faltantes y qué se hace con la fianza. Es el dato del
/// que F3 partirá para emitir el cobro real y mover la fianza, y por eso está separado del contrato:
/// es un hecho propio, con su fecha y su autor, no un campo más del contrato.
/// </para>
/// <para>
/// <b>Nace cerrada y no se vuelve a escribir</b>, igual que la revisión de salida de F2-08 y que el
/// incremento ya aplicado de F2-06: el índice único sobre <see cref="ContratoId"/> es la regla de "un
/// contrato se liquida una vez" puesta en la base y no solo en el servicio. Editar una liquidación
/// cerrada no es corregirla: es reescribir el cierre económico de un contrato.
/// </para>
/// <para>
/// Los importes van en la moneda del contrato y por eso no llevan columna de moneda: una moneda por
/// documento (AGENTS.md §6). No hay ni <c>cobro_id</c> ni <c>fianza_devolucion_id</c>, porque esas
/// tablas son de F3 y una columna que apunta a algo que no existe no es un dato, es una promesa que
/// nadie puede cumplir.
/// </para>
/// </summary>
public sealed class ContratoLiquidacion : IEntidadTrazada
{
    /// <summary>Contrato liquidado. Único: se liquida una vez y no se vuelve atrás.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>
    /// Revisión de salida contra la que se liquidó. Nula cuando el contrato no tenía inventario:
    /// §3.6 dice "si el contrato tiene inventario", y §3.1 hace el inventario base opcional.
    /// </summary>
    public Guid? RevisionInventarioId { get; set; }

    public RevisionInventario? RevisionInventario { get; set; }

    /// <summary>
    /// Día de negocio en el que se liquidó (D16). No es la fecha en que terminó el contrato: esa está
    /// en <c>contrato.fecha_fin_real</c>, y un contrato puede quedarse semanas sin liquidar, que es
    /// justo lo que la alerta persistente de §3.5 avisa.
    /// </summary>
    public DateOnly FechaLiquidacion { get; set; }

    /// <summary>
    /// Quién liquida. Siempre una persona: liquidar exige <c>contrato.estado</c> (gerente o admin) y
    /// el trabajo automático de las 07:00 no liquida, solo finaliza.
    /// </summary>
    public Guid LiquidadoPorId { get; set; }

    /// <summary>
    /// Importe a cobrar por los faltantes que dejó la revisión de salida, en la moneda del contrato.
    /// Sale de <see cref="RevisionInventario.ImportePropuesto"/> y es 0 cuando no hay inventario o
    /// cuando la revisión no propuso nada.
    /// <para>
    /// <b>No es un cobro</b>: nadie ha cobrado esto todavía. Es el número contra el que F3-01 emite la
    /// factura o proforma que sí lo cobre.
    /// </para>
    /// </summary>
    public decimal ImporteFaltantes { get; set; }

    /// <summary>Qué se decide hacer con la fianza del contrato. Los dos casos, en <see cref="DecisionFianza"/>.</summary>
    public DecisionFianza DecisionFianza { get; set; }

    /// <summary>
    /// Importe de la fianza pactada en el contrato, copiado aquí al liquidar. Se copia en lugar de
    /// leerse de <c>contrato.fianza</c> porque la liquidación dice sobre qué cifra se decidió y, si
    /// mañana alguien toca la fianza de un contrato liquidado, el cierre no puede cambiar de número
    /// sin que nadie lo note.
    /// </summary>
    public decimal FianzaContrato { get; set; }

    /// <summary>
    /// Parte de la fianza que se retiene por los faltantes. Nunca más que
    /// <see cref="FianzaContrato"/>: de una fianza no se retiene más de lo que hay, y lo que el
    /// faltante supera a la fianza es un cobro pendiente, no una retención.
    /// </summary>
    public decimal FianzaRetenida { get; set; }

    /// <summary>Motivo o nota de la liquidación, si el gerente dejó alguna. No obligatorio.</summary>
    public string? Observacion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public Guid Id { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}