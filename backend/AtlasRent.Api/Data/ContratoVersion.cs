using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Historial inmutable de los cambios de un contrato activo (docs/ESPECIFICACION.md §3.5:
/// "un cambio en contrato activo genera versión nueva" y "el cambio de renta afecta solo a
/// facturas futuras").
/// <para>
/// <b>Es un libro de solo añadir.</b> No tiene forma de modificarse ni de borrarse desde el
/// servicio: el único <c>DELETE</c> que existe en el código de este dominio es el de las líneas de
/// cotización, que se reescriben mientras el documento está en borrador. Aquí lo que se escribió se
/// queda, porque es lo que demuestra qué se aprobó, qué se cobró y en qué condiciones.
/// </para>
/// <para>
/// La primera versión (número 1) es la que nace al activar el contrato, con el contenido con el que
/// empezó a ser vigente.
/// </para>
/// </summary>
public sealed class ContratoVersion : IEntidadTrazada
{
    /// <summary>Contrato al que pertenece la versión.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>
    /// Número de versión dentro del contrato, empezando en 1. Único junto con
    /// <see cref="ContratoId"/>: es la red que evita que dos cambios simultáneos se numeren igual.
    /// </summary>
    public int Numero { get; set; }

    /// <summary>Qué cambió, escrito para que se lea en la ficha del contrato.</summary>
    public string Cambio { get; set; } = string.Empty;

    /// <summary>
    /// Renta que tenía el contrato antes del cambio. Es la que da la razón de que el cambio afecta
    /// solo a facturas futuras: lo ya facturado se queda con lo que se facturó.
    /// </summary>
    public decimal? RentaAnterior { get; set; }

    /// <summary>Renta con la que queda el contrato a partir de este cambio.</summary>
    public decimal? RentaNueva { get; set; }

    /// <summary>Moneda de las dos rentas, para no confundir DOP con USD.</summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Alcance del cambio, en el idioma de quien lo lee. La especificación (§3.5) dice que el cambio
    /// de renta afecta <b>solo a facturas futuras</b>, y esto es lo que lo deja escrito en el
    /// contrato: en F3-01, la generación de facturas lee este alcance y no toca lo ya emitido.
    /// </summary>
    public string Alcance { get; set; } = string.Empty;

    /// <summary>Quién hizo el cambio.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}