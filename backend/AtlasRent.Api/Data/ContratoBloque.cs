namespace AtlasRent.Api.Data;

/// <summary>
/// Un bloque de duración por el que ha pasado un contrato (F2-06, docs/ESPECIFICACION.md §3.5).
/// <para>
/// La renovación automática al vencer el bloque "queda en el historial, sin anexo ni firma nueva":
/// no hay documento que firmar porque no hay un contrato nuevo, así que el rastro tiene que ser una
/// fila. Cada bloque guarda las fechas que lo componen y la renta que se pactó durante él.
/// </para>
/// <para>
/// El <b>primer</b> bloque no está aquí: es el contrato mismo (<see cref="Contrato.FechaInicio"/> y
/// <see cref="Contrato.FechaFinInicial"/>), que es lo que se firmó y lo que no se toca. Esta tabla
/// guarda los bloques que abrió la renovación automática, a partir del segundo.
/// </para>
/// <para>
/// <b>Nunca se borra ni se edita</b>: es historial económico. Un bloque que se corrigió mal se anula
/// con otro bloque, no se reescribe.
/// </para>
/// </summary>
public sealed class ContratoBloque : IEntidadTrazada
{
    /// <summary>Contrato al que pertenece el bloque.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>
    /// Número de bloque, contando desde 1 el inicial. El inicial no tiene fila: el primero que sí
    /// la tiene es el 2, porque es el primero que abrió una renovación.
    /// <para>
    /// Con <c>(contrato_id, numero)</c> único. Es la red que evita que dos copias del trabajo de
    /// renovación abiertas a la vez dejen dos bloques iguales para el mismo contrato.
    /// </para>
    /// </summary>
    public int Numero { get; set; }

    /// <summary>Primer día del bloque, inclusive.</summary>
    public DateOnly FechaInicio { get; set; }

    /// <summary>Último día del bloque, inclusive. El bloque siguiente empieza al día siguiente.</summary>
    public DateOnly FechaFin { get; set; }

    /// <summary>
    /// Renta que regía durante el bloque, ya con los incrementos que le tocaron aplicados.
    /// Es la renta que se facturó en este tramo, por eso se guarda aquí y no en el contrato.
    /// </summary>
    public decimal Renta { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}