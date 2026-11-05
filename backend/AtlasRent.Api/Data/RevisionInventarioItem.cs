using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Línea de la revisión de salida: el estado en que se encuentra <b>un</b> elemento del inventario
/// congelado del contrato el día que el cliente se va (F2-08, §3.6).
/// <para>
/// <b>Ausente y dañado no son lo mismo</b>, y por eso hay tres estados y no una bandera de "falta":
/// lo que no está y lo que está roto llevan a liquidaciones distintas y a discusiones distintas con
/// el cliente. "Presente" no lleva nota obligatoria: forzar un texto cuando no hay nada que anotar
/// solo genera ruido que luego alguien tiene que leer.
/// </para>
/// <para>
/// No copia nombre ni cantidad: se apoya en <see cref="ContratoInventarioItemId"/>, que es una fila
/// que no se vuelve a escribir. Duplicar aquí los valores haría que la revisión pudiera discrepar de
/// la foto que dice comprobar.
/// </para>
/// </summary>
public sealed class RevisionInventarioItem : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid RevisionId { get; set; }

    public RevisionInventario? Revision { get; set; }

    /// <summary>Elemento del inventario congelado del contrato que se está revisando.</summary>
    public Guid ContratoInventarioItemId { get; set; }

    public ContratoInventarioItem? ContratoInventarioItem { get; set; }

    /// <summary>Presente, ausente o dañado. Nunca nulo.</summary>
    public EstadoRevisionInventario Estado { get; set; }

    /// <summary>
    /// Qué pasó. Obligatoria en <see cref="EstadoRevisionInventario.Ausente"/> y
    /// <see cref="EstadoRevisionInventario.Danado"/>, opcional en
    /// <see cref="EstadoRevisionInventario.Presente"/>.
    /// </summary>
    public string? Nota { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}