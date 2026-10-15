namespace AtlasRent.Api.Dominio;

/// <summary>
/// Monedas del sistema (ESPECIFICACION 3.12): la base de informes es USD y se manejan USD y DOP.
/// Los códigos van en mayúsculas porque son códigos, no identificadores de dominio.
/// </summary>
public enum Moneda
{
    USD = 1,
    DOP = 2,
}