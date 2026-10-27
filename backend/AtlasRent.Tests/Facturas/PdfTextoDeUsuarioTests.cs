using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// BUG-051: el mismo test de «el texto del usuario sale tal cual» que tiene la factura, para el
/// resto de PDF (recibo, estado de cuenta, cotización y contrato), pero afirmando sobre <b>el
/// texto que el PDF imprime</b> (<see cref="TextoPdf"/>) y no sobre el modelo.
/// <para>
/// Un texto interpretado saldría sin las marcas (<c>&lt;script&gt;</c> comido como HTML, <c>{{x}}</c>
/// sustituido) o escapado; aquí tiene que salir idéntico. Las acciones activas de un PDF
/// (<c>/JavaScript</c>, <c>/OpenAction</c>…) tampoco pueden estar (SEC-001).
/// </para>
/// </summary>
public sealed class PdfTextoDeUsuarioTests
{
    private const string Script = "<script>alert('x')</script>";
    private const string Plantilla = "{{total}}";
    private const string Sustitucion = "{cliente}";
    private const string Imagen = "<img src=x onerror=alert(1)>";

    private static readonly string[] Hostiles = [Script, Plantilla, Sustitucion, Imagen];

    private static readonly string[] Prohibidos =
        ["/JavaScript", "/JS", "/OpenAction", "/URI", "/Launch", "/EmbeddedFile"];

    private static readonly PlantillaPdf Estandar = RegistroPlantillasPdf.Resolver(null);

    // Sin la API arrancada no hay Program.cs que declare la licencia (misma que usa la API).
    static PdfTextoDeUsuarioTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    [Fact]
    public void ElExtractorLeeElTextoQueElPdfImprime()
    {
        // Si la librería cambia y el extractor deja de leer, las demás pruebas fallarían por eso y no
        // por el texto del usuario: aquí se comprueba el instrumento.
        var pdf = DocumentoReciboPdf.Generar(ModeloRecibo());

        TextoPdf.SinEspacios(TextoPdf.Extraer(pdf)).Should().Contain(TextoPdf.SinEspacios("Cliente de prueba"));
    }

    [Fact]
    public void ElReciboImprimeElTextoDelUsuarioTalCual()
    {
        var pdf = DocumentoReciboPdf.Generar(ModeloRecibo());

        Verificar(pdf);
    }

    [Fact]
    public void ElEstadoDeCuentaImprimeElTextoDelUsuarioTalCual()
    {
        var modelo = new DocumentoEstadoCuentaPdfModelo(
            Estandar,
            "Todo el historial",
            Cliente(),
            [
                new DocumentoEstadoCuentaPdfFila(
                    "F-1", "01/01/2026", string.Join(" ", Hostiles), "USD", "10.00", null, null, "10.00",
                    "01/02/2026", "Emitida", false),
            ],
            [new DocumentoEstadoCuentaPdfTotales("USD", "10.00", "0.00", "0.00", "10.00", "0.00")],
            null);

        Verificar(DocumentoEstadoCuentaPdf.Generar(modelo));
    }

    [Fact]
    public void LaCotizacionImprimeElTextoDelUsuarioTalCual()
    {
        var cotizacion = new Cotizacion
        {
            Id = Guid.NewGuid(),
            Estado = EstadoCotizacion.Borrador,
            Moneda = Moneda.USD,
            Total = 40m,
            CreatedAt = DateTimeOffset.UtcNow,
            Cliente = new Cliente { Nombre = Script, Direccion = Plantilla, Email = Sustitucion },
            Unidad = new Unidad { Nombre = Imagen },
            Lineas = Hostiles
                .Select((d, i) => new CotizacionLinea
                {
                    Posicion = i + 1,
                    Descripcion = d,
                    Cantidad = 1m,
                    PrecioUnitario = 10m,
                    Importe = 10m,
                })
                .ToList(),
        };

        Verificar(DocumentoCotizacionPdf.Generar(cotizacion, "001-0000000-1", null));
    }

    [Fact]
    public void ElContratoImprimeElTextoDelUsuarioTalCual()
    {
        var contrato = new Contrato
        {
            Id = Guid.NewGuid(),
            Moneda = Moneda.USD,
            FechaInicio = new DateOnly(2026, 1, 1),
            FechaFinVigente = new DateOnly(2026, 12, 31),
            DuracionInicialMeses = 12,
            Renta = 100m,
            DiaPago = 5,
            ReglaRenovacion = ReglaRenovacion.Mensual,
            RenovacionMeses = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Cliente = new Cliente { Nombre = Script, Direccion = Plantilla, Email = Sustitucion },
            Unidad = new Unidad { Nombre = Imagen },
            Facturadora = new Subempresa { Nombre = Script },
        };
        var congelada = new ContratoPlantillaCongelada
        {
            Codigo = "P-1",
            Nombre = Script,
            CongeladaAt = DateTimeOffset.UtcNow,
            Secciones = Hostiles.Select(h => new SeccionTexto(h, h)).ToList(),
            Anexos = [new AnexoCongelado(default, Plantilla, Hostiles.Select(h => new SeccionTexto(h, h)).ToList())],
        };

        Verificar(DocumentoContratoPdf.Generar(contrato, congelada, "001-0000000-1"));
    }

    private static DocumentoReciboPdfModelo ModeloRecibo() => new(
        Estandar,
        "REC-00000001",
        "01/01/2026",
        "USD",
        "10.00",
        new DocumentoFacturaPdfEmpresa(Script, Plantilla, Sustitucion, Imagen, Script),
        Cliente(),
        [new DocumentoReciboPdfDato("Referencia", Script + " " + Plantilla)],
        [new DocumentoReciboPdfAsignacion(Sustitucion + " " + Imagen, "USD", "10.00", null, null, true)],
        [new DocumentoFacturaPdfTotal("Total", "10.00", true)],
        null,
        Script);

    private static DocumentoFacturaPdfCliente Cliente() => new(
        "Cliente de prueba " + Script, Plantilla, Sustitucion, Imagen, "Unidad " + Script, "C-1");

    private static void Verificar(byte[] pdf)
    {
        var texto = TextoPdf.SinEspacios(TextoPdf.Extraer(pdf));

        texto.Should().NotBeEmpty("el extractor tiene que leer algo del PDF");
        foreach (var hostil in Hostiles)
        {
            texto.Should().Contain(TextoPdf.SinEspacios(hostil), $"«{hostil}» sale tal cual en el papel");
        }

        texto.Should().NotContain("&lt;").And.NotContain("&amp;", "nada se escapó como HTML");

        var bytes = System.Text.Encoding.Latin1.GetString(pdf);
        foreach (var prohibido in Prohibidos)
        {
            bytes.Should().NotContain(prohibido, $"un PDF generado por código no lleva «{prohibido}» (SEC-001)");
        }
    }
}
