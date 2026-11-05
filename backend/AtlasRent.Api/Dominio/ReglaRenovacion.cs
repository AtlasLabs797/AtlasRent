namespace AtlasRent.Api.Dominio;

/// <summary>
/// Regla de renovación automática de un contrato (docs/ESPECIFICACION.md §3.5: "por bloques
/// (6 meses, 1 año u otro) hasta cancelación").
/// <para>
/// <b>No hay una opción "sin renovación".</b> La especificación da la renovación por obligatoria
/// ("regla y duración de renovación" entre los obligatorios) y dice que se repite hasta la
/// cancelación, que en el flujo es la baja programada. Quien no quiera renovar da de baja el
/// contrato, no marca una regla que lo congelaría con una fecha de fin caducada.
/// <para>
/// Los valores numéricos son los que guarda la columna y no se reordenan.
/// </para>
/// </summary>
public enum ReglaRenovacion
{
    /// <summary>Bloques de un mes.</summary>
    Mensual = 1,

    /// <summary>Bloques de tres meses.</summary>
    Trimestral = 2,

    /// <summary>Bloques de seis meses.</summary>
    Semestral = 3,

    /// <summary>Bloques de doce meses.</summary>
    Anual = 4,

    /// <summary>
    /// Bloques del tamaño que pacte el contrato, que va en <c>renovacion_meses</c> (la duración que
    /// pide la especificación como obligatoria).
    /// </summary>
    Personalizada = 5,
}