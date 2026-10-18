using System.Net;
using System.Text;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using ClosedXML.Excel;
using Xunit;

namespace AtlasRent.Tests.Informes;

/// <summary>
/// F5-02: exportación de informes a Excel y CSV (docs/ESPECIFICACION.md §3.15).
/// <para>
/// El criterio de aceptación es <b>que el contenido externo no llegue al archivo como fórmula</b>
/// (SEC-005): un nombre de cliente como <c>=HYPERLINK(...)</c> se ejecuta al abrir el Excel. Las
/// pruebas van contra la API de verdad y leen el fichero generado, porque una función que neutraliza
/// bien en aislamiento y un archivo donde el texto se escapa siguen siendo dos cosas distintas.
/// </para>
/// <para>
/// Los clientes de estas pruebas llevan nombres inventados a propósito: uno es una fórmula, que es el
/// caso que hay que probar. Ninguno se parece a una persona real.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InformeExportacionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>Nombre de cliente que Excel interpretaría como fórmula si la hoja no lo neutralizara.</summary>
    private const string ClienteFormula = "=HYPERLINK(\"http://ejemplo.test\")";

    /// <summary>Columnas del detalle, en el orden de la hoja.</summary>
    private const int ColumnaTipo = 2;

    private const int ColumnaCliente = 4;

    private const int ColumnaTotal = 9;

    /// <summary>Fila en la que empieza el detalle: las tres de encabezado y la de los nombres.</summary>
    private const int PrimeraFilaDeDetalle = 5;

    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---------------------------------------------------------------- neutralización

    /// <summary>
    /// La función compartida, en aislamiento: el criterio de aceptación es exactamente esta tabla de
    /// casos, y probarla aquí deja que las pruebas de archivo miren solo lo que el archivo hace.
    /// </summary>
    [Theory]
    [InlineData("=HYPERLINK(\"http://ejemplo.test\")", "'=HYPERLINK(\"http://ejemplo.test\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tCon tabulador", "'\tCon tabulador")]
    [InlineData("\r\nCon salto", "'\r\nCon salto")]
    [InlineData("Cliente normal", "Cliente normal")]
    [InlineData("Ana María Ruiz", "Ana María Ruiz")]
    [InlineData("12345", "12345")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void ElTextoQueEmpiezaPorFormulaVaConApostrofo(string? entrada, string? esperado) =>
        ServicioExportacionInformes.TextoSeguro(entrada).Should().Be(esperado);

    /// <summary>
    /// En el xlsx, el nombre del cliente <b>queda como texto, no como fórmula</b>. Lo que se mira es
    /// <c>HasFormula</c> y no solo el apóstrofo: lo que falla es que la celda sea ejecutable, que es
    /// distinto de cómo se muestre.
    /// </summary>
    [Fact]
    public async Task ElNombreDeClienteQueEsUnaFormulaNoEsUnaFormulaEnElExcel()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await EscenarioConClienteAsync(admin, ClienteFormula, 750m, ct);

        using var libro = AbrirLibro(await DescargarAsync(admin, "xlsx", $"clienteId={clienteId:D}", ct));
        var detalle = libro.Worksheet("Detalle");

        var celda = detalle.Cell(FilaDe(detalle, ClienteFormula), ColumnaCliente);

        celda.HasFormula.Should().BeFalse("SEC-005: el contenido externo no puede ejecutarse al abrir la hoja");
        celda.Value.Type.Should().Be(XLDataType.Text);
        celda.GetString().Should().Be(ClienteFormula, "el nombre va intacto, sin comillas ni recortes");

        // El apóstrofo que antepone el servicio es, en un xlsx, el prefijo de texto de la celda: no se
        // ve al abrir el archivo, pero es lo que le dice a Excel que no lo ejecute.
        celda.Style.IncludeQuotePrefix.Should().BeTrue(
            "el apóstrofo tiene que acabar como prefijo de texto de la celda, que es lo que evita que Excel la ejecute");
    }

    /// <summary>
    /// Lo mismo en el CSV, que no tiene el prefijo de texto de Excel y se abre en la misma aplicación.
    /// El campo va entrecomillado por RFC 4180 y con las comillas internas duplicadas, porque el nombre
    /// lleva comillas y el separador es <c>;</c>.
    /// </summary>
    [Fact]
    public async Task ElNombreDeClienteQueEsUnaFormulaTampocoEsUnaFormulaEnElCsv()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await EscenarioConClienteAsync(admin, ClienteFormula, 750m, ct);

        var cuerpo = await TextoDelCsvAsync(admin, $"clienteId={clienteId:D}", ct);

        cuerpo.Should().Contain(
            "\"'=HYPERLINK(\"\"http://ejemplo.test\"\")\"",
            "el texto va con apóstrofo y el campo entrecomillado con las comillas duplicadas");
        cuerpo.Should().NotContain($";{ClienteFormula}", "ningún campo puede empezar por el signo sin neutralizar");
    }

    /// <summary>
    /// La contraprueba de la anterior, y la razón de que la neutralización sea solo de texto: un
    /// importe negativo empieza por <c>-</c>, así que neutralizarlo lo sacaría como la cadena
    /// <c>'-1234.50</c> y en Excel dejaría de ser un número. Es la única suma que alguien hace con la
    /// columna, y el caso es latente —ninguna columna exportada baja hoy de cero—, pero el día que
    /// baje, sin esto la hoja daría un total que no cuadra.
    /// <para>
    /// El texto de la otra columna de la misma fila se neutraliza igual, que es lo que quiere SEC-005:
    /// la decisión es por tipo de celda, no por el signo.
    /// </para>
    /// </summary>
    [Fact]
    public void UnCampoCsvDeNumeroNegativoNoSeNeutraliza()
    {
        var negativo = ServicioExportacionInformes.CampoCsv(
            ServicioExportacionInformes.Celda.DeNumero(-1_234.50m));

        negativo.Should().Be("-1234.50");
        negativo.Should().NotStartWith("'", "un importe con apóstrofo es texto y en Excel no suma");

        ServicioExportacionInformes.CampoCsv(ServicioExportacionInformes.Celda.DeNumero(1_234.50m))
            .Should().Be("1234.50");

        ServicioExportacionInformes.CampoCsv(ServicioExportacionInformes.Celda.DeNumero(null))
            .Should().BeEmpty("una celda sin importe es una celda vacía, no un cero");

        ServicioExportacionInformes.CampoCsv(
            ServicioExportacionInformes.Celda.DeFecha(new DateOnly(2026, 10, 16)))
            .Should().Be("2026-10-16");

        // El texto sigue neutralizado y entrecomillado por RFC 4180: esto no abre la puerta de SEC-005.
        ServicioExportacionInformes.CampoCsv(ServicioExportacionInformes.Celda.DeTexto("-2+3"))
            .Should().Be("'-2+3");
        ServicioExportacionInformes.CampoCsv(
            ServicioExportacionInformes.Celda.DeTexto("Ana; María"))
            .Should().Be("\"Ana; María\"");
    }

    // ---------------------------------------------------------------- tipos de columna

    /// <summary>
    /// Los importes se escriben como número y las fechas como fecha. Un total en texto no suma en
    /// Excel, que es justo lo que se abre una hoja de esto a hacer; y los importes van con el signo,
    /// sin neutralizar, porque un número no es una fórmula por empezar por <c>-</c>.
    /// </summary>
    [Fact]
    public async Task LosImportesSonNumerosYLasFechasSonFechas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, 1234.56m, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 100m, ct);

        using var libro = AbrirLibro(await DescargarAsync(admin, "xlsx", $"clienteId={escenario.ClienteId:D}", ct));
        var detalle = libro.Worksheet("Detalle");

        var filaFactura = FilaDeTipo(detalle, nameof(TipoFilaCuenta.Factura));

        detalle.Cell(filaFactura, ColumnaTotal).Value.Type.Should().Be(XLDataType.Number);
        detalle.Cell(filaFactura, ColumnaTotal).GetDouble().Should().BeApproximately(1234.56, 0.0001);
        detalle.Cell(filaFactura, ColumnaTotal).Style.NumberFormat.Format.Should().Be("#,##0.00");
        detalle.Cell(filaFactura, 1).Value.Type.Should().Be(XLDataType.DateTime, "una fecha como texto no se puede ordenar");
        detalle.Cell(filaFactura, 7).Value.Type.Should().Be(XLDataType.DateTime, "el vencimiento es una fecha");

        // Todas las columnas de dinero del detalle: ninguna puede haber salido como texto.
        var tipos = detalle
            .Range(PrimeraFilaDeDetalle, ColumnaTotal, detalle.LastRowUsed()!.RowNumber(), 12)
            .Cells()
            .Select(celda => celda.Value.Type)
            .Distinct()
            .ToArray();

        tipos.Should().BeSubsetOf(
            [XLDataType.Number, XLDataType.Blank],
            "una columna de dinero escrita como texto no suma en Excel");
    }

    // ---------------------------------------------------------------- permisos y contrato

    /// <summary>
    /// La exportación es de gerente y administrador, con el mismo permiso que el resumen: el empleado
    /// recibe un 403 y no un archivo. El <c>Content-Type</c> se comprueba porque es lo que decide si
    /// Excel abre el adjunto o el navegador enseña un texto.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoRecibeLaExportacionYGerenteYAdminSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaCuentas.SesionAsync(bd, api, Roles.Gerente, ct);
        using var empleado = await AyudaCuentas.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await EscenarioAsync(admin, 300m, ct);
        var consulta = $"clienteId={escenario.ClienteId:D}";

        var prohibido = await AyudaInformes.ExportarAsync(empleado, "xlsx", consulta, ct);
        prohibido.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "AGENTS.md §6: el empleado no recibe nada por API con datos de la empresa");

        var prohibidoCsv = await AyudaInformes.ExportarAsync(empleado, "csv", consulta, ct);
        prohibidoCsv.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "el CSV tiene los mismos datos que el Excel: el permiso no depende del formato");

        foreach (var sesion in new[] { admin, gerente })
        {
            var xlsx = await AyudaInformes.ExportarAsync(sesion, "xlsx", consulta, ct);
            xlsx.StatusCode.Should().Be(HttpStatusCode.OK, await xlsx.Cuerpo());
            xlsx.Content.Headers.ContentType!.MediaType.Should().Be(TipoXlsx);
            xlsx.Content.Headers.ContentDisposition!.FileName.Should().Match(
                "informe-????????-????.xlsx",
                "el nombre lleva la fecha y ni espacios ni tildes, para que no se rompa al descargarlo");

            var csv = await AyudaInformes.ExportarAsync(sesion, "csv", consulta, ct);
            csv.StatusCode.Should().Be(HttpStatusCode.OK, await csv.Cuerpo());
            csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        }
    }

    /// <summary>
    /// Un formato que no existe es una petición no válida, no un error de servidor: un 500 dejaría
    /// sin saber si es un fallo del código o un dato raro.
    /// </summary>
    [Fact]
    public async Task UnFormatoQueNoExisteEsUn400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, 300m, ct);

        var respuesta = await AyudaInformes.ExportarAsync(admin, "pdf", $"clienteId={escenario.ClienteId:D}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Cuerpo());
    }

    // ---------------------------------------------------------------- filtros

    /// <summary>
    /// La hoja sale de las filas de la vista única, así que con los mismos filtros tiene que traer
    /// exactamente las mismas filas. Y filtrar por un cliente no deja pasar las facturas de otro.
    /// </summary>
    [Fact]
    public async Task LaExportacionAplicaLosFiltrosYCuadraConLaVistaUnica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var primero = await EscenarioAsync(admin, 111.11m, ct);
        var segundo = await EscenarioAsync(admin, 222.22m, ct);

        // Un cliente con dos filas (factura y fianza) y otro con una: el filtro por cliente tiene que
        // dejar fuera las del primero sin recortar la del segundo.
        await AyudaCuentas.EmitidaAsync(admin, primero, primero.Hoy.AddDays(10), 111.11m, ct);
        await AyudaCuentas.FianzaAsync(admin, primero, 90m, ct);

        var consulta = $"clienteId={segundo.ClienteId:D}";

        using var libro = AbrirLibro(await DescargarAsync(admin, "xlsx", consulta, ct));
        var detalle = libro.Worksheet("Detalle");

        var filas = detalle.RowsUsed().Select(fila => fila.RowNumber())
            .Where(numero => numero >= PrimeraFilaDeDetalle)
            .ToArray();

        filas.Should().HaveCount(1, "el filtro es por cliente: las filas del otro no van");

        // La misma consulta contra la vista única: si los dos dan lo mismo, la hoja no tiene una
        // consulta propia que un día diga otra cosa (§3.15).
        var vista = await AyudaCuentas.VistaAsync(
            admin, $"{consulta}&incluirProformas=true&incluirFianzas=true", ct);

        filas.Should().HaveCount(
            AyudaCuentas.Filas(vista).Length,
            "la exportación tiene que traer las mismas filas que la vista única con los mismos filtros");

        detalle.Cell(filas[0], ColumnaTotal).GetDouble().Should().BeApproximately(222.22, 0.0001);
    }

    /// <summary>
    /// La cabecera de las dos salidas lleva los filtros aplicados y cuándo se generó: una hoja que se
    /// abre de un correo dentro de tres meses tiene que decir de qué periodo es.
    /// </summary>
    [Fact]
    public async Task LaCabeceraDiceLosFiltrosYCuandoSeGenero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, 300m, ct);
        var consulta = $"clienteId={escenario.ClienteId:D}&desde={escenario.Hoy.AddDays(-1):yyyy-MM-dd}";

        using var libro = AbrirLibro(await DescargarAsync(admin, "xlsx", consulta, ct));
        var detalle = libro.Worksheet("Detalle");

        detalle.Cell(2, 1).GetString().Should().Contain("Generado el ");
        detalle.Cell(3, 1).GetString().Should().Contain($"cliente={escenario.ClienteId:D}");
        detalle.Cell(3, 1).GetString().Should().Contain($"desde={escenario.Hoy.AddDays(-1):yyyy-MM-dd}");
        detalle.Cell(3, 1).GetString().Should().NotContain("serie=", "solo se anotan los filtros que se aplicaron");

        var csv = await TextoDelCsvAsync(admin, consulta, ct);
        csv.Should().Contain("Generado el ");
        csv.Should().Contain($"cliente={escenario.ClienteId:D}");
        csv.Split("\r\n").Should().Contain(linea => linea.StartsWith("Fecha;Tipo;", StringComparison.Ordinal));
    }

    /// <summary>
    /// El CSV lleva BOM. Sin él, Excel en Windows abre el archivo como ANSI y las tildes de los
    /// encabezados y de los nombres se ven rotas.
    /// </summary>
    [Fact]
    public async Task ElCsvVaEnUtf8ConBomYSeparadoPorPuntoYComa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, 300m, ct);
        var bytes = await DescargarAsync(admin, "csv", $"clienteId={escenario.ClienteId:D}", ct);

        bytes[0].Should().Be(0xEF, "sin BOM Excel en español lee el archivo como ANSI");
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
        Encoding.UTF8.GetString(bytes).Should().Contain("Retención");
    }

    // ---------------------------------------------------------------- ayudas

    /// <summary>
    /// Un escenario de <see cref="AyudaCuentas"/> con un cliente de nombre propio. No se puede pedir
    /// por <c>AyudaCuentas.EscenarioAsync</c> porque el nombre del cliente no es un parámetro suyo y
    /// tocarlo por esto sería cambiar una ayuda que usan otras muchas pruebas.
    /// </summary>
    private async Task<Guid> EscenarioConClienteAsync(
        SesionCliente sesion,
        string nombreCliente,
        decimal total,
        CancellationToken ct)
    {
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, nombre: nombreCliente);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            sesion,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, moneda: "USD"),
            ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, null, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), total, ct, unidadId);

        return clienteId;
    }

    /// <summary>Cliente y factura del escenario, para las pruebas que no necesitan el nombre a medida.</summary>
    private async Task<AyudaCuentas.Escenario> EscenarioAsync(SesionCliente sesion, decimal total, CancellationToken ct)
    {
        var escenario = await AyudaCuentas.EscenarioAsync(sesion, bd, ct);
        await AyudaCuentas.EmitidaAsync(sesion, escenario, escenario.Hoy.AddDays(10), total, ct);

        return escenario;
    }

    /// <summary>El libro del adjunto, listo para leer celdas.</summary>
    private static XLWorkbook AbrirLibro(byte[] contenido)
    {
        using var memoria = new MemoryStream(contenido);
        return new XLWorkbook(memoria);
    }

    private static async Task<byte[]> DescargarAsync(
        SesionCliente sesion,
        string formato,
        string consulta,
        CancellationToken ct)
    {
        var respuesta = await AyudaInformes.ExportarAsync(sesion, formato, consulta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadAsByteArrayAsync(ct);
    }

    private static async Task<string> TextoDelCsvAsync(
        SesionCliente sesion,
        string consulta,
        CancellationToken ct) =>
        Encoding.UTF8.GetString(await DescargarAsync(sesion, "csv", consulta, ct));

    /// <summary>La fila del detalle que trae ese cliente.</summary>
    private static int FilaDe(IXLWorksheet hoja, string cliente) =>
        hoja.RowsUsed().Select(fila => fila.RowNumber())
            .Where(numero => numero >= PrimeraFilaDeDetalle)
            .Single(numero => hoja.Cell(numero, ColumnaCliente).GetString()
                .Contains(cliente, StringComparison.Ordinal));

    /// <summary>La fila del detalle de ese tipo de fila (factura, proforma o fianza).</summary>
    private static int FilaDeTipo(IXLWorksheet hoja, string tipo) =>
        hoja.RowsUsed().Select(fila => fila.RowNumber())
            .Where(numero => numero >= PrimeraFilaDeDetalle)
            .Single(numero => hoja.Cell(numero, ColumnaTipo).GetString() == tipo);
}