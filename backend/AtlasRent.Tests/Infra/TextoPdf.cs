using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// Lee el texto que un PDF de QuestPDF (Skia) <b>imprime de verdad</b>, sin paquetes: inflando los
/// flujos con <see cref="ZLibStream"/> y trayendo cada glifo a Unicode con el CMap <c>ToUnicode</c> de
/// su fuente. Sirve para afirmar sobre el papel y no sobre el modelo que lo alimenta (BUG-051).
/// <para>
/// Solo entiende lo que Skia escribe: fuentes <c>Type0</c> con <c>Identity-H</c> y <c>ToUnicode</c>,
/// operadores <c>Tf</c>, <c>Tj</c> y <c>TJ</c>, y fuentes llamadas <c>F&lt;n&gt;</c> con <c>n</c> igual al
/// número de su objeto. Si cambia la librería y deja de cumplirse, <see cref="Extraer"/> devuelve
/// cadena vacía y las pruebas que lo usan fallan por eso (se les exige un texto conocido), no pasan.
/// </para>
/// </summary>
internal static class TextoPdf
{
    private static readonly Regex Objeto = new(@"(\d+) 0 obj(.*?)endobj", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Flujo = new(@"stream\r?\n(.*?)endstream", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ToUnicode = new(@"/ToUnicode (\d+) 0 R", RegexOptions.Compiled);
    private static readonly Regex Fuente = new(@"/F(\d+) [\d.]+ Tf", RegexOptions.Compiled);
    private static readonly Regex Operador = new(@"/F(\d+) [\d.]+ Tf|\[(.*?)\]\s*TJ|<([0-9A-Fa-f]*)>\s*Tj", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"<([0-9A-Fa-f]*)>", RegexOptions.Compiled);
    private static readonly Regex BfChar = new(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", RegexOptions.Compiled);
    private static readonly Regex BfRango = new(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", RegexOptions.Compiled);

    /// <summary>El texto del documento, una cadena de TJ/Tj por línea.</summary>
    public static string Extraer(byte[] pdf)
    {
        var crudo = Encoding.Latin1.GetString(pdf);
        var objetos = new Dictionary<int, string>();
        var flujos = new Dictionary<int, string>();

        foreach (Match o in Objeto.Matches(crudo))
        {
            var n = int.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture);
            objetos[n] = o.Groups[2].Value;
            var f = Flujo.Match(o.Groups[2].Value);
            if (f.Success)
            {
                flujos[n] = Inflar(Encoding.Latin1.GetBytes(f.Groups[1].Value));
            }
        }

        var cmaps = new Dictionary<int, Dictionary<int, string>>();
        foreach (var (n, cuerpo) in objetos)
        {
            var t = ToUnicode.Match(cuerpo);
            if (t.Success && flujos.TryGetValue(int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture), out var cmap))
            {
                cmaps[n] = LeerCmap(cmap);
            }
        }

        var texto = new StringBuilder();
        foreach (var contenido in flujos.Values.Where(c => c.Contains(" Tf", StringComparison.Ordinal)))
        {
            Dictionary<int, string>? actual = null;
            foreach (Match m in Operador.Matches(contenido))
            {
                if (m.Groups[1].Success)
                {
                    cmaps.TryGetValue(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), out actual);
                    continue;
                }

                var trozos = m.Groups[2].Success
                    ? Hex.Matches(m.Groups[2].Value).Select(h => h.Groups[1].Value)
                    : [m.Groups[3].Value];
                foreach (var trozo in trozos)
                {
                    for (var i = 0; i + 4 <= trozo.Length; i += 4)
                    {
                        var glifo = int.Parse(trozo.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        texto.Append(actual is not null && actual.TryGetValue(glifo, out var c) ? c : "\uFFFD");
                    }
                }

                texto.Append('\n');
            }
        }

        return texto.ToString();
    }

    /// <summary>Quita todo el espacio en blanco: el texto largo se parte en líneas al maquetar.</summary>
    public static string SinEspacios(string texto) => Regex.Replace(texto, @"\s+", "");

    private static string Inflar(byte[] datos)
    {
        try
        {
            using var entrada = new MemoryStream(datos);
            using var zlib = new ZLibStream(entrada, CompressionMode.Decompress);
            using var salida = new MemoryStream();
            zlib.CopyTo(salida);
            return Encoding.Latin1.GetString(salida.ToArray());
        }
        catch (InvalidDataException)
        {
            return Encoding.Latin1.GetString(datos);
        }
    }

    private static Dictionary<int, string> LeerCmap(string cmap)
    {
        var mapa = new Dictionary<int, string>();
        foreach (Match bloque in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
        {
            foreach (Match p in BfChar.Matches(bloque.Groups[1].Value))
            {
                mapa[Convert.ToInt32(p.Groups[1].Value, 16)] = Utf16(p.Groups[2].Value);
            }
        }

        foreach (Match bloque in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
        {
            foreach (Match r in BfRango.Matches(bloque.Groups[1].Value))
            {
                var desde = Convert.ToInt32(r.Groups[1].Value, 16);
                var hasta = Convert.ToInt32(r.Groups[2].Value, 16);
                var destino = Convert.ToInt32(r.Groups[3].Value, 16);
                for (var g = desde; g <= hasta; g++)
                {
                    mapa[g] = char.ConvertFromUtf32(destino + g - desde);
                }
            }
        }

        return mapa;
    }

    private static string Utf16(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 4 <= hex.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
        }

        return sb.ToString();
    }
}
