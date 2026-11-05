using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Unidad: lo alquilable (docs/ESPECIFICACION.md §3.1). Contratos y reservas apuntan a una unidad,
/// nunca a un nombre suelto.
/// <para>
/// <see cref="EstadoManual"/> es lo que alguien pone a mano; ocupada y reservada se calculan por
/// fechas y no viven aquí (AGENTS.md §6).
/// </para>
/// </summary>
public sealed class Unidad : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid InmuebleId { get; set; }

    /// <summary>Nombre o número de la unidad dentro del inmueble.</summary>
    public string Nombre { get; set; } = string.Empty;

    public TipoUnidad Tipo { get; set; }

    /// <summary>Subempresa propietaria. Hereda la del inmueble si no se indica otra.</summary>
    public Guid? PropietariaId { get; set; }

    /// <summary>Subempresa que factura por defecto en los contratos de esta unidad.</summary>
    public Guid? FacturadoraPorDefectoId { get; set; }

    /// <summary>
    /// Estado manual (disponible / mantenimiento / fuera de servicio). No dice si está ocupada.
    /// </summary>
    public EstadoUnidad EstadoManual { get; set; }

    /// <summary>Modalidades que esta unidad ofrece. Fuera de esta lista no se puede contratar ni ponerle tarifa.</summary>
    public List<ModalidadUnidad> Modalidades { get; set; } = [];

    /// <summary>Tarifa por modalidad y moneda.</summary>
    public List<TarifaUnidad> Tarifas { get; set; } = [];

    /// <summary>
    /// Inventario base de la unidad (F2-08, §3.1): lo que hay aquí, vivo y corregible. Opcional y sin
    /// fotos. El contrato que se firme sobre esta unidad se lleva su <b>copia congelada</b>
    /// (<c>ContratoInventarioItem</c>), que no se actualiza aunque esto se cambie después.
    /// </summary>
    public List<InventarioBaseItem> InventarioBase { get; set; } = [];

    // Opcionales: no todos los espacios los tienen.
    public decimal? MetrosCuadrados { get; set; }

    public int? Capacidad { get; set; }

    public int? Habitaciones { get; set; }

    /// <summary>Baños con medios: 1,5 para un aseo y un medio.</summary>
    public decimal? Banos { get; set; }

    /// <summary>Medidor o contador que lleva la unidad, para el mantenimiento.</summary>
    public string? Medidor { get; set; }

    public string? Notas { get; set; }

    /// <summary>Baja lógica.</summary>
    public bool Activo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}