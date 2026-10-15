using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AtlasRent.Api.Data;

/// <summary>
/// Un bloque de texto con su título: la unidad mínima con la que se escribe una plantilla o un
/// anexo.
/// <para>
/// <b>Contenido de usuario, nunca instrucción.</b> El texto de una plantilla lo escribe un empleado y
/// acaba impreso en un documento que firma un cliente: es texto, no una orden. Por eso el tipo no
/// tiene nada que interpretar (ni HTML, ni marcado, ni plantillas) y solo se escribe con los
/// elementos de texto de QuestPDF. Regla de <c>docs/DESIGN.md §7</c>: "Los textos de usuario
/// (descripciones, notas) se escriben como texto plano; nunca se interpretan como formato", y la de
/// <c>docs/SEGURIDAD.md</c>: nada de lo que escriba un usuario llega al documento como instrucción.
/// </para>
/// </summary>
/// <param name="Titulo">Encabezado del bloque. Vacío si el bloque no lo lleva.</param>
/// <param name="Texto">Cuerpo del bloque, tal cual lo escribió el usuario.</param>
public sealed record SeccionTexto(string Titulo, string Texto);

/// <summary>
/// Un anexo tal y como quedó <b>congelado</b> dentro de un contrato: el tipo, el nombre que tenía en
/// el catálogo y una copia de sus secciones en el momento de aprobar.
/// <para>
/// No es el anexo vivo ni una referencia a él. Si mañana se edita el anexo del catálogo, este
/// registro sigue diciendo lo que decía el día de la firma.
/// </para>
/// </summary>
/// <param name="Tipo">Tipo del anexo en el catálogo.</param>
/// <param name="Nombre">Nombre del anexo el día de la congelación.</param>
/// <param name="Secciones">Copia de sus bloques de texto.</param>
public sealed record AnexoCongelado(TipoAnexo Tipo, string Nombre, List<SeccionTexto> Secciones);

/// <summary>
/// Cómo viaja a la base de datos el texto de las plantillas, los anexos y sus copias congeladas.
/// <para>
/// Va en <c>jsonb</c> y no en una tabla de secciones porque el contenido es un bloque de texto libre
/// que el usuario escribe y el PDF imprime, no algo que la aplicación consulte por partes: normalizar
/// cada bloque en filas daría cuatro tablas y una prosa de joins para volver a juntarlas en el mismo
/// orden en que se leyeron.
/// </para>
/// </summary>
internal static class TextoDePlantilla
{
    /// <summary>
    /// Opciones de serialización. El tipo del anexo va por su nombre y no por su número: el JSON es
    /// una copia que se lee y compara a ojo en una consola, y un <c>"Normas"</c> sobrevive a que
    /// mañana se añada un tipo nuevo al enumerado, mientras que un <c>3</c> significaría otra cosa.
    /// </summary>
    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>Secciones de una plantilla, de un anexo o de una copia congelada.</summary>
    public static readonly ValueConverter<List<SeccionTexto>, string> Secciones = new(
        lista => JsonSerializer.Serialize(lista, Opciones),
        json => JsonSerializer.Deserialize<List<SeccionTexto>>(json, Opciones) ?? new List<SeccionTexto>());

    /// <summary>Anexos congelados dentro de un contrato.</summary>
    public static readonly ValueConverter<List<AnexoCongelado>, string> Anexos = new(
        lista => JsonSerializer.Serialize(lista, Opciones),
        json => JsonSerializer.Deserialize<List<AnexoCongelado>>(json, Opciones) ?? new List<AnexoCongelado>());

    /// <summary>Tipos de anexo que declara necesitar una plantilla.</summary>
    public static readonly ValueConverter<List<TipoAnexo>, string> Tipos = new(
        lista => JsonSerializer.Serialize(lista, Opciones),
        json => JsonSerializer.Deserialize<List<TipoAnexo>>(json, Opciones) ?? new List<TipoAnexo>());
}