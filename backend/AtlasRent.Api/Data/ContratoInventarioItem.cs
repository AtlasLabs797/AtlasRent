namespace AtlasRent.Api.Data;

/// <summary>
/// <b>2 de 3. El inventario del contrato</b>: la copia congelada del inventario base de la unidad en
/// el momento en que el contrato se activa (F2-08, docs/ESPECIFICACION.md §3.5: "el inventario se
/// copia y congela al activar").
/// <para>
/// Es <b>copia, no referencia</b>. Copia porque si mañana alguien corrige el inventario base de la
/// unidad, el contrato que ya está firmado tiene que seguir viendo lo que había cuando entró el
/// cliente, no lo que hay hoy. Por eso los valores viajan dentro de la fila: <see cref="Nombre"/>,
/// <see cref="Cantidad"/> y <see cref="Notas"/> están aquí escritos, aunque el original siga
/// editable. Es el mismo patrón que la plantilla congelada de F2-07
/// (<see cref="ContratoPlantillaCongelada"/>).</para>
/// <para>
/// <b>No implementa <see cref="IEntidadTrazada"/> y no tiene <c>updated_at</c> a propósito:</b>
/// no existe ningún camino de escritura sobre esta tabla después de la activación. La escribe
/// <c>ServicioInventario.CongelarAsync</c>, dentro de la transacción del activar, y a partir de ahí
/// solo se lee. No hay endpoint que la edite ni que la borre, porque una foto del estado de la unidad
/// el día de la entrada es prueba, y una prueba no se corrige.</para>
/// <para>
/// <see cref="InventarioBaseId"/> apunta al elemento del que se copió, solo para rastrear de dónde
/// salió cada línea. Va sin cascada: si algún día se limpiara el catálogo, esta fila tiene que
/// quedarse.</para>
/// </summary>
public sealed class ContratoInventarioItem
{
    public Guid Id { get; set; }

    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Elemento del inventario base del que salió esta copia. Null solo si el original ya no existe.</summary>
    public Guid? InventarioBaseId { get; set; }

    public InventarioBaseItem? InventarioBaseItem { get; set; }

    /// <summary>Nombre tal y como estaba en el inventario base el día del activar.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Cantidad tal y como estaba en el inventario base el día del activar.</summary>
    public decimal Cantidad { get; set; }

    /// <summary>Nota tal y como estaba en el inventario base el día del activar.</summary>
    public string? Notas { get; set; }

    /// <summary>Posición que tenía en el inventario base, para que la revisión salga en el mismo orden.</summary>
    public int Orden { get; set; }
}