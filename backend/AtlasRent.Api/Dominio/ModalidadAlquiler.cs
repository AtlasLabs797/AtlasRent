using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Modalidades de alquiler que una unidad puede ofrecer (docs/ESPECIFICACION.md §3.1 y decisión
/// D11). Solo tres: mensual, medio día y día completo; no hay franjas fijas ni alquiler por horas.
/// <para>
/// Una unidad <strong>solo</strong> ofrece las modalidades que tiene permitidas, y su tarifa se
/// elige por modalidad y por moneda. Por eso son un enum cerrado y no texto libre.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModalidadAlquiler
{
    Mensual = 1,
    MedioDia = 2,
    DiaCompleto = 3,
}