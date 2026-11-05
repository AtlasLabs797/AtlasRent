using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Bloqueo de una unidad por un día suelto: lo que deja una reserva corta —medio día o día
/// completo— o un mantenimiento (docs/ESPECIFICACION.md §3.4).
/// <para>
/// No hay restricción de exclusión aquí porque el día admite <strong>dos medios días</strong> y no
/// uno: lo decide el trigger <c>ocupacion_dia_comprueba_capacidad</c> de la base de datos, que
/// bloquea la fila de la unidad antes de contar para que dos inserciones simultáneas no puedan las
/// dos (AGENTS.md §6).
/// </para>
/// <para>
/// <see cref="Bloquea"/> es el estado de bloqueo de <strong>esta fila</strong>, igual que en
/// <see cref="OcupacionRango"/>, y no un campo "ocupado" de la unidad: la unidad no lo tiene.
/// Liberar es ponerla a false y conservarla.
/// </para>
/// </summary>
public sealed class OcupacionDia
{
    public Guid Id { get; set; }

    public Guid UnidadId { get; set; }

    /// <summary>Qué fila de negocio deja el bloqueo: <see cref="OrigenOcupacion"/>.</summary>
    public OrigenOcupacion Origen { get; set; }

    /// <summary>Identificador de esa fila de negocio: la reserva corta o el aviso de mantenimiento.</summary>
    public Guid OrigenId { get; set; }

    /// <summary>El día bloqueado.</summary>
    public DateOnly Fecha { get; set; }

    /// <summary>Cuánto ocupa: <see cref="TipoOcupacionDia"/>.</summary>
    public TipoOcupacionDia Tipo { get; set; }

    /// <summary>Si esta fila bloquea de verdad. False = liberada, pero se conserva.</summary>
    public bool Bloquea { get; set; }

    public DateTimeOffset CreadoAt { get; set; }
}