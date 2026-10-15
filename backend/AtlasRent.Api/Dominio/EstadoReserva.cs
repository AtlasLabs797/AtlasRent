namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estados de una reserva (docs/ESPECIFICACION.md §3.4): Activa → Convertida (en contrato) |
/// Completada (corta) | Cancelada | En conflicto.
/// <para>
/// Enum mapeado a entero, como el resto del dominio. Ojo al reordenar valores: esta tabla guarda
/// números, no nombres.
/// </para>
/// </summary>
public enum EstadoReserva
{
    /// <summary>Reserva viva: bloquea la unidad.</summary>
    Activa = 1,

    /// <summary>Se convirtió en contrato (lo pone el servicio de contratos, F2-05).</summary>
    Convertida = 2,

    /// <summary>La corta se usó y terminó (la cierra la operación, F2-04).</summary>
    Completada = 3,

    /// <summary>Baja: el bloqueo se libera y la fila se conserva (AGENTS.md §6).</summary>
    Cancelada = 4,

    /// <summary>
    /// Choca con un contrato vigente: el cliente tiene que buscar otra unidad. La pone el servicio
    /// de contratos al activar el contrato (F2-05), llamando a
    /// <c>ServicioReservas.MarcarEnConflictoAsync</c>.
    /// <para>
    /// Este método no suelta el candado. Mientras el contrato no esté activo, la reserva en conflicto
    /// sigue bloqueando, que es lo que dice la especificación: el conflicto se ve, no se pierde el
    /// sitio. Cuando el contrato se activa, el candado pasa al contrato y el de la reserva se apaga,
    /// porque dos candados vivos que se solapan no pueden existir.
    /// </para>
    /// </summary>
    EnConflicto = 5,
}