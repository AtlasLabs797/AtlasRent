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
/// <para>
/// <b>La regla anterior solo miraba la línea.</b> Si la línea no hablaba de ITBIS, retención o
/// porcentaje se la saltaba entera, y con ello quedaban fuera justo los campos que
/// <c>docs/DOC_TECNICA.md</c> llama cifras fiscales y afirma que no están en el código: el rango
/// autorizado del numerador (<c>RangoDesde</c>, <c>RangoHasta</c>), su fecha de vencimiento, su
/// umbral de aviso y la tolerancia de diferencia de cambio. El defecto era del mecanismo, no del
/// código: hoy no hay ninguna cifra escrita, pero la guarda no lo estaba viendo.
/// </para>
/// <para>
/// Por eso hay una segunda comprobación, <see cref="LosCamposDeConfiguracionFiscalNoTienenValorPorDefecto"/>,
/// que no busca el número sino el <b>valor por defecto</b> del campo: un <c>= 5m</c> o un
/// <c>= new DateOnly(...)</c> en su declaración, y un campo numérico o de fecha que no lleve
/// <c>required</c>, porque entonces una petición que lo omita se queda con el cero o con el
/// <c>0001-01-01</c> del CLR en vez de con un 400.
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

    /// <summary>
    /// Nombres de los campos de configuración numérica o de fecha que la regla anterior no cubría.
    /// <para>
    /// <c>rango</c>, <c>umbral</c> y <c>tolerancia</c> se buscan por <b>prefijo</b> y
    /// <c>vencimiento</c> por contenido, porque los campos son <c>RangoDesde</c>, <c>RangoHasta</c>,
    /// <c>UmbralAlerta</c>, <c>ToleranciaCambioUsd</c> y <c>FechaVencimiento</c>. El prefijo deja
    /// fuera de la guarda cosas que llevan la palabra «rango» y no son configuración fiscal —<c>ServicioCalendario.DiasMaximoRango</c> es el tope de días de una consulta—: no es
    /// que valgan, es que no son un dato que el administrador escriba.
    /// </para>
    /// </summary>
    private static readonly Regex CampoDeConfiguracion = new(
        @"^(rango|umbral|tolerancia)\w*$|vencimient\w*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Declaración de una propiedad: <c>public [required] Tipo Nombre { get; ...</c>. Exige el
    /// <c>public</c> a propósito, y por eso los parámetros posicionales de un <c>record</c> —que no
    /// llevan modificador y no pueden llevar <c>required</c>— quedan fuera.
    /// </summary>
    private static readonly Regex Propiedad = new(
        @"\bpublic\s+(?<required>required\s+)?(?<tipo>[\w\.\?\[\]<>,]+)\s+(?<nombre>\w+)\s*\{\s*get\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Fecha con literal en un inicializador (<c>= new DateOnly(2026, 1, 1)</c>). Va aparte del
    /// patrón numérico porque sus números van dentro de un constructor y no aparecen como literales
    /// sueltos.
    /// </summary>
    private static readonly Regex LiteralDeFecha = new(
        @"=\s*new\s+(DateOnly|DateTime|DateTimeOffset)\s*\(",
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

    /// <summary>
    /// Los campos de configuración numérica y de fecha no pueden traer un valor por defecto escrito.
    /// <para>
    /// Es la segunda mitad de D14 y cubre lo que la regla por línea no veía: <c>rango*</c>,
    /// <c>umbral*</c>, <c>*vencimiento*</c> y <c>tolerancia*</c>. No busca un número escondido sino
    /// un <b>valor por defecto</b>, y hay dos formas de tenerlo:
    /// </summary>
    /// <list type="bullet">
    /// <item>
    /// Un literal en el inicializador: <c>= 5m</c>, <c>= new DateOnly(2026, 1, 1)</c>. Los ceros y
    /// los unos de <see cref="Permitidos"/> valen aquí igual que en la otra regla: un rango o un
    /// umbral no empiezan en 1 ni valen 0.
    /// </item>
    /// <item>
    /// Un tipo no anulable sin <c>required</c>. No lleva un número escrito, pero tiene un valor por
    /// defecto igual: el del CLR. Si la petición llega sin el campo, se guarda <c>0</c> o
    /// <c>0001-01-01</c> sin que nadie lo haya escrito, que es exactamente lo que la regla quiere
    /// imposible. El <c>[Required]</c> de <c>DataAnnotations</c> no cubre este caso: sobre un
    /// <c>int</c> o un <c>DateOnly</c> no anulable no hace nada en la deserialización, porque no
    /// hay nada que pueda venir a nulo.
    /// </item>
    /// </list>
    /// <para>
    /// Solo se aplica a <c>DTOs/</c>. En <c>Data/</c>, <c>Services/</c> y <c>Dominio/</c> el tipo
    /// no anulable sin <c>required</c> es lo normal —una entidad de EF la rellena la columna y un
    /// DTO de salida la construye el servicio—, y marcarlo sería ruido. Donde el valor por defecto se
    /// cuela de verdad es en el DTO de petición, que es el que rellena el JSON de quien llama.
    /// </para>
    /// <para>
    /// Solo se escande <c>backend/AtlasRent.Api</c>: las pruebas sí llevan números de prueba a
    /// propósito y las migraciones son código generado con los tipos de columna que el modelo exige.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LosCamposDeConfiguracionFiscalNoTienenValorPorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var infracciones = new List<string>();
        var separador = Path.DirectorySeparatorChar;

        foreach (var archivo in ArchivosDeLaApi())
        {
            var esPeticion = archivo.Contains($"{separador}DTOs{separador}", StringComparison.Ordinal);
            var numeroLinea = 0;

            foreach (var linea in await File.ReadAllLinesAsync(archivo, ct))
            {
                numeroLinea++;
                var codigo = NoCodigo.Replace(linea, string.Empty);
                var coincidencia = Propiedad.Match(codigo);

                if (!coincidencia.Success
                    || !CampoDeConfiguracion.IsMatch(coincidencia.Groups["nombre"].Value))
                {
                    continue;
                }

                var nombre = coincidencia.Groups["nombre"].Value;
                var tipo = coincidencia.Groups["tipo"].Value;
                var esAnulable = tipo.EndsWith('?');
                var obligatorio = coincidencia.Groups["required"].Success;

                if (LiteralDeFecha.IsMatch(codigo))
                {
                    infracciones.Add(
                        $"{Path.GetFileName(archivo)}:{numeroLinea}: '{nombre}' se inicializa con una fecha "
                            + $"escrita en el código. {linea.Trim()}");
                }

                foreach (Match numero in LiteralNumerico.Matches(codigo))
                {
                    var valor = numero.Groups[1].Value;
                    if (!Permitidos.Contains(valor))
                    {
                        infracciones.Add(
                            $"{Path.GetFileName(archivo)}:{numeroLinea}: '{nombre}' con el valor por defecto "
                                + $"'{valor}'. {linea.Trim()}");
                    }
                }

                // El tipo anulable no tiene valor por defecto que esconder: `null` es su estado
                // inicial y, en la tolerancia, además es el estado documentado de «sin configurar».
                if (esPeticion && !esAnulable && tipo != "bool" && !obligatorio)
                {
                    infracciones.Add(
                        $"{Path.GetFileName(archivo)}:{numeroLinea}: '{nombre}' ({tipo}) no lleva `required`. "
                            + "Una petición que lo omita se queda con el valor por defecto del CLR y no da "
                            + $"ningún 400. {linea.Trim()}");
                }
            }
        }

        infracciones.Should().BeEmpty(
            """
            un campo de configuración fiscal con valor por defecto en el código. El rango autorizado del
            numerador, su umbral de aviso, su fecha de vencimiento y la tolerancia de diferencia de
            cambio los escribe el administrador (D14) y ninguno se puede inventar en el fuente
            """);
    }

    /// <summary>
    /// Ficheros <c>.cs</c> de la API: nada de migraciones generadas, <c>obj</c> ni <c>bin</c>. La
    /// raíz es <c>backend/AtlasRent.Api</c>, un proyecto hermano del de pruebas, así que
    /// <c>backend/AtlasRent.Tests/</c> queda fuera por construcción y no por filtro: las pruebas sí
    /// llevan números de prueba a propósito y esta guarda no los puede tocar.
    /// </summary>
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