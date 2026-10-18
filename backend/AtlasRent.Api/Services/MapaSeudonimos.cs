using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtlasRent.Api.Services;

/// <summary>
/// Seudonimiza los datos personales antes de que salgan del proceso y los vuelve a poner al
/// volver (F6-02).
/// <para>
/// El ciclo es: <c>Anonimizar(datos)</c> → el proveedor solo ve <c>[[cliente_1]]</c> →
/// <c>Reidentificar(respuesta)</c> → el usuario vuelve a ver el nombre real. El proveedor nunca
/// vio el nombre, y el usuario nunca ve el seudónimo.
/// </para>
/// <para>
/// <b>El mapa vive solo en la memoria de esta instancia y de esta petición.</b> No se persiste, no
/// va al log y no va al <c>datos_json</c> de la auditoría: un mapa de seudónimos guardado es un
/// mapa de datos personales guardado con otro nombre.
/// </para>
/// <para>
/// <b>La lista de propiedades es explícita, no por reflexión.</b> Una reflexión ciega sobre el DTO
/// acabaría seudonimizando también los importes o los estados —rompería el cuadre con la vista
/// única— y dejaría fuera cualquier propiedad nueva que nadie se acordara de añadir. Aquí una
/// propiedad entra en el mapa porque alguien la escribió en <see cref="Prefijos"/> y lo revisó.
/// </para>
/// </summary>
public sealed class MapaSeudonimos
{
    /// <summary>
    /// Propiedad del dato → prefijo del seudónimo. <b>Es la lista de cierre de lo que sale
    /// seudonimizado</b>: lo que no está aquí sale en claro, así que añadir una propiedad personal
    /// a un DTO de lectura exige añadirla aquí en el mismo cambio.
    /// <para>
    /// Los prefijos son cortos a propósito (<c>[[cliente_1]]</c>, no un UUID): el modelo los lee y
    /// los copia, y los corchetes son lo que hace que sobrevivan al viaje de ida y vuelta sin que
    /// el proveedor los mezcle con su propia notación.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Prefijos = new(StringComparer.OrdinalIgnoreCase)
    {
        // Persona o empresa.
        ["nombre"] = "cliente",
        ["cliente"] = "cliente",
        ["clienteNombre"] = "cliente",
        ["nombreContacto"] = "cliente",
        ["razonSocial"] = "cliente",
        // Contacto.
        ["email"] = "mail",
        ["telefono"] = "tel",
        ["direccion"] = "dir",
        // Identidad.
        ["documento"] = "doc",
        // Texto libre que alguien escribió sobre una persona.
        ["concepto"] = "conc",
        ["notas"] = "nota",
    };

    private readonly Dictionary<string, string> _porValor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _porSeudonimo = new(StringComparer.Ordinal);
    private int _indice;

    /// <summary>Número de valores distintos que se han seudonimizado. Para pruebas y diagnóstico.</summary>
    public int Cuantos => _porValor.Count;

    /// <summary>
    /// Recorre un JSON ya construido y cambia por seudónimo el valor de las propiedades de la lista.
    /// Devuelve el JSON nuevo: el original no se toca, así que el mapa no puede «envejecer» un
    /// objeto que otro ya tiene entre manos.
    /// </summary>
    public string Anonimizar(JsonNode? nodo)
    {
        var copia = nodo?.DeepClone();
        Recorrer(copia);
        return copia?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "{}";
    }

    /// <summary>
    /// Devuelve la respuesta con los seudónimos sustituidos por los valores reales. Si el modelo
    /// inventó un seudónimo que no existe, se queda tal cual: es texto del modelo y no hay a qué
    /// sustituirlo.
    /// </summary>
    public string Reidentificar(string texto)
    {
        var resultado = texto;

        // Del seudónimo más largo al más corto: sin esto, al sustituir [[cliente_1]] se comería el
        // principio de [[cliente_11]].
        foreach (var (seudonimo, original) in _porSeudonimo.OrderByDescending(p => p.Key.Length))
        {
            resultado = resultado.Replace(seudonimo, original, StringComparison.Ordinal);
        }

        return resultado;
    }

    /// <summary>
    /// Sustituye en un texto libre los valores que el mapa ya conoce. Se usa sobre la pregunta del
    /// usuario, que puede traer un nombre escrito a mano: sin esto, «¿cuánto debe Ana Pérez?»
    /// mandaría ese nombre al proveedor.
    /// <para>
    /// Solo conoce los valores de las filas cargadas para este alcance, no el padrón entero: un
    /// nombre que no está en esa página sí sale. Es el límite que se documenta en DOC_TECNICA §7 y
    /// no se disimula.
    /// </para>
    /// </summary>
    public string AnonimizarTexto(string texto)
    {
        var resultado = texto;

        foreach (var (valor, seudonimo) in _porValor.OrderByDescending(p => p.Key.Length))
        {
            if (resultado.Contains(valor, StringComparison.OrdinalIgnoreCase))
            {
                resultado = resultado.Replace(valor, seudonimo, StringComparison.OrdinalIgnoreCase);
            }
        }

        return resultado;
    }

    private void Recorrer(JsonNode? nodo)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var propiedad in objeto.Select(par => par.Key).ToArray())
                {
                    var valor = objeto[propiedad];

                    if (Prefijos.TryGetValue(propiedad, out var prefijo) && valor is JsonValue { } texto)
                    {
                        var original = texto.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(original))
                        {
                            objeto[propiedad] = Seudonimo(prefijo, original);
                        }
                    }
                    else
                    {
                        Recorrer(valor);
                    }
                }

                break;

            case JsonArray lista:
                foreach (var elemento in lista)
                {
                    Recorrer(elemento);
                }

                break;
        }
    }

    /// <summary>
    /// El mismo valor con dos nombres de propiedad distintos —el nombre del cliente en
    /// <c>facturas</c> y su texto en <c>concepto</c>, por ejemplo— sale con el seudónimo del
    /// primero que lo vio, para que el modelo lo reconozca como la misma persona. La numeración es
    /// global, no por prefijo, para que dos prefijos distintos no puedan producir el mismo texto:
    /// <c>[[cliente_1]]</c> es una persona y <c>[[doc_2]]</c> es su documento.
    /// </summary>
    private string Seudonimo(string prefijo, string original)
    {
        if (_porValor.TryGetValue(original, out var existente))
        {
            return existente;
        }

        var seudonimo = $"[[{prefijo}_{++_indice}]]";
        _porValor[original] = seudonimo;
        _porSeudonimo[seudonimo] = original;

        return seudonimo;
    }
}