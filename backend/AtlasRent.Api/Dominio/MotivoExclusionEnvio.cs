using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Por qué un documento que entra en el filtro del envío masivo no se manda (F4-03,
/// docs/ESPECIFICACION.md §3.14).
/// <para>
/// La pantalla de resumen de §3.14 pide los excluidos «con su motivo», y no es adorno: un lote que
/// se recorta sin decir qué se quedó fuera se parece a un lote entero que funcionó. Cada motivo es
/// una razón por la que <b>ese</b> documento no entra, y son disjuntos: se informa del primero que
/// se encuentra y no se acumulan quejas.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotivoExclusionEnvio>))]
public enum MotivoExclusionEnvio
{
    /// <summary>Todavía no se ha calculado ninguno.</summary>
    Ninguno = 0,

    /// <summary>
    /// El documento ya se mandó a ese destinatario. Sale salvo con <c>forzar</c>, igual que en el
    /// envío suelto (§3.14): un envío fallido no bloquea, uno que salió sí.
    /// </summary>
    YaEnviado = 1,

    /// <summary>
    /// Ni el cliente ni ninguno de sus contactos tienen casilla de correo. No hay a quién mandarlo
    /// sin inventar una dirección.
    /// </summary>
    SinDireccionCorreo = 2,

    /// <summary>
    /// El cliente tiene varias casillas de correo y ninguna es el contacto principal, así que no se
    /// sabe a cuál mandarle. <c>TODO(A#)</c>: §3.2 dice que el principal es «el que se usa si no se
    /// elige otro», pero no dice qué hacer cuando hay varios con correo y ninguno principal. Aquí se
    /// excluye en vez de elegir el primero de la lista; si el responsable quiere otro criterio, es una
    /// línea.
    /// </summary>
    SinContactoElegible = 3,

    /// <summary>La factura está anulada (§3.7): mandar una factura anulada es mandar papel muerto.</summary>
    FacturaAnulada = 4,

    /// <summary>
    /// No hay plantilla activa del tipo de mensaje en el idioma pedido. En el envío suelto (F4-02)
    /// esto no es un error y sale en «es»; en un lote de cientos de correos, no: alguien acaba de
    /// pedir que se mande en un idioma y lo que se mandaría sería otro. Se excluye y se dice por
    /// qué. <c>TODO(A#)</c>: si el responsable quiere el mismo «se manda en español» que en el envío
    /// suelto, el motivo desaparece y el resumen tiene que enseñar en qué idioma sale cada uno.
    /// </summary>
    SinPlantilla = 5,

    /// <summary>
    /// La fila no es un documento que se pueda mandar: es una fianza, que en la vista de cuentas
    /// (§3.13) es una fila de dinero, no un papel con PDF.
    /// </summary>
    NoGeneraDocumento = 6,

    /// <summary>
    /// El filtro trae más documentos de los que caben en un lote. Se cuentan y se dicen, nunca se
    /// recortan en silencio: un lote que se manda entero y en realidad no iba entero es peor que uno
    /// que no se manda.
    /// </summary>
    SuperaElTopeDelLote = 7,
}