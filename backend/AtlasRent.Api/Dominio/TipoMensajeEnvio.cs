namespace AtlasRent.Api.Dominio;

/// <summary>
/// Qué plantilla de mensaje (<c>plantilla_mensaje</c>) compone el correo (docs/ESPECIFICACION.md
/// §3.14). Son las cinco clases de mensaje que la especificación nombra.
/// <para>
/// No está aquí el contrato: §3.14 manda el PDF del contrato por el mismo camino que el resto, pero
/// el contrato no lleva una plantilla de mensaje propia y su correo lo compone quien lo llama.
/// </para>
/// </summary>
public enum TipoMensajeEnvio
{
    /// <summary>Cotización enviada al cliente.</summary>
    Cotizacion = 1,

    /// <summary>Factura o proforma al cliente. Cubre las dos porque viven en la misma tabla.</summary>
    Factura = 2,

    /// <summary>Aviso de vencimiento (§3.16). Lo encola el trabajo de W5, no una pantalla.</summary>
    Recordatorio = 3,

    /// <summary>Recibo de un cobro.</summary>
    Recibo = 4,

    /// <summary>Estado de cuenta del cliente (§3.13).</summary>
    EstadoCuenta = 5,
}