namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estados de una cotización (docs/ESPECIFICACION.md §3.3, D18): <c>Borrador → Enviada → Aceptada
/// / Rechazada</c>.
/// <para>
/// Los valores numéricos son los que guarda la columna, como el resto de enums del dominio, y no se
/// reordenan: los lee también el SQL de la migración.
/// </para>
/// </summary>
public enum EstadoCotizacion
{
    /// <summary>Se está redactando. Es el único estado en el que se puede editar o dar de baja.</summary>
    Borrador = 1,

    /// <summary>Enviada al cliente. Ya no se edita: lo que se envió es lo que quedó escrito.</summary>
    Enviada = 2,

    /// <summary>El cliente la aceptó. Desde aquí se pueden copiar los datos a una reserva o a un contrato.</summary>
    Aceptada = 3,

    /// <summary>El cliente la rechazó.</summary>
    Rechazada = 4,
}