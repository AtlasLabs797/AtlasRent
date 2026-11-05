using System.Globalization;
using System.Text;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using ClosedXML.Excel;

namespace AtlasRent.Api.Services;

/// <summary>
/// Exportación de los informes de §3.15 a Excel y CSV (F5-02, docs/ESPECIFICACION.md §3.15).
/// <para>
/// <b>Lo que se exporta son las mismas filas de la vista única</b> que ya trae
/// <see cref="ServicioInformes.FilasDelInformeAsync"/>, no una consulta de documentos propia: un
/// volcado que no sale de la vista acaba divergiendo de ella el día que cambia un filtro, y §3.15
/// exige que el informe cuadre con la tabla. Los importes van tal cual vienen en la fila, que ya
/// están calculados una vez.
/// </para>
/// <para>
/// <b>Los importes se escriben como número y las fechas como fecha</b>, no como texto: un total en
/// texto no suma en Excel, que es justo para lo que se abre una hoja de esto.
/// </para>
/// <para>
/// <b>Todo el texto externo pasa por <see cref="TextoSeguro"/></b>: nombres de clientes, números y
/// estados son dato no confiable (AGENTS.md §6) y un <c>=HYPERLINK(...)</c> en una celda se ejecuta
/// al abrir el archivo (SEC-005). La neutralización es solo para cadenas: un importe negativo es un
/// número y se escribe como número.
/// </para>
/// </summary>
public sealed class ServicioExportacionInformes(ServicioInformes informes, TimeProvider reloj)
{
    /// <summary>Tipo MIME de un xlsx, que es el que Excel espera para no abrirlo como texto.</summary>
    public const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Tipo MIME del CSV, con la codificación declarada porque el archivo va en UTF-8 con BOM.</summary>
    public const string TipoCsv = "text/csv; charset=utf-8";

    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    /// <summary>
    /// CSV en UTF-8 <b>con BOM</b>: sin él, Excel en Windows abre el archivo como ANSI y las tildes
    /// de «Retención» o de los nombres se ven como caracteres rotos.
    /// </summary>
    private static readonly Encoding CodificacionCsv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Las columnas del detalle, en orden. Son las que un gerente usa para revisar una factura en
    /// una hoja: qué es, de quién, cuándo vence y qué queda pendiente.
    /// </summary>
    private static readonly string[] Columnas =
    [
        "Fecha", "Tipo", "Número", "Cliente", "Serie", "Estado",
        "Vencimiento", "Moneda", "Total", "Cobrado", "Pendiente", "Retención",
    ];

    /// <summary>Fila 4 en las dos salidas: es donde empieza el detalle.</summary>
    private const int FilaCabecera = 4;

    /// <summary>
    /// La hoja con los mismos filtros que el resumen y el dashboard, en el formato pedido.
    /// </summary>
    public async Task<ArchivoExportado> ExportarAsync(
        FiltroInformes filtro,
        FormatoInforme formato,
        CancellationToken ct)
    {
        var filas = await informes.FilasDelInformeAsync(filtro, ct);

        var instante = reloj.GetUtcNow();
        var generado = instante.ToOffset(TiempoNegocio.Zona.GetUtcOffset(instante));
        var sello = generado.ToString("yyyyMMdd-HHmm", Cultura);

        return formato switch
        {
            FormatoInforme.Csv => new ArchivoExportado(
                Csv(filas, filtro, generado),
                TipoCsv,
                $"informe-{sello}.csv"),
            _ => new ArchivoExportado(
                await XlsxAsync(filas, filtro, generado, ct),
                TipoXlsx,
                $"informe-{sello}.xlsx"),
        };
    }

    // ---------------------------------------------------------------- xlsx

    private async Task<byte[]> XlsxAsync(
        IReadOnlyList<CuentaFila> filas,
        FiltroInformes filtro,
        DateTimeOffset generado,
        CancellationToken ct)
    {
        using var libro = new XLWorkbook();

        // La hoja de bloques va primero porque es lo que se lee; el detalle es la letra pequeña. Los
        // bloques salen de `ResumenAsync` y no de recalcular aquí: sería una segunda fórmula de
        // dinero, que es lo que §3.15 prohíbe.
        HojaResumen(libro.Worksheets.Add("Resumen"), await informes.ResumenAsync(filtro, ct), filtro, generado);

        var detalle = libro.Worksheets.Add("Detalle");

        detalle.Cell(1, 1).Value = TextoSeguro("Informe de cuentas (detalle de documentos)");
        detalle.Cell(2, 1).Value = TextoSeguro(LineaDeGeneracion(generado));
        detalle.Cell(3, 1).Value = TextoSeguro(LineaDeFiltros(filtro));

        for (var columna = 0; columna < Columnas.Length; columna++)
        {
            detalle.Cell(FilaCabecera, columna + 1).Value = TextoSeguro(Columnas[columna]);
        }

        detalle.Row(FilaCabecera).Style.Font.Bold = true;
        detalle.SheetView.FreezeRows(FilaCabecera);

        var numeroFila = FilaCabecera + 1;

        foreach (var fila in filas)
        {
            var celdas = FilaDe(fila);

            for (var columna = 0; columna < celdas.Length; columna++)
            {
                Escribir(detalle.Cell(numeroFila, columna + 1), celdas[columna]);
            }

            numeroFila++;
        }

        detalle.Columns(1, Columnas.Length).AdjustToContents(8, 40);

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);

        return memoria.ToArray();
    }

    private static void HojaResumen(
        IXLWorksheet hoja,
        InformeResumenRespuesta resumen,
        FiltroInformes filtro,
        DateTimeOffset generado)
    {
        hoja.Cell(1, 1).Value = TextoSeguro("Informe de cuentas (resumen económico)");
        hoja.Cell(2, 1).Value = TextoSeguro(LineaDeGeneracion(generado));
        hoja.Cell(3, 1).Value = TextoSeguro(LineaDeFiltros(filtro));

        hoja.Cell(5, 1).Value = TextoSeguro("Bloque");
        hoja.Cell(5, 2).Value = TextoSeguro("USD");
        hoja.Cell(5, 3).Value = TextoSeguro("DOP");
        hoja.Cell(5, 4).Value = TextoSeguro("Consolidado USD");
        hoja.Row(5).Style.Font.Bold = true;

        var numeroFila = 6;

        foreach (var (nombre, importe) in Bloques(resumen))
        {
            hoja.Cell(numeroFila, 1).Value = TextoSeguro(nombre);
            EscribirImporte(hoja.Cell(numeroFila, 2), importe.Usd);
            EscribirImporte(hoja.Cell(numeroFila, 3), importe.Dop);
            EscribirImporte(hoja.Cell(numeroFila, 4), importe.ConsolidadoUsd);
            numeroFila++;
        }

        // Un consolidado al que le faltan documentos tiene que decirlo (§3.12, D9): si no, la hoja
        // parece completa y no lo está.
        if (resumen.DocumentosSinTipoCambioCongelado > 0)
        {
            hoja.Cell(numeroFila + 1, 1).Value = TextoSeguro(
                $"{resumen.DocumentosSinTipoCambioCongelado} documento(s) en pesos sin tipo de cambio "
                + "congelado quedan fuera del consolidado en USD.");
        }

        // Y con la misma razón: el total de cobrado no puede ser menor que sus tres líneas sin que la
        // hoja diga por qué.
        if (resumen.SaldoAFavorFueraDePeriodo)
        {
            hoja.Cell(numeroFila + 2, 1).Value = TextoSeguro(
                "El filtro lleva rango de fechas y el saldo a favor no se suma al cobrado total: es un "
                + "saldo corriente sin fecha de negocio, no un movimiento del periodo.");
        }

        hoja.Columns(1, 4).AdjustToContents(8, 40);
    }

    /// <summary>
    /// Los bloques de §3.15 con su desglose, en el orden en que se leen. Ninguna suma nueva: los
    /// importes vienen ya calculados del resumen de F5-01.
    /// </summary>
    private static IEnumerable<(string Nombre, ImporteInforme Importe)> Bloques(InformeResumenRespuesta resumen)
    {
        yield return ("Proformado", resumen.Proformado);
        yield return ("Facturado", resumen.Facturado);
        yield return ("Cobrado a facturas", resumen.Cobrado.Facturas);
        yield return ("Cobrado a fianzas", resumen.Cobrado.Fianzas);
        yield return ("Cobrado a saldo a favor", resumen.Cobrado.SaldoAFavor);
        yield return ("Cobrado total", resumen.Cobrado.Total);
        yield return ("Retenido con comprobante", resumen.Retenido.ConComprobante);
        yield return ("Retenido sin comprobante", resumen.Retenido.SinComprobante);
        yield return ("Retenido total", resumen.Retenido.Total);
        yield return ("Pendiente de facturas", resumen.Pendiente.Facturas);
        yield return ("Pendiente de fianzas", resumen.Pendiente.Fianzas);
        yield return ("Pendiente total", resumen.Pendiente.Total);
        yield return ("Vencido", resumen.Vencido);
        yield return ("Fianzas retenidas", resumen.FianzasRetenidas);
        yield return ("Diferencias de cambio", resumen.DiferenciasCambio);
    }

    // ---------------------------------------------------------------- csv

    /// <summary>
    /// El CSV con la misma cabecera de tres líneas que el Excel y el mismo detalle. Va a mano y no
    /// con una librería porque son doce columnas y un archivo de texto: una dependencia más por eso
    /// sería una dependencia más que auditar.
    /// </summary>
    private static byte[] Csv(IReadOnlyList<CuentaFila> filas, FiltroInformes filtro, DateTimeOffset generado)
    {
        var texto = new StringBuilder();

        Linea(texto, "Informe de cuentas (detalle de documentos)");
        Linea(texto, LineaDeGeneracion(generado));
        Linea(texto, LineaDeFiltros(filtro));

        // Campo a campo, para que la fila de nombres sea una fila de celdas y se pueda importar como
        // tabla. Las tres líneas de arriba sí son un texto cada una, y por eso van entrecomilladas si
        // llevan separadores.
        texto.Append(string.Join(';', Columnas.Select(columna => CampoCsv(Celda.DeTexto(columna))))).Append("\r\n");

        foreach (var fila in filas)
        {
            texto.Append(string.Join(';', FilaDe(fila).Select(CampoCsv))).Append("\r\n");
        }

        // `Encoding.GetBytes` no pone el BOM por su cuenta —solo lo pone `StreamWriter`—, así que se
        // antepone a mano con `GetPreamble()`. Sin él, Excel en Windows abre el archivo como ANSI.
        return [.. CodificacionCsv.GetPreamble(), .. CodificacionCsv.GetBytes(texto.ToString())];
    }

    /// <summary>Una línea del encabezado, entrecomillada si hace falta porque lleva separadores.</summary>
    private static void Linea(StringBuilder texto, string contenido) =>
        texto.Append(CampoCsv(Celda.DeTexto(contenido))).Append("\r\n");

    /// <summary>
    /// Un campo del CSV: separador <c>;</c> (que es el que espera Excel en español), entrecomillado
    /// según RFC 4180 —con las comillas internas duplicadas— y con <b>el texto ya neutralizado</b>.
    /// Los números y las fechas salen sin neutralizar: son números y fechas.
    /// </summary>
    internal static string CampoCsv(Celda celda)
    {
        // La neutralización es solo para el texto (SEC-005). Un importe negativo empieza por «-», así
        // que neutralizarlo lo convertiría en la cadena «'-1234.50» y en Excel dejaría de ser un número:
        // se rompería la única suma que alguien hace con la columna. Las fechas no se tocan por lo
        // mismo. El entrecomillado de RFC 4180 es para todos los campos.
        var valor = celda.EsNumero || celda.EsFecha ? ValorComoTexto(celda) : TextoSeguro(celda.Texto);

        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        return valor.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? $"\"{valor.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : valor;
    }

    // ---------------------------------------------------------------- celdas

    /// <summary>
    /// La fila del detalle tal cual sale de la vista única, con cada celda marcada como texto,
    /// número o fecha para que los dos formatos escriba lo mismo con el tipo correcto.
    /// </summary>
    private static Celda[] FilaDe(CuentaFila fila) =>
    [
        Celda.DeFecha(fila.Fecha),
        Celda.DeTexto(fila.Tipo.ToString()),
        Celda.DeTexto(fila.Numero?.ToString(Cultura)),
        Celda.DeTexto(fila.Cliente),
        Celda.DeTexto(fila.SerieNumero?.ToString(Cultura)),
        Celda.DeTexto(fila.Estado),
        Celda.DeFecha(fila.Vencimiento),
        Celda.DeTexto(fila.Moneda.ToString()),
        Celda.DeNumero(fila.Total),
        Celda.DeNumero(fila.Cobrado),
        Celda.DeNumero(fila.Pendiente),
        Celda.DeNumero(fila.Retencion),
    ];

    private static void Escribir(IXLCell celda, Celda valor)
    {
        if (valor.EsNumero)
        {
            EscribirImporte(celda, valor.Numero);
            return;
        }

        if (valor.EsFecha)
        {
            if (valor.Fecha is { } dia)
            {
                celda.Value = dia.ToDateTime(TimeOnly.MinValue);
                celda.Style.NumberFormat.Format = "dd/mm/yyyy";
            }

            return;
        }

        if (valor.Texto is { } texto)
        {
            // ClosedXML documenta que, al escribir un texto que empieza por apóstrofo, lo guarda como
            // el prefijo de texto de la celda (`IXLStyle.IncludeQuotePrefix`) y deja el texto sin él:
            // es exactamente el mecanismo de Excel y por eso el archivo no enseña un apóstrofo.
            celda.Value = TextoSeguro(texto);
        }
    }

    /// <summary>
    /// Un importe como número, con formato de moneda. Un nulo es una celda vacía y no un cero: cero
    /// es un dato («esta factura está pagada») y el hueco es que no lo hay.
    /// </summary>
    private static void EscribirImporte(IXLCell celda, decimal? importe)
    {
        if (importe is not { } valor)
        {
            return;
        }

        celda.Value = valor;
        celda.Style.NumberFormat.Format = "#,##0.00";
    }

    private static string? ValorComoTexto(Celda celda) =>
        celda.EsNumero ? celda.Numero?.ToString("0.00", Cultura)
        : celda.EsFecha ? celda.Fecha?.ToString("yyyy-MM-dd", Cultura)
        : celda.Texto;

    /// <summary>
    /// Una celda del detalle, marcada con su tipo. El texto va <b>sin neutralizar</b> y lo neutraliza
    /// quien escribe: así Excel y CSV pasan por la misma función y no por dos que se parezcan.
    /// </summary>
    internal readonly record struct Celda(bool EsNumero, bool EsFecha, string? Texto, decimal? Numero, DateOnly? Fecha)
    {
        public static Celda DeTexto(string? texto) => new(false, false, texto, null, null);

        public static Celda DeNumero(decimal? importe) => new(true, false, null, importe, null);

        public static Celda DeFecha(DateOnly? dia) => new(false, true, null, null, dia);
    }

    // ---------------------------------------------------------------- seguridad del contenido

    /// <summary>
    /// Un texto de celda que Excel y CSV no interpretan como fórmula (SEC-005).
    /// <para>
    /// <b>Antepone un apóstrofo</b> —el prefijo de texto de Excel, que no se ve al abrir el archivo—
    /// cuando el valor empieza por <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tabulador, retorno de carro
    /// o salto de línea. Un nombre de cliente es contenido externo: alguien puede llamarse
    /// <c>=HYPERLINK("http://…")</c> y la hoja lo ejecutaría al abrirse.
    /// </para>
    /// <para>
    /// <b>Solo cadenas.</b> Un importe negativo empieza por <c>-</c> y no se toca: se escribe como
    /// número y con signo, y ahí lo que evita la fórmula es el tipo, no el apóstrofo.
    /// </para>
    /// <para>
    /// En Excel el apóstrofo no se ve dentro de la celda: <c>XLWorkbook</c> lo guarda como el prefijo
    /// de texto de la celda, que es lo que hace Excel cuando tecleas <c>'</c> delante de algo. La
    /// protección es la misma y el archivo se ve limpio.
    /// </para>
    /// </summary>
    internal static string? TextoSeguro(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return valor;
        }

        return valor[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' ? $"'{valor}" : valor;
    }

    // ---------------------------------------------------------------- encabezados

    private static string LineaDeGeneracion(DateTimeOffset generado) =>
        $"Generado el {generado.ToString("dd/MM/yyyy HH:mm", Cultura)} (Santo Domingo)";

    /// <summary>
    /// Los filtros que sí se aplicaron, para que una hoja suelta sepa qué es. Solo los puestos: un
    /// filtro ausente en la línea no es un dato que la hoja pueda inventar después.
    /// </summary>
    private static string LineaDeFiltros(FiltroInformes filtro)
    {
        var partes = new List<string>();

        if (filtro.Desde is { } desde)
        {
            partes.Add($"desde={desde.ToString("yyyy-MM-dd", Cultura)}");
        }

        if (filtro.Hasta is { } hasta)
        {
            partes.Add($"hasta={hasta.ToString("yyyy-MM-dd", Cultura)}");
        }

        if (filtro.SerieId is { } serie)
        {
            partes.Add($"serie={serie:D}");
        }

        if (filtro.Moneda is { } moneda)
        {
            partes.Add($"moneda={moneda}");
        }

        if (filtro.Estado is { } estado)
        {
            partes.Add($"estado={estado}");
        }

        if (filtro.ClienteId is { } cliente)
        {
            partes.Add($"cliente={cliente:D}");
        }

        if (filtro.UnidadId is { } unidad)
        {
            partes.Add($"unidad={unidad:D}");
        }

        if (filtro.ContratoId is { } contrato)
        {
            partes.Add($"contrato={contrato:D}");
        }

        if (filtro.InmuebleId is { } inmueble)
        {
            partes.Add($"inmueble={inmueble:D}");
        }

        if (filtro.PropietariaId is { } propietaria)
        {
            partes.Add($"propietaria={propietaria:D}");
        }

        if (filtro.FacturadoraId is { } facturadora)
        {
            partes.Add($"facturadora={facturadora:D}");
        }

        return partes.Count == 0
            ? "Filtros: ninguno (todo el histórico)"
            : $"Filtros: {string.Join("; ", partes)}";
    }
}