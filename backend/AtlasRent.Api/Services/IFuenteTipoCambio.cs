using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Services;

/// <summary>
/// De dónde sale el valor del día (F3-01, D22). Es una interfaz y no el cliente HTTP directo por la
/// misma razón que <see cref="IRegistroAlertas"/>: las pruebas del criterio de aceptación —"si la
/// API falla hay alerta y carga manual"— no pueden depender de que el día que se ejecuten la API de
/// verdad esté en pie.
/// </summary>
public interface IFuenteTipoCambio
{
    /// <summary>
    /// El valor de <paramref name="moneda"/> de hoy, o <see cref="FalloTipoCambioException"/> si no
    /// se pudo saber. <see cref="Moneda.USD"/> se resuelve sin llamar a nadie: es la base.
    /// </summary>
    Task<decimal> ObtenerAsync(Moneda moneda, CancellationToken ct);
}

/// <summary>
/// La fuente no respondió o respondió algo que no es un tipo de cambio.
/// <para>
/// Es una excepción propia y no un <c>HttpRequestException</c> suelto porque el trabajo diario la
/// distingue de lo demás: su mensaje se copia tal cual a la alerta, que es texto que alguien va a
/// leer, y el de una excepción de red puede traer la URL con la clave o detalles internos. La
/// diferencia es exactamente la que separa "dime qué pasó, escríbelo" de "anota el tipo de error".
/// </para>
/// </summary>
public sealed class FalloTipoCambioException(string motivo) : Exception(motivo);
