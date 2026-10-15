using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Subida de renta pactada con fecha, que el trabajo diario aplica solo y con aviso interno
/// (F2-06, docs/ESPECIFICACION.md §3.5: "los incrementos de renta, en porcentaje o importe fijo,
/// con fecha, se aplican solos con aviso interno").
/// <para>
/// El incremento se guarda <b>una vez</b>, con su fecha, y de ahí vive: no hay que repetirlo cada
/// mes ni marcarlo al llegar el día. Que se aplique o no lo decide <see cref="Aplicado"/>, que
/// escribe el trabajo el día que le toca.
/// </para>
/// <para>
/// <b>Nunca se borra</b>: es valor económico. Un incremento mal dado de alta se anula con el
/// mecanismo de cambios del contrato, no se elimina.
/// </para>
/// </summary>
public sealed class ContratoIncremento : IEntidadTrazada
{
    /// <summary>Contrato al que aplica.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Porcentaje o importe fijo. Decide qué columna de valor está llena (ver el CHECK).</summary>
    public TipoIncremento Tipo { get; set; }

    /// <summary>
    /// Puntos porcentuales, no la fracción: un 5 % es <c>5</c>, no <c>0,05</c>. Solo llena si
    /// <see cref="Tipo"/> es <see cref="TipoIncremento.Porcentaje"/>.
    /// </summary>
    public decimal? Porcentaje { get; set; }

    /// <summary>
    /// Importe absoluto a sumar, <b>en la moneda del contrato</b>. Solo llena si <see cref="Tipo"/>
    /// es <see cref="TipoIncremento.ImporteFijo"/>.
    /// </summary>
    public decimal? Importe { get; set; }

    /// <summary>
    /// Día en que el incremento entra en vigor. Se aplica en la renovación cuyo bloque empieza ese
    /// día o más tarde, nunca antes: una subida con fecha futura no adelanta la renta.
    /// </summary>
    public DateOnly FechaVigencia { get; set; }

    /// <summary>Si el trabajo diario ya lo aplicó a la renta del contrato.</summary>
    public bool Aplicado { get; set; }

    /// <summary>
    /// Si alguien lo canceló antes de que se aplicara. Un incremento no se borra: se cancela, y la
    /// fila se queda con la marca para poder responder "¿por qué este incremento no llegó a subir
    /// la renta?".
    /// <para>
    /// Cancelado y aplicado son excluyentes: un incremento ya aplicado no se cancela, porque su
    /// efecto está en la renta y en una versión, y deshacer eso es un cambio de contrato, no retirar
    /// una nota.
    /// </para>
    /// </summary>
    public bool Cancelado { get; set; }

    /// <summary>Cuándo se canceló. Nulo mientras siga vivo.</summary>
    public DateTimeOffset? CanceladoAt { get; set; }

    /// <summary>Cuándo se aplicó. Nulo mientras no lo esté.</summary>
    public DateTimeOffset? AplicadoAt { get; set; }

    /// <summary>
    /// Renta del contrato justo antes de aplicarlo. Junto con <see cref="RentaDespues"/> deja
    /// ver el efecto sin tener que reconstruirlo sumando incrementos a mano.
    /// </summary>
    public decimal? RentaAntes { get; set; }

    /// <summary>Renta del contrato justo después de aplicarlo.</summary>
    public decimal? RentaDespues { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}