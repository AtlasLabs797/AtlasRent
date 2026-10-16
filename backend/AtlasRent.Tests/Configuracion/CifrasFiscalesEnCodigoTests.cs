using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// D14: las cifras fiscales (tipos de ITBIS, porcentajes de retención, rangos de comprobante) son
/// datos y se configuran en pantalla. En el código de la aplicación no puede haber ninguna.
/// <para>
/// La comprobación no busca "números raros": en una línea que habla de ITBIS, retención o
/// porcentaje no puede aparecer una cifra, salvo los ceros, los unos y los valores de enumeración.
/// Los tamaños de campo (<c>numeric(18,4)</c>) van dentro de comillas y no cuentan, y el sufijo
/// <c>m</c> de un literal decimal tampoco se esconde. Una constante fiscal con nombre innocento
/// (<c>ItbisGeneral</c>, <c>RetencionPais</c>) cae también, porque la regla mira la línea entera.
/// </para>
/// <para>
/// Y el valor que usa el resto de pruebas no sale de leer el código: sale de que la API devuelva lo
/// que el administrador guardó (ver <see cref="TiposImpuestoTests"/>).
/// </para>
/// </summary>
public sealed class CifrasFiscalesEnCodigoTests
{
    /// <summary>
    /// Palabras que delatan una decisión fiscal. Van sin límite al final a propósito: una constante
    /// fiscal escondida detrás de un nombre innocento (<c>ItbisGeneral</c>, <c>RetencionPais</c>)
    /// tiene que caer también, y por eso el prefijo basta.
    /// <para>
    /// "IVA" no está en la lista a propósito: en el código aparece dentro de "activar" y
    /// "desactivar", que no tienen nada que ver con impuestos.
    /// </para>
    /// </summary>
    private static readonly Regex Fiscal = new(
        @"\b(itbis|retenci|porcentaj)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Cadenas y comentarios, que no son código: un "18 %" en un comentario no configura nada.</summary>
    private static readonly Regex NoCodigo = new(
        @"//.*$|/\*.*?\*/|""(?:[^""\\]|\\.)*""",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Números que no son una decisión fiscal: ceros, unos, y los valores de enumeración.</summary>
    private static readonly HashSet<string> Permitidos = new(StringComparer.Ordinal)
    {
        "0", "1", "2", "3", "4", "0.0", "1.0", "0.00", "1.00",
        // F3-04: el ordinal de TipoLineaFactura.Retencion (§3.9). Es el siguiente valor libre de la
        // enumeración, no un tipo de impuesto: los valores de enumeración nunca se renumeran y por eso
        // no pueden derivarse de nada que este test pueda cambiar.
        "8",
    };

    /// <summary>
    /// Literal numérico de C#, con el sufijo opcional (<c>18</c>, <c>18m</c>, <c>1.5m</c>). El
    /// sufijo se cuela sin este patrón, que es justo lo que esconde una constante fiscal.
    /// </summary>
    private static readonly Regex LiteralNumerico = new(
        @"(?<![\w.])(\d+(?:[.,]\d+)?)[mdfMDF]?(?![\w.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task ElCodigoDeLaAplicacionNoTieneCifrasFiscales()
    {
        var ct = TestContext.Current.CancellationToken;
        var infracciones = new List<string>();

        foreach (var archivo in ArchivosDeLaApi())
        {
            var numeroLinea = 0;
            foreach (var linea in await File.ReadAllLinesAsync(archivo, ct))
            {
                numeroLinea++;
                var codigo = NoCodigo.Replace(linea, string.Empty);

                if (!Fiscal.IsMatch(codigo))
                {
                    continue;
                }

                foreach (Match numero in LiteralNumerico.Matches(codigo))
                {
                    var valor = numero.Groups[1].Value;
                    if (!Permitidos.Contains(valor))
                    {
                        infracciones.Add(
                            $"{Path.GetFileName(archivo)}:{numeroLinea}: '{valor}' junto a un concepto fiscal. {linea.Trim()}");
                    }
                }
            }
        }

        infracciones.Should().BeEmpty(
            """
            una cifra fiscal escrita en el código. Los porcentajes de ITBIS y retención se
            configuran en /api/v1/config (D14) y no viven en el fuente
            """);
    }

    /// <summary>
    /// El cálculo no puede tener un porcentaje propio: solo multiplica por el que le pasan.
    /// </summary>
    [Fact]
    public async Task ElCalculoNoAportaNingunPorcentajePropio()
    {
        var ct = TestContext.Current.CancellationToken;
        var calculo = Path.Combine(RutaProyectoApi(), "Dominio", "CalculoImpuestos.cs");

        var numeros = new List<string>();
        var numeroLinea = 0;
        foreach (var linea in await File.ReadAllLinesAsync(calculo, ct))
        {
            numeroLinea++;
            var codigo = NoCodigo.Replace(linea, string.Empty);

            foreach (Match numero in LiteralNumerico.Matches(codigo))
            {
                var valor = numero.Groups[1].Value;

                // El 100 es el divisor de un porcentaje, no un tipo de impuesto.
                if (!Permitidos.Contains(valor) && valor != "100")
                {
                    numeros.Add($"CalculoImpuestos.cs:{numeroLinea}: '{valor}'");
                }
            }
        }

        numeros.Should().BeEmpty("el cálculo solo reparte porcentajes que le llegan de la configuración");
    }

    private static IEnumerable<string> ArchivosDeLaApi()
    {
        var raiz = RutaProyectoApi();

        return Directory
            .EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(ruta => !ruta.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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

        throw new InvalidOperationException(
            "No se encuentra backend/AtlasRent.Api (AtlasRent.Api.csproj).");
    }
}