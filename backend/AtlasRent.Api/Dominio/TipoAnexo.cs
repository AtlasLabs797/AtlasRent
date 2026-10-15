using System.Text.Json.Serialization;

namespace AtlasRent.Api.Dominio;

/// <summary>
/// Tipo de anexo del contrato (docs/ESPECIFICACION.md §3.5: "Anexos: catálogo, uno activo por
/// tipo"). El tipo es lo que hace único al anexo dentro del catálogo: solo puede haber uno activo de
/// cada tipo, así que un anexo nunca se borra, se desactiva y el siguiente entra en su lugar.
/// <para>
/// La lista es corta a propósito y es el tipo, no el contenido, lo que el catálogo ordena: cambiar
/// el texto de un anexo es editarlo; cambiarle el tipo es darlo de baja y crear otro.
/// </para>
/// <para>
/// TODO(A#) F2-07: el enunciado nombra los anexos ("catálogo, uno activo por tipo") pero no dice
/// cuáles son. Estos cuatro cubren lo que la especificación menciona en otra parte (inventario en
/// §3.5, normas del inmueble en §3.1, mantenimiento) más el cajón de sastre. Si el responsable
/// define otra lista, es añadir valores aquí y en la migración; no es una decisión de código.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoAnexo
{
    /// <summary>Reglas de uso del inmueble (convivencia, horarios, mascotas) que acompañan al contrato.</summary>
    Normas = 1,

    /// <summary>
    /// Relación de lo que se entrega con la unidad. Su contenido lo fija F2-08 al congelar el
    /// inventario del contrato; hasta entonces el anexo es solo un texto que escribe el responsable.
    /// </summary>
    Inventario = 2,

    /// <summary>Cómo se reportan y se atienden las averías durante el alquiler.</summary>
    Mantenimiento = 3,

    /// <summary>Cualquier otro anexo que el responsable considere necesario, tipificado aquí.</summary>
    Otros = 4,
}