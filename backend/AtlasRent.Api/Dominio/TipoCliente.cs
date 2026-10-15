namespace AtlasRent.Api.Dominio;

/// <summary>
/// Naturaleza del cliente (docs/ESPECIFICACION.md §3.2). Decide si el documento de identidad es
/// personal (cédula o pasaporte) o de empresa (RNC), y de qué forma se valida.
/// </summary>
public enum TipoCliente
{
    PersonaFisica = 1,
    Empresa = 2,
}