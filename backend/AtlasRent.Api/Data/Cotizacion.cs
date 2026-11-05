using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Cotización (docs/ESPECIFICACION.md §3.3): lo que se ofrece a un cliente o a un prospecto, con
/// sus líneas y su total, antes de que exista una reserva o un contrato.
/// <para>
/// Va a un cliente <b>o</b> a un prospecto, nunca a los dos y nunca a ninguno: es la misma regla que
/// usa la aplicación para validar, reforzada por una restricción <c>CHECK</c> en la tabla.
/// </para>
/// <para>
/// No lleva ITBIS ni retenciones: la especificación solo las pide en facturas (§3.8) y proformas.
/// Tampoco lleva número visible; ver el <c>TODO(A#)</c> de <see cref="CotizacionLinea"/>.
/// </para>
/// </summary>
public sealed class Cotizacion : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Cliente al que se cotiza. Excluyente con <see cref="ProspectoId"/>.</summary>
    public Guid? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>Prospecto al que se cotiza, mientras todavía no es cliente. Excluyente con <see cref="ClienteId"/>.</summary>
    public Guid? ProspectoId { get; set; }

    public Prospecto? Prospecto { get; set; }

    /// <summary>Unidad cotizada, si la cotización es sobre una unidad concreta.</summary>
    public Guid? UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    public EstadoCotizacion Estado { get; set; }

    /// <summary>
    /// Moneda única de todo el documento (D7): no se convierte nada. Si el cliente pide otra moneda
    /// se rehace la cotización (docs/ESPECIFICACION.md §3.8).
    /// </summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Suma de las líneas, redondeada medio hacia arriba. Sin impuestos ni retenciones,
    /// <see cref="Total"/> es también el subtotal.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Líneas del documento. Se reemplazan enteras mientras la cotización está en borrador y a
    /// partir de que se envía quedan congeladas.
    /// </summary>
    public List<CotizacionLinea> Lineas { get; set; } = [];

    /// <summary>Baja lógica: una cotización enviada no se borra nunca (AGENTS.md §6).</summary>
    public bool Activo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}