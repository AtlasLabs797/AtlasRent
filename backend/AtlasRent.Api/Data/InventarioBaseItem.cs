using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// <b>1 de 3. El inventario base de la unidad</b>: lo que hay en la unidad (F2-08,
/// docs/ESPECIFICACION.md §3.1, "Inventario base por unidad (opcional), sin fotos").
/// <para>
/// Es el catálogo vivo que el empleado prepara y corrige mientras la unidad está en el mercado.
/// <b>Sin fotos</b>, tal y como dice §3.1: aquí no hay columnas de imagen ni ruta de archivo, y no
/// las debe haber. Si algún día hacen falta, es un subsistema entero que alguien pidió y que aquí no
/// está.</para>
/// <para>
/// No es lo que el contrato firma. Lo que el contrato firma es la copia de esta tabla en el momento
/// de activar (<see cref="ContratoInventarioItem"/>), y lo que se comprueba al salir el cliente es
/// otra cosa distinta (<see cref="RevisionInventario"/>). Editar esta fila después de que un
/// contrato está activo no cambia lo que ese contrato tiene por suyo.</para>
/// <para>
/// <b>Nada se borra.</b> Un elemento que sobra se desactiva con <see cref="Activo"/>, que es lo
/// mismo que hacen las unidades y las plantillas del catálogo: quitar un mueble del inventario
/// base no es borrar que estuvo, es decir que ya no está.</para>
/// </para>
/// </summary>
public sealed class InventarioBaseItem : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    /// <summary>Qué es el elemento: "colchón", "silla de oficina", "nevera".</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Cuántos hay. Cero es un dato legítimo (queda uno en mal estado, se anota en la nota).</summary>
    public decimal Cantidad { get; set; }

    /// <summary>Marca, modelo, medidas, estado. Lo que hace falta para reconocer el elemento.</summary>
    public string? Notas { get; set; }

    /// <summary>
    /// Posición en la lista que escribe el empleado. Sin esta columna el listado saldría en el orden
    /// que la base devuelva, que no está garantizado y descuadra la revisión de salida, donde el orden
    /// es el del documento que firmó el cliente.
    /// </summary>
    public int Orden { get; set; }

    /// <summary>Baja lógica: el elemento ya no forma parte del inventario de la unidad.</summary>
    public bool Activo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}