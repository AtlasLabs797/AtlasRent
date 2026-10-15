namespace AtlasRent.Api.Data;

/// <summary>
/// <b>3 de 3. La revisión de salida</b>: lo que se comprueba cuando el cliente se va (F2-08,
/// docs/ESPECIFICACION.md §3.6).
/// <para>
/// Es la tercera cosa y no una versión de las otras dos. El inventario base es lo que había;
/// el inventario del contrato es lo que había <b>el día que entró</b>; esta revisión es lo que hay
/// <b>el día que sale</b>, elemento por elemento, con su estado.</para>
/// <para>
/// <b>Bloqueada desde que existe.</b> No hay borrador: la fila se crea ya firmada
/// (<see cref="FirmadaAt"/> y <see cref="FirmadaPorId"/> no son opcionales) y no hay ningún endpoint
/// que la modifique después. Firmar es un hecho histórico, igual que aplicar un incremento de renta
/// (F2-06): por eso hay un índice único sobre <see cref="ContratoId"/> y por eso el servicio devuelve
/// 409 al segundo intento. Editar una revisión firmada no es corregirla, es reescribir lo que el
/// cliente firmó.</para>
/// <para>
/// <b>Firmar no borra nada.</b> El inventario del contrato (<see cref="ContratoInventarioItem"/>) se
/// conserva tal cual, con sus valores: es la contra que se mira contra la cual se firma esta
/// revisión. Sin él, las líneas de <see cref="Items"/> no dirían nada.</para>
/// <para>
/// <see cref="ImportePropuesto"/> es el importe del faltante que el gerente deja constancia al
/// firmar, en la moneda del contrato. No es un cobro: la tabla <c>cobro</c> es de F3-01 y todavía no
/// existe. F2-09 liquida contra este número.</para>
/// </summary>
public sealed class RevisionInventario : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Cuándo se firmó. Nunca nulo: la revisión nace firmada.</summary>
    public DateTimeOffset FirmadaAt { get; set; }

    /// <summary>Quién firmó. La firma de la revisión es la que desbloquea la liquidación (F2-09).</summary>
    public Guid? FirmadaPorId { get; set; }

    /// <summary>
    /// Importe del faltante que el gerente propone, en la moneda del contrato. Queda escrito aunque
    /// todavía no se haya cobrado nada: es la cifra contra la que F2-09 liquida.
    /// </summary>
    public decimal? ImportePropuesto { get; set; }

    /// <summary>Observación general de la salida.</summary>
    public string? Observacion { get; set; }

    /// <summary>Una línea por cada elemento del inventario congelado del contrato, con su estado.</summary>
    public List<RevisionInventarioItem> Items { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}