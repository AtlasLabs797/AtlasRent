using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lo que el estado de cuenta de un cliente <b>va a decir</b> (F4-01,
/// <c>docs/ESPECIFICACION.md</c> §3.14), todo en texto ya formateado. No sabe nada de QuestPDF.
/// <para>
/// <b>Las filas y los importes son los de la vista única</b> (<see cref="CuentaFila"/>, F3-10): aquí no
/// se calcula ningún saldo. El compositor solo suma las columnas que ya trae la vista, que es la misma
/// cuenta que hace una persona al pie de la tabla, y por eso el documento cuadra con lo que sale en
/// <c>GET /api/v1/cuentas</c>.
/// </para>
/// </summary>
/// <param name="Periodo">«Año 2026» o «Todos los años»: los documentos del que se ha impreso.</param>
/// <param name="Aviso">
/// Lo que hay que decir cuando el documento no cabe entero —filas recortadas por el tope—, o nulo si
/// cabe. Un estado de cuenta que recorta y no lo dice es peor que uno que no existe.
/// </param>
internal sealed record DocumentoEstadoCuentaPdfModelo(
    PlantillaPdf Plantilla,
    string Periodo,
    DocumentoFacturaPdfCliente Cliente,
    IReadOnlyList<DocumentoEstadoCuentaPdfFila> Filas,
    IReadOnlyList<DocumentoEstadoCuentaPdfTotales> Totales,
    string? Aviso);

/// <summary>
/// Una fila del estado de cuenta: las columnas de §3.13. Lo que no aplica a la fila va en <c>null</c>
/// (una proforma no tiene pendiente ni vencimiento) y entonces no se imprime, igual que en la vista.
/// </summary>
internal sealed record DocumentoEstadoCuentaPdfFila(
    string Documento,
    string Fecha,
    string? Concepto,
    string Moneda,
    string? Total,
    string? Retenido,
    string? Cobrado,
    string? Pendiente,
    string? Vencimiento,
    string? Estado,
    bool Vencida);

/// <summary>
/// Totales de una moneda: total, retenido, cobrado, pendiente y la parte del pendiente que está
/// vencida. <b>Nunca se convierte entre monedas</b> (§6): cada una se suma consigo misma.
/// </summary>
internal sealed record DocumentoEstadoCuentaPdfTotales(
    string Moneda,
    string Total,
    string Retenido,
    string Cobrado,
    string Pendiente,
    string Vencido);

/// <summary>
/// Estado de cuenta del cliente con el diseño de <c>docs/DESIGN.md §7</c>: carta, márgenes de 18 mm,
/// tinta <c>#1D1D1F</c> y el acento solo en la marca. Sin HTML ni plantillas: se compone por código.
/// <para>
/// <b>Modelo y render van separados a propósito</b>, igual que en <see cref="DocumentoFacturaPdf"/>:
/// <see cref="Modelo"/> decide y formatea, <see cref="Generar"/> solo pinta. Es lo que permite probar
/// el contenido sin extraer texto del PDF.
/// </para>
/// <para>
/// <b>La cabecera no lleva empresa facturadora.</b> Un estado de cuenta es de un cliente, y un cliente
/// puede tener contratos con facturadoras distintas: imprimir la de cualquiera de ellos sería inventar
/// un dato. El bloque sale vacío antes que mentir.
/// </para>
/// <para>
/// <b>Lo que viene del usuario (concepto, nombre del cliente) sale con los elementos de texto de la
/// librería, que no interpretan nada</b>: ni HTML, ni marcado, ni plantillas. Regla de
/// <c>docs/SEGURIDAD.md</c> (SEC-001) y de <c>docs/DESIGN.md §7</c>.
/// </para>
/// <para>
/// TODO(A#): las fuentes del design system son Geist y Geist Mono y hay que incrustarlas; los archivos no
/// están en el repositorio (<c>docs/DESIGN.md §8</c>). Es el mismo pendiente que el resto de PDF.
/// </para>
/// </summary>
internal static class DocumentoEstadoCuentaPdf
{
    private const string Tinta = "#1D1D1F";
    private const string Apagado = "#6E6E73";
    private const string Acento = "#AD2275";
    private const string Separador = "#E0E0E0";

    private const float Cuerpo = 9.5f;
    private const float Sobrelinea = 7.5f;
    private const float TamTitulo = 20f;
    private const float TamSubtitulo = 13f;

    private const float AnchoNumeroDato = 175f;
    private const float AnchoFecha = 42f;
    private const float AnchoMoneda = 30f;
    private const float AnchoImporte = 62f;
    private const float AnchoRetencion = 52f;
    private const float AnchoVencimiento = 42f;
    private const float AnchoEstado = 40f;
    private const float AnchoTotales = 210f;
    private const float AnchoPie = 150f;

    /// <summary>
    /// Monta el modelo: decide qué dice el documento y con qué formato. No toca QuestPDF.
    /// </summary>
    /// <param name="cliente">Cliente del estado de cuenta, con sus datos de contacto.</param>
    /// <param name="documentoCliente">Documento de identidad del cliente, ya descifrado (D16).</param>
    /// <param name="filas">
    /// Filas de la vista única (<see cref="CuentaFila"/>) ya filtradas por cliente y por año. Son las
    /// que se suman y las que se imprimen, en el mismo orden.
    /// </param>
    /// <param name="documentosEnElFiltro">
    /// Cuántos documentos tiene el filtro, que puede ser más que las filas si hubo recorte. Es lo que
    /// permite decir en el papel «de 640 se muestran 500» en vez de dejar que se piense que son todos.
    /// </param>
    public static DocumentoEstadoCuentaPdfModelo Modelo(
        Cliente cliente,
        string? documentoCliente,
        int? anio,
        IReadOnlyList<CuentaFila> filas,
        int documentosEnElFiltro,
        PlantillaPdf plantilla)
    {
        return new DocumentoEstadoCuentaPdfModelo(
            plantilla,
            anio is { } anyo ? $"Año {anyo}" : "Todos los años",
            new DocumentoFacturaPdfCliente(
                cliente.Nombre,
                documentoCliente,
                cliente.Direccion,
                cliente.Email ?? cliente.Telefono,
                Unidad: null,
                Contrato: null),
            [.. filas.Select(Fila)],
            Totales(filas),
            Aviso(filas.Count, documentosEnElFiltro));
    }

    /// <summary>
    /// Dibuja el modelo y devuelve el PDF en memoria. No escribe nada en disco: lo sirve la API.
    /// </summary>
    public static byte[] Generar(DocumentoEstadoCuentaPdfModelo modelo) =>
        modelo.Plantilla.Nombre == RegistroPlantillasPdf.NombrePorDefecto
            ? Dibujar(modelo)
            : throw new InvalidOperationException(
                $"No hay compositor para la plantilla de PDF «{modelo.Plantilla.Nombre}».");

    private static byte[] Dibujar(DocumentoEstadoCuentaPdfModelo modelo) =>
        Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(modelo.Plantilla.Ancho, modelo.Plantilla.Alto);
                pagina.Margin(18, Unit.Millimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(Cuerpo).FontColor(Tinta));

                pagina.Header().Element(c => Cabecera(c, modelo));
                pagina.Content().PaddingVertical(14).Element(c => Contenido(c, modelo));
                pagina.Footer().Element(Pie);
            });
        }).GeneratePdf();

    /// <summary>
    /// Lockup de marca a la izquierda; tipo de documento, periodo y nombre del cliente a la derecha. Sin
    /// bloque de empresa, y no por descuido: ver la nota de la clase.
    /// </summary>
    private static void Cabecera(IContainer contenedor, DocumentoEstadoCuentaPdfModelo modelo)
    {
        contenedor.Column(columna =>
        {
            columna.Item().Row(fila =>
            {
                fila.RelativeItem().Column(lockup =>
                {
                    lockup.Item().Text(texto => texto.Span("Atlas Rent").FontSize(13).SemiBold());
                    lockup.Item().PaddingTop(1).Text(texto => texto
                        .Span("by Atlas Labs").FontSize(Sobrelinea).FontColor(Apagado));
                });

                fila.ConstantItem(AnchoNumeroDato).Column(derecha =>
                {
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span("Estado de cuenta").FontSize(TamTitulo).SemiBold().FontColor(Acento));
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.Periodo).FontSize(TamSubtitulo).SemiBold());
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.Cliente.Nombre).FontColor(Apagado));
                });
            });

            columna.Item().PaddingTop(6)
                .BorderBottom(0.5f)
                .BorderColor(Acento)
                .Text(" ")
                .FontSize(1);
        });
    }

    /// <summary>
    /// Cliente, aviso de recorte, tabla y totales. Sin marca de agua: un estado de cuenta no es un
    /// documento que se anule (§3.14 es de consulta, no de historial), y quien lo archiva lo archiva
    /// entero.
    /// </summary>
    private static void Contenido(IContainer contenedor, DocumentoEstadoCuentaPdfModelo modelo)
    {
        contenedor.Column(columna =>
        {
            columna.Item().Column(cliente =>
            {
                cliente.Item().Text("Cliente").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cliente.Item().PaddingTop(4).Text(modelo.Cliente.Nombre).SemiBold();
                cliente.Item().Text(FormatoDocumentoPdf.Dato(modelo.Cliente.Documento, "Documento"));
                cliente.Item().Text(FormatoDocumentoPdf.Dato(modelo.Cliente.Direccion, "Dirección"));
                cliente.Item().Text(FormatoDocumentoPdf.Dato(modelo.Cliente.Contacto, "Contacto"));
            });

            // El aviso va antes de la tabla, no al final: quien lee tiene que enterarse de que le
            // faltan documentos antes de empezar a sumar lo que ve.
            if (modelo.Aviso is { } aviso)
            {
                columna.Item().PaddingTop(10).Text(texto => texto.Span(aviso).FontColor(Apagado));
            }

            columna.Item().PaddingTop(14).Element(c => Tabla(c, modelo.Filas));

            if (modelo.Totales.Count > 0)
            {
                columna.Item().PaddingTop(14).Element(c => Totales(c, modelo.Totales));
            }
        });
    }

    /// <summary>
    /// Tabla de documentos sin bandas de color, cabecera en overline gris y separador de 0,5 pt bajo
    /// cada fila (<c>docs/DESIGN.md §7</c>). El concepto va bajo el documento porque las columnas de
    /// §3.13 no caben en una fila de carta con el concepto al lado.
    /// </summary>
    private static void Tabla(IContainer contenedor, IReadOnlyList<DocumentoEstadoCuentaPdfFila> filas)
    {
        if (filas.Count == 0)
        {
            contenedor.Text(texto => texto
                .Span("No hay documentos de este cliente en el periodo.")
                .FontColor(Apagado));

            return;
        }

        contenedor.Column(columna =>
        {
            columna.Item().PaddingBottom(4).Row(cabecera =>
            {
                cabecera.RelativeItem().Text("Documento").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoFecha).AlignRight().Text("Fecha").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoMoneda).AlignRight().Text("Mon.").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Total").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoRetencion).AlignRight().Text("Reten.").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Cobrado").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Pendiente").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoVencimiento).AlignRight().Text("Vence").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoEstado).AlignRight().Text("Estado").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            });

            foreach (var fila in filas)
            {
                columna.Item().PaddingVertical(4)
                    .BorderBottom(0.5f)
                    .BorderColor(Separador)
                    .Row(linea =>
                    {
                        // El tipo va en el documento porque una fianza no tiene serie ni número y sin él
                        // la columna no se sabría leer (§3.13: la insignia de tipo va en la fila).
                        linea.RelativeItem().Column(celda =>
                        {
                            celda.Item().Text(texto => texto.Span(fila.Documento).SemiBold());

                            if (fila.Concepto is not null)
                            {
                                celda.Item().Text(texto => texto
                                    .Span(fila.Concepto).FontSize(Sobrelinea).FontColor(Apagado));
                            }
                        });

                        linea.ConstantItem(AnchoFecha).AlignRight().Text(fila.Fecha);
                        linea.ConstantItem(AnchoMoneda).AlignRight().Text(fila.Moneda);
                        linea.ConstantItem(AnchoImporte).AlignRight().Text(fila.Total);
                        linea.ConstantItem(AnchoRetencion).AlignRight().Text(fila.Retenido);
                        linea.ConstantItem(AnchoImporte).AlignRight().Text(fila.Cobrado);
                        linea.ConstantItem(AnchoImporte).AlignRight().Text(fila.Pendiente);
                        linea.ConstantItem(AnchoVencimiento).AlignRight().Text(fila.Vencimiento);
                        linea.ConstantItem(AnchoEstado).AlignRight().Text(fila.Estado);
                    });
            }
        });
    }

    /// <summary>Totales por moneda a la derecha (§3.13 «totales por moneda»).</summary>
    private static void Totales(IContainer contenedor, IReadOnlyList<DocumentoEstadoCuentaPdfTotales> totales)
    {
        contenedor.Column(columna =>
        {
            foreach (var moneda in totales)
            {
                columna.Item().PaddingTop(6).Text(moneda.Moneda).SemiBold();

                foreach (var (etiqueta, valor) in new[]
                {
                    ("Total", moneda.Total),
                    ("Retenido", moneda.Retenido),
                    ("Cobrado", moneda.Cobrado),
                    ("Pendiente", moneda.Pendiente),
                    ("Vencido", moneda.Vencido),
                })
                {
                    var destacado = etiqueta == "Pendiente";

                    columna.Item().PaddingTop(2).Width(AnchoTotales).Row(fila =>
                    {
                        var etiquetaFila = fila.RelativeItem().Text(etiqueta);
                        var valorFila = fila.RelativeItem().AlignRight().Text(valor);

                        if (destacado)
                        {
                            etiquetaFila.SemiBold();
                            valorFila.SemiBold();
                        }
                    });
                }
            }
        });
    }

    /// <summary>Pie con el número de página, que lo cuenta la biblioteca mientras dibuja.</summary>
    private static void Pie(IContainer contenedor)
    {
        contenedor.BorderTop(0.5f).BorderColor(Separador).PaddingTop(6).Row(fila =>
        {
            fila.RelativeItem().Text("Atlas Rent").FontColor(Apagado);
            fila.ConstantItem(AnchoPie).AlignRight().Text(texto =>
            {
                texto.Span("Página ").FontSize(Sobrelinea).FontColor(Apagado);
                texto.CurrentPageNumber().FontSize(Sobrelinea).FontColor(Apagado);
                texto.Span(" de ").FontSize(Sobrelinea).FontColor(Apagado);
                texto.TotalPages().FontSize(Sobrelinea).FontColor(Apagado);
            });
        });
    }

    // ---------------------------------------------------------------- decisiones del modelo

    /// <summary>Una fila de la vista única traducida a columnas de papel.</summary>
    private static DocumentoEstadoCuentaPdfFila Fila(CuentaFila fila) =>
        new(
            Documento(fila),
            fila.Fecha is { } fecha ? FormatoDocumentoPdf.Fecha(fecha) : "—",
            Dato(fila.Concepto),
            fila.Moneda.ToString(),
            Importe(fila.Total, fila.Moneda),
            Importe(fila.Retencion, fila.Moneda),
            Importe(fila.Cobrado, fila.Moneda),
            Importe(fila.Pendiente, fila.Moneda),
            // El estado de vencimiento va como texto junto a la fecha («12/03/2026 · vencida»), nunca como
            // un bloque de color: §7 lo dice, y en un papel impreso el color tampoco dice nada por sí solo.
            fila.Vencimiento is { } vencimiento
                ? FormatoDocumentoPdf.Fecha(vencimiento) + (fila.Vencida is true ? " · vencida" : string.Empty)
                : null,
            Dato(fila.Estado),
            fila.Vencida is true);

    /// <summary>
    /// «Serie 3 · nº 45», «Serie 1 · nº 7 · proforma» o «Fianza». La insignia de tipo va en la fila porque
    /// una fianza no tiene serie ni número y sin ella la columna no se sabe leer (§3.13).
    /// </summary>
    private static string Documento(CuentaFila fila)
    {
        if (fila.Tipo == TipoFilaCuenta.Fianza)
        {
            return "Fianza";
        }

        var tipo = fila.Tipo == TipoFilaCuenta.Proforma ? " · proforma" : string.Empty;
        var numero = fila.Numero is { } n ? $"nº {n}" : "sin número";
        var serie = fila.SerieNumero is { } s ? $"Serie {s} · " : string.Empty;

        return $"{serie}{numero}{tipo}";
    }

    /// <summary>
    /// Totales por moneda, sumando las columnas de las filas. <b>Solo se suma: ningún saldo se calcula
    /// aquí.</b> El pendiente de cada fila lo dio <c>CalculoCobro</c> o <c>CalculoFianza</c> a través de
    /// la vista única, y lo vencido es la suma del pendiente de las filas que la vista marcó como
    /// vencidas —la misma cuenta que hace una persona al pie de la tabla.
    /// </summary>
    private static IReadOnlyList<DocumentoEstadoCuentaPdfTotales> Totales(IReadOnlyList<CuentaFila> filas)
    {
        var porMoneda = new Dictionary<Moneda, (decimal Total, decimal Retencion, decimal Cobrado, decimal Pendiente, decimal Vencido)>();

        foreach (var fila in filas)
        {
            var suma = porMoneda.GetValueOrDefault(fila.Moneda);
            var pendiente = fila.Pendiente ?? 0m;

            porMoneda[fila.Moneda] = (
                Redondeo.Importe(suma.Total + (fila.Total ?? 0m)),
                Redondeo.Importe(suma.Retencion + (fila.Retencion ?? 0m)),
                Redondeo.Importe(suma.Cobrado + (fila.Cobrado ?? 0m)),
                Redondeo.Importe(suma.Pendiente + pendiente),
                Redondeo.Importe(suma.Vencido + (fila.Vencida is true ? pendiente : 0m)));
        }

        return
        [
            .. porMoneda.Keys
                .OrderBy(m => m.ToString(), StringComparer.Ordinal)
                .Select(moneda =>
                {
                    var suma = porMoneda[moneda];

                    return new DocumentoEstadoCuentaPdfTotales(
                        moneda.ToString(),
                        FormatoDocumentoPdf.Importe(suma.Total, moneda),
                        FormatoDocumentoPdf.Importe(suma.Retencion, moneda),
                        FormatoDocumentoPdf.Importe(suma.Cobrado, moneda),
                        FormatoDocumentoPdf.Importe(suma.Pendiente, moneda),
                        FormatoDocumentoPdf.Importe(suma.Vencido, moneda));
                })
        ];
    }

    /// <summary>
    /// Lo que hay que decir cuando el documento no cabe entero.
    /// <para>
    /// TODO(A#): el tope de filas del servicio recorta el detalle de un cliente con muchos documentos,
    /// y los totales se calculan sobre las filas mostradas. Quien reciba uno de esos papelones no está
    /// viendo la deuda completa sin que el papel lo diga. Las salidas, si el responsable lo decide: un
    /// anexo con el resto del año, partir por periodo, o subir el tope. Hasta entonces, el recorte se
    /// avisa y no se esconde.
    /// </para>
    /// </summary>
    private static string? Aviso(int filas, int documentosEnElFiltro) =>
        documentosEnElFiltro > filas
            ? $"Este estado de cuenta muestra {filas} de los {documentosEnElFiltro} documentos del "
                + "cliente: los importes son los de los documentos mostrados. Pida el detalle completo "
                + "si necesita verlos todos."
            : null;

    private static string? Dato(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string? Importe(decimal? valor, Moneda moneda) =>
        valor is { } importe ? FormatoDocumentoPdf.Importe(importe, moneda) : null;
}