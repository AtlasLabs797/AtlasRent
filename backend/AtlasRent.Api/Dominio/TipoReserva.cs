namespace AtlasRent.Api.Dominio;

/// <summary>
/// Clase de reserva (docs/ESPECIFICACION.md §3.4). Solo dos, y no son intercambiables: lo que
/// bloquea y lo que se guarda es distinto en cada una.
/// <list type="bullet">
/// <item>
/// <see cref="Mensual"/> bloquea la unidad en un <b>rango abierto</b> desde la entrada prevista
/// (<c>ocupacion_rango.fecha_fin</c> es nulo) y no lleva días.
/// </item>
/// <item>
/// <see cref="Corta"/> bloquea días sueltos (<c>ocupacion_dia</c>) y lleva un
/// <c>ReservaDia</c> por día, con su hora de entrada y de salida.
/// </item>
/// </list>
/// <para>
/// Enum mapeado a entero, como el resto del dominio. No hay más tipos: un alquiler por horas no
/// existe (D11).
/// </para>
/// </summary>
public enum TipoReserva
{
    /// <summary>Reserva mensual, antes del contrato.</summary>
    Mensual = 1,

    /// <summary>Reserva corta: medio día o día completo (D11).</summary>
    Corta = 2,
}