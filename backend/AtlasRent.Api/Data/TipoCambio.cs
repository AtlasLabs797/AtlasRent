using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// El tipo de cambio de un día (F3-01, docs/ESPECIFICACION.md §3.12, D9 y D22).
/// <para>
/// <b>Un valor por día y por moneda, y ese es el índice único.</b> El tipo de cambio solo sirve para
/// consolidar informes y se congela al emitir una factura y al registrar un cobro, así que a lo largo
/// del día el valor es uno: el de la mañana es el que queda. Guardar dos valores del mismo día y la
/// misma moneda haría que dos documentos congelaran números distintos según cuál se leyera primero.
/// </para>
/// <para>
/// <b>No hay fila de USD.</b> El dólar es la base de informes y vale 1 por definición
/// (docs/ESPECIFICACION.md §3.12), así que <c>ServicioTiposCambio.ObtenerAsync</c> lo devuelve sin
/// consultar la base. Guardarlo sería guardar una fila que dice lo mismo siempre y que además
/// admitirse modificar, cuando su valor no depende de nadie.
/// </para>
/// <para>
/// <b>La fuente decide quién puede pisar a quién.</b> Un valor cargado a mano no lo pisa nunca la
/// fuente: es el camino de recuperación que la especificación deja escrito ("si la API falla:
/// alerta y carga manual"), y si la fuente lo sobrescribiera a la mañana siguiente la carga manual
/// no habría arreglado nada. Al revés sí: una fila que vino de la fuente se actualiza con el valor
/// del día cuando el trabajo corre otra vez.
/// </para>
/// <para>
/// Las marcas de quien la cargó son las columnas de trazabilidad de siempre (<c>created_at</c>,
/// <c>created_by</c>), no un par de columnas propio: para una fuente cargada por el trabajo no hay
/// persona, y el trabajo ya escribe su auditoría con <c>usuario_id</c> nulo y <c>origen = "job"
/// </c> (docs/DOC_TECNICA.md §8.0). Duplicarlas dejaría dos fechas que pueden no ser la misma.
/// </para>
/// </summary>
public sealed class TipoCambio : IEntidadTrazada
{
    /// <summary>Cuándo y quién cargó el valor. Son las columnas de trazabilidad de siempre.</summary>
    public Guid Id { get; set; }

    /// <summary>Momento de la carga, en UTC. Es la marca de <c>created_at</c>, no la fecha del valor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Quién lo cargó. Nulo cuando lo cargó el trabajo diario: no hay persona detrás de una fuente.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Cuándo se cambió el valor por última vez sin cambiar la fecha.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Quién lo cambió por última vez. Nulo en las filas del trabajo.</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Día de negocio al que se aplica el valor, en la zona de Santo Domingo
    /// (<see cref="TiempoNegocio.Zona"/>). No la fecha en que se cargó: cargar tarde el cambio de
    /// ayer es una operación normal, y por eso la fecha es un dato y no <c>created_at</c>.
    /// </summary>
    public DateOnly Fecha { get; set; }

    /// <summary>
    /// Moneda de la fila. Nunca USD: el dólar es la base y no se guarda.
    /// </summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Cuántas unidades de <see cref="Moneda"/> vale un dólar. <c>numeric(18,6)</c>, seis decimales
    /// porque es lo que dice docs/DOC_TECNICA.md §4.2 y lo que trae <see cref="Redondeo.TipoCambio"/>.
    /// </summary>
    public decimal Valor { get; set; }

    /// <summary>
    /// De dónde salió el valor: <see cref="FuentesTipoCambio.Api"/> o
    /// <see cref="FuentesTipoCambio.Manual"/>. Es texto y no un entero porque es un dato que se lee
    /// tal cual en el histórico y en la auditoría, y porque el histórico tiene que seguir siendo
    /// legible aunque mañana se añada una fuente más.
    /// </summary>
    public string Fuente { get; set; } = string.Empty;
}

/// <summary>
/// Valores de <see cref="TipoCambio.Fuente"/>. Se comparan en el servicio para decidir si la fuente
/// puede pisar la fila, así que no pueden escribirse de otra manera en la columna.
/// </summary>
public static class FuentesTipoCambio
{
    /// <summary>ExchangeRate-API (D22), que es la que el trabajo de las 05:00 consulta.</summary>
    public const string Api = "exchange_rate_api";

    /// <summary>Carga manual de una persona, el camino de recuperación cuando la API falla.</summary>
    public const string Manual = "manual";
}
