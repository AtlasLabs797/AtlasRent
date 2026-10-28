using System.Globalization;
using System.Text.Json;

/// <summary>
/// La API escribe los decimales como string (AGENTS.md §6); las pruebas leen los dos formatos para
/// no depender de si la fuente es la API o un JSON interno.
/// </summary>
public static class ExtensionesJson
{
    public static decimal LeerDecimal(this JsonElement elemento) =>
        elemento.ValueKind == JsonValueKind.String
            ? decimal.Parse(elemento.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture)
            : elemento.GetDecimal();
}
