using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// SEC-001 y <c>docs/DESIGN.md §7</c>: los PDF se generan por código, nunca interpretando marcado.
/// <para>
/// Con HTML y un navegador, un concepto de línea que dijera <c>&lt;script&gt;</c> ejecutaba código
/// y podía hacer peticiones a la red interna. Con QuestPDF hay una tentación más sutil y más
/// fácil: la librería trae <c>Html()</c> y <c>Svg()</c>, que son cómodos para escribir texto con
/// formato y para pegar un gráfico, y bastarían para devolver el problema entero. El texto de
/// usuario (conceptos, nombre del cliente, notas) tiene que salir como texto plano.
/// </para>
/// <para>
/// Por eso la regla no es «no escribas scripts», sino «ningún compositor de PDF usa el intérprete
/// de HTML ni el de SVG». Es la misma forma que la guarda de cifras fiscales de
/// <c>CifrasFiscalesEnCodigoTests</c>: se mira el código de la aplicación y no lo que el
/// administrador escribió.
/// </para>
/// <para>
/// Las cadenas y los comentarios no son código, así que no cuentan: el nombre de un método o una
/// nota que hable de HTML no activan nada.
/// </para>
/// </summary>
public sealed class InterpretacionEnPdfEnCodigoTests
{
    /// <summary>
    /// Cómo se llama al intérprete de QuestPDF. Se busca el nombre del método con sus dos
    /// paréntesis, que es como lo escribe el código, y no la palabra suelta: <c>Html(</c> aparece
    /// en un <c>using</c> o en un comentario sin que nadie interpete nada.
    /// </summary>
    private static readonly Regex Interprete = new(
        @"\b(Html|Svg)\s*\(|\.ParseHtml\b|\bMinifyHtml\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Cadenas y comentarios, que no son código.</summary>
    private static readonly Regex NoCodigo = new(
        @"//.*$|/\*.*?\*/|""(?:[^""\\]|\\.)*""",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Solo los de <c>Services</c>: los PDF se componen ahí. Se excluyen <c>bin</c> y <c>obj</c> para no
    /// leer copias.
    /// </summary>
    private static IEnumerable<string> Compositores()
    {
        var raiz = Path.Combine(RutaProyectoApi(), "Services");

        return Directory
            .EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(ruta => !ruta.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(ruta => !ruta.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>Sube desde el directorio de salida del test hasta <c>backend/AtlasRent.Api</c>.</summary>
    private static string RutaProyectoApi()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null)
        {
            var api = Path.Combine(directorio.FullName, "AtlasRent.Api");
            if (File.Exists(Path.Combine(api, "AtlasRent.Api.csproj")))
            {
                return api;
            }

            directorio = directorio.Parent;
        }

        throw new InvalidOperationException("No se encuentra el proyecto AtlasRent.Api desde " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task NingunCompositorDePdfUsaElInterpreteDeHtmlNiElDeSvg()
    {
        var ct = TestContext.Current.CancellationToken;
        var infracciones = new List<string>();

        foreach (var archivo in Compositores())
        {
            var numeroLinea = 0;
            foreach (var linea in await File.ReadAllLinesAsync(archivo, ct))
            {
                numeroLinea++;
                var codigo = NoCodigo.Replace(linea, string.Empty);

                if (Interprete.IsMatch(codigo))
                {
                    infracciones.Add($"{Path.GetFileName(archivo)}:{numeroLinea}: {linea.Trim()}");
                }
            }
        }

        infracciones.Should().BeEmpty(
            "los PDF se componen por código con texto plano; el intérprete de HTML o de SVG de "
                + "QuestPDF devuelve el problema de SEC-001");
    }
}
