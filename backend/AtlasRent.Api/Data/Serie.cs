using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Serie de numeración de facturas (docs/ESPECIFICACION.md §3.7). Cada serie tiene su contador
/// propio: el número visible es el de la serie más el contador, y el contador solo lo mueve
/// <see cref="Services.ServicioSeries"/> con un <c>UPDATE ... RETURNING</c> dentro de la
/// transacción de emisión (docs/DOC_TECNICA.md §4.3).
/// </summary>
public sealed class Serie : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    /// <summary>Número visible de la serie. La especificación fija las iniciales: 1, 2, 3, 4 y 9.</summary>
    public int Numero { get; set; }

    /// <summary>
    /// Prefijo del número emitido. Nulo en las series que nacen de la especificación, que da los
    /// números pero no los prefijos.
    /// <para>
    /// TODO(A#): ¿qué prefijos imprime la empresa? La especificación §3.7 solo fija los números
    /// de serie (1 USD, 2 DOP, 3 fiscal, 4 proforma, 9 histórica).
    /// </para>
    /// </summary>
    public string? Prefijo { get; set; }

    /// <summary>Moneda fija de la serie. Nula = moneda libre (D7).</summary>
    public Moneda? Moneda { get; set; }

    public TipoSerie Tipo { get; set; }

    /// <summary>
    /// Nombre de la plantilla de PDF de esta serie (<c>docs/ESPECIFICACION.md</c> §3.7), del
    /// catálogo de <see cref="Services.RegistroPlantillasPdf"/>. Nulo = la de por defecto.
    /// <para>
    /// Se valida al guardar la serie (F4-01): un nombre que no existe se rechaza con 400 en vez de
    /// imprimir el documento con otro diseño del que nadie se enteró.
    /// </para>
    /// </summary>
    public string? PlantillaPdf { get; set; }

    /// <summary>
    /// Condiciones de pago que se imprimen en el pie del documento de esta serie (F4-01,
    /// <c>docs/DESIGN.md</c> §7). Texto de la empresa, escrito como texto plano.
    /// <para>
    /// Es por serie y no global porque las series no se pagan igual: una factura a 30 días y una al
    /// contado son documentos distintos aunque el diseño sea el mismo.
    /// </para>
    /// </summary>
    public string? CondicionesPago { get; set; }


    /// <summary>Suma a los totales de facturado (docs/ESPECIFICACION.md §3.15).</summary>
    public bool SumaAFacturado { get; set; }

    /// <summary>Genera deuda: entra en el saldo pendiente.</summary>
    public bool SumaASaldos { get; set; }

    /// <summary>Emite también comprobante fiscal. Solo tiene sentido en una serie de tipo fiscal.</summary>
    public bool GeneraComprobante { get; set; }

    /// <summary>Contador: el número que se emite a continuación. Solo lo mueve el contador transaccional.</summary>
    public int Siguiente { get; set; }

    /// <summary>
    /// Serie desactivada: no emite números. No se borra, porque sus números emitidos hacen falta
    /// para siempre (AGENTS.md §6).
    /// </summary>
    public bool Activa { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}