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

    /// <summary>
    /// Red de seguridad por tipo de dato (SEC-026): una propiedad cuyo nombre contiene alguno de estos
    /// fragmentos se seudonimiza aunque nadie la haya escrito en <see cref="Prefijos"/>, de modo que
    /// una propiedad personal nueva en un DTO no sale en claro por olvido.
    /// </summary>
    private static readonly (string Fragmento, string Prefijo)[] PorTipo =
    [
        ("email", "mail"), ("correo", "mail"),
        ("telefono", "tel"), ("movil", "tel"), ("celular", "tel"),
        ("direccion", "dir"),
        ("documento", "doc"), ("cedula", "doc"), ("rnc", "doc"), ("pasaporte", "doc"),
        ("razonsocial", "cliente"), ("titular", "cliente"),
    ];

    private static readonly System.Text.RegularExpressions.Regex Email =
        new(@"[^\s@""]+@[^\s@""]+\.[^\s@""]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Salida = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
        ComprobarSinFugas(copia);
        return copia?.ToJsonString(Salida) ?? "{}";
    }

    /// <summary>
    /// Última comprobación antes de que el JSON salga: ningún valor personal que el mapa conoce
    /// puede aparecer ya en ningún texto del resultado (en otro campo, dentro de una frase), ni
    /// puede quedar nada con forma de correo. Si aparece, no se envía nada. Se compara sobre los
    /// textos ya des-escapados, no sobre el JSON impreso, donde las tildes salen escapadas (XXXX).
    /// </summary>
    private void ComprobarSinFugas(JsonNode? nodo)
    {
        var textos = new List<string>();
        Textos(nodo, textos);

        foreach (var texto in textos)
        {
            if (Email.IsMatch(texto)
                || _porValor.Keys.Any(valor => valor.Length >= 4
                    && texto.Contains(valor, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ProveedorIaException(
                    "No se envió la consulta: se detectaron datos personales sin seudonimizar.");
            }
        }
    }

    private static void Textos(JsonNode? nodo, List<string> destino)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var (_, hijo) in objeto)
                {
                    Textos(hijo, destino);
                }

                break;
            case JsonArray lista:
                foreach (var hijo in lista)
                {
                    Textos(hijo, destino);
                }

                break;
            case JsonValue valor when valor.TryGetValue<string>(out var texto):
                destino.Add(texto);
                break;
        }
    }

    private static string? PrefijoDe(string propiedad)
    {
        if (Prefijos.TryGetValue(propiedad, out var prefijo))
        {
            return prefijo;
        }

        foreach (var (fragmento, porTipo) in PorTipo)
        {
            if (propiedad.Contains(fragmento, StringComparison.OrdinalIgnoreCase))
            {
                return porTipo;
            }
        }

        return null;
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

                    if (PrefijoDe(propiedad) is { } prefijo && valor is JsonValue { } texto)
                    {
                        // Un número (teléfono guardado como número) o cualquier otro tipo también
                        // se seudonimiza: se pasa a texto en vez de lanzar.
                        var original = texto.TryGetValue<string>(out var cadena)
                            ? cadena
                            : texto.ToJsonString().Trim('"');
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