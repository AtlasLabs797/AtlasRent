using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Bloqueo de una unidad por un rango de fechas: lo que deja un contrato activo o una reserva
/// mensual (docs/ESPECIFICACION.md §3.4 y §3.5).
/// <para>
/// La disponibilidad no es un campo de la unidad: es esta tabla, y se comprueba en la propia base de
/// datos con una restricción de exclusión para que dos altas simultáneas no puedan solaparse
/// (AGENTS.md §6). El rango es <c>[inicio, fin)</c>: un contrato que acaba el 31 deja libre el día 1
/// siguiente.
/// </para>
/// <para>
/// <see cref="Bloquea"/> es el estado de bloqueo de <strong>esta fila</strong>, no un campo "ocupado"
/// de la unidad, que no existe. Ponerla a false es como se libera (reserva cancelada, contrato
/// finalizado): la fila se queda en la tabla con su historia en lugar de borrarse, porque los
/// registros con efecto económico no se borran y porque hace falta poder auditar por qué un día
/// estuvo bloqueado. La restricción de exclusión solo mira las filas que bloquean.
/// </para>
/// </summary>
public sealed class OcupacionRango
{
    public Guid Id { get; set; }

    public Guid UnidadId { get; set; }

    /// <summary>Qué fila de negocio deja el bloqueo: <see cref="OrigenOcupacion"/>.</summary>
    public OrigenOcupacion Origen { get; set; }

    /// <summary>Identificador de esa fila de negocio: un contrato o una reserva mensual.</summary>
    public Guid OrigenId { get; set; }

    /// <summary>Primer día bloqueado, incluido.</summary>
    public DateOnly FechaInicio { get; set; }

    /// <summary>Primer día libre, excluido. Null = abierto hasta que se libera.</summary>
    public DateOnly? FechaFin { get; set; }

    /// <summary>Si esta fila bloquea de verdad. False = liberada, pero se conserva.</summary>
    public bool Bloquea { get; set; }

    public DateTimeOffset CreadoAt { get; set; }
}