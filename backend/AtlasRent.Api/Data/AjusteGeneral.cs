using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Ajustes generales de la casa: tolerancias de diferencia de cambio y modo de recordatorios
/// (docs/ESPECIFICACION.md §3.10.2 y §3.14, D12). Es una fila sola.
/// <para>
/// Las tolerancias nacen nulas: la especificación da 1,00 USD y 50,00 DOP como <em>supuesto</em>, y
/// un supuesto no es una configuración (AGENTS.md §5).
/// <para>
/// TODO(A#): confirmar con el administrador si los valores por defecto de la tolerancia son 1,00 USD
/// y 50,00 DOP, y qué hace el código de cobros con una diferencia sin tolerancia fijada.
/// </para>
/// </para>
/// </summary>
public sealed class AjusteGeneral : IEntidadConfiguracion
{
    public Guid Id { get; set; }

    /// <summary>Tolerancia de diferencia de cambio en USD. Nula si el administrador no la ha fijado.</summary>
    public decimal? ToleranciaCambioUsd { get; set; }

    /// <summary>Tolerancia de diferencia de cambio en DOP. Nula si el administrador no la ha fijado.</summary>
    public decimal? ToleranciaCambioDop { get; set; }

    /// <summary>Automático, manual o apagado (D12). Nace apagado: nadie envía mensajes sin querer.</summary>
    public ModoRecordatorios ModoRecordatorios { get; set; }

    /// <summary>
    /// Días desde el vencimiento hasta el primer aviso (§3.14). Nace en 5, que es el supuesto que
    /// escribe la especificación, y no en el código: si el administrador lo pone a nula el trabajo de
    /// recordatorios no avisa a nadie y deja una alerta diciendo qué configurar (criterio de BUG-007,
    /// un recordatorio con plazos sin decidir es ruido para el cliente).
    /// </summary>
    public int? DiasPrimerRecordatorio { get; set; } = 5;

    /// <summary>
    /// Días entre un aviso y el siguiente mientras siga habiendo saldo. Nace en 15 por el mismo
    /// motivo y con la misma consecuencia de nula que <see cref="DiasPrimerRecordatorio"/>.
    /// </summary>
    public int? DiasEntreRecordatorios { get; set; } = 15;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}