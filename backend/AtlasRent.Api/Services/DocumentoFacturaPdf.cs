using System.Globalization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lo que el documento de una factura o una proforma <b>va a decir</b> (F4-01), todo en texto ya
/// formateado. No sabe nada de QuestPDF.
/// <para>
/// Existe separado del render porque el texto de un PDF de QuestPDF no se puede leer del archivo:
/// la librería incrusta las fuentes con subconjunto y escribe los caracteres como índices de glifo
/// (ver <c>AyudaPlantilla.PdfAsync</c>). Con el modelo delante, las pruebas afirmar sobre lo que el
/// documento dice sin extraer nada del PDF, y el PDF solo se comprueba en lo estructural.
/// </para>
/// </summary>
/// <param name="Plantilla">Plantilla resuelta de la serie, ya validada contra el catálogo.</param>
/// <param name="TipoDocumento">«Factura» o «Proforma»: lo que dice la serie, no el estado.</param>
/// <param name="Numero">Prefijo de serie más número, o el texto de borrador sin número.</param>
/// <param name="MarcaAgua">«ANULADA» en una factura anulada; nulo en las demás.</param>
internal sealed record DocumentoFacturaPdfModelo(
    PlantillaPdf Plantilla,
    string TipoDocumento,
    string Numero,
    string FechaEmision,
    string FechaVencimiento,
    string? ComprobanteFiscal,
    string Moneda,
    DocumentoFacturaPdfEmpresa? Empresa,
    DocumentoFacturaPdfCliente Cliente,
    IReadOnlyList<DocumentoFacturaPdfLinea> Lineas,
    IReadOnlyList<DocumentoFacturaPdfTotal> Totales,
    string? MarcaAgua,
    DocumentoFacturaPdfPie Pie);

/// <summary>
/// Datos de la empresa facturadora que van en la cabecera (<c>docs/DESIGN.md §7</c>). El bloque
/// entero es nulo cuando la factura no tiene a quién ponerle esos datos.
/// </summary>
internal sealed record DocumentoFacturaPdfEmpresa(
    string Nombre,
    string? Rnc,
    string? Direccion,
    string? Telefono,
    string? Email);

/// <summary>Bloque de cliente. El documento va completo (D16): sin enmascarar.</summary>
internal sealed record DocumentoFacturaPdfCliente(
    string Nombre,
    string? Documento,
    string? Direccion,
    string? Contacto,
    string? Unidad,
    string? Contrato);

/// <summary>Una línea del documento, con sus tres números ya formateados.</summary>
internal sealed record DocumentoFacturaPdfLinea(
    string Concepto,
    string Cantidad,
    string PrecioUnitario,
    string Importe);

/// <summary>Una fila de totales, ya formateada. El total va destacado (600).</summary>
internal sealed record DocumentoFacturaPdfTotal(string Etiqueta, string Valor, bool Destacado);

/// <summary>
/// Pie del documento. Los dos textos pueden venir nulos y entonces no se imprimen.
/// <para>
/// El «Página x de y» no está aquí porque no se puede saber antes de componer: lo cuenta la
/// biblioteca mientras dibuja.
/// </para>
/// </summary>
internal sealed record DocumentoFacturaPdfPie(string? CondicionesPago, string? ReferenciaEfactura);

/// <summary>
/// PDF de la factura y de la proforma con el diseño de <c>docs/DESIGN.md §7</c>: carta, márgenes de
/// 18 mm, tinta <c>#1D1D1F</c> y el acento solo en la marca. Sin HTML ni plantillas: se compone por
/// código.
/// <para>
/// <b>Modelo y render van separados a propósito.</b> <see cref="Modelo"/> decide y formatea —todo lo
/// que el documento dice— sin tocar QuestPDF, y <see cref="Generar"/> solo pinta. Es lo que permite
/// probar el contenido del documento: el texto de un PDF de QuestPDF no se puede leer del archivo.
/// </para>
/// <para>
/// TODO(A#): las fuentes del design system son Geist y Geist Mono y hay que incrustarlas. Los
/// archivos no están en el repositorio (docs/DESIGN.md §8 los deja como pendiente), así que el
/// documento sale con la tipografía que trae la librería y el número no va en mono. Cuando
/// lleguen, se añaden con <c>FontManager.RegisterFont</c>, que es exactamente lo que falta.
/// </para>
/// <para>
/// <b>Todo lo que venga del usuario (conceptos, nombre del cliente, notas) se escribe con los
/// elementos de texto de la librería, que no interpretan nada: ni HTML, ni marcado, ni plantillas.</b>
/// Regla de <c>docs/DESIGN.md §7</c> y de <c>docs/SEGURIDAD.md</c> (SEC-001). No hay nada en este
/// archivo que evalúe lo que viene de la base.
/// </para>
/// </summary>
internal static class DocumentoFacturaPdf
{
    private const string Tinta = "#1D1D1F";
    private const string Apagado = "#6E6E73";
    private const string Acento = "#AD2275";
    private const string Separador = "#E0E0E0";
    private const string Marca = "#D0D0D5";

    private const float Cuerpo = 9.5f;
    private const float Sobrelinea = 7.5f;

    private const float AnchoTipo = 20f;
    private const float AnchoNumero = 24f;
    private const float AnchoNumeroDato = 175f;
    private const float AnchoCantidad = 55f;
    private const float AnchoPrecio = 85f;
    private const float AnchoImporte = 95f;
    private const float AnchoTotales = 210f;
    private const float AnchoPie = 150f;

    private const string TextoAnulada = "ANULADA";

    /// <summary>
    /// Monta el modelo: decide qué dice el documento y con qué formato. No toca QuestPDF.
    /// </summary>
    /// <param name="factura">Factura con cliente, unidad, serie y líneas cargados.</param>
    /// <param name="plantilla">Plantilla ya resuelta de su serie (nunca una desconocida).</param>
    /// <param name="empresaFacturadora">
    /// Empresa que va en la cabecera, ya resuelta por el servicio, o nula si la factura no tiene
    /// ninguna de las dos posibles.
    /// </param>
    /// <param name="documentoCliente">Documento de identidad del cliente, ya descifrado (D16).</param>
    /// <param name="situacionCobro">
    /// Cobrado y pendiente de <c>Dominio/CalculoCobro</c>, o nulo si la factura no tiene cobros: los
    /// totales del documento salen de los datos ya calculados del dominio, no se recalculan aquí.
    /// </param>
    /// <param name="comprobanteFiscal">Comprobante fiscal de la factura, si la serie lo genera.</param>
    public static DocumentoFacturaPdfModelo Modelo(
        Factura factura,
        PlantillaPdf plantilla,
        Subempresa? empresaFacturadora,
        string? documentoCliente,
        SituacionCobroFactura? situacionCobro,
        ComprobanteFiscal? comprobanteFiscal)
    {
        var serie = factura.Serie;
        var cliente = factura.Cliente;
        var unidadNombre = factura.Unidad?.Nombre ?? factura.Contrato?.Unidad?.Nombre;

        var totales = new List<DocumentoFacturaPdfTotal>
        {
            new("Subtotal", Importe(factura.Subtotal, factura.Moneda), false),
        };

        // Cada uno de los dos sale solo si lo hay. Una fila con un cero y su etiqueta es ruido: la
        // factura de una línea sin ITBIS no lleva una fila que diga que no lleva impuesto, ni una que
        // diga que no retiene.
        if (factura.Itbis != 0m)
        {
            totales.Add(new DocumentoFacturaPdfTotal("ITBIS", Importe(factura.Itbis, factura.Moneda), false));
        }

        if (factura.Retencion != 0m)
        {
            totales.Add(new DocumentoFacturaPdfTotal("Retención", Importe(factura.Retencion, factura.Moneda), false));
        }

        totales.Add(new DocumentoFacturaPdfTotal("Total", Importe(factura.Total, factura.Moneda), true));

        // El neto a cobrar es el total menos la retención (F3-04, §3.9): es lo que el cliente
        // paga, y por eso es la última fila y va destacada.
        totales.Add(new DocumentoFacturaPdfTotal("Neto a cobrar", Importe(factura.NetoACobrar, factura.Moneda), true));

        // Pagado y pendiente solo cuando la factura tiene cobros. Sin ellos son los dos ceros del
        // valor por defecto y son ruido; con ellos, un pendiente de 0,00 sí dice algo —que está
        // pagada— y por eso la fila se queda.
        if (situacionCobro is not null)
        {
            totales.Add(new DocumentoFacturaPdfTotal("Pagado", Importe(situacionCobro.Cobrado, factura.Moneda), false));
            totales.Add(new DocumentoFacturaPdfTotal("Pendiente", Importe(situacionCobro.Pendiente, factura.Moneda), false));
        }

        return new DocumentoFacturaPdfModelo(
            plantilla,
            TipoDocumento(serie?.Tipo),
            NumeroDocumento(factura, serie?.Prefijo),
            factura.FechaEmision is DateOnly emitido ? FormatoDocumentoPdf.Fecha(emitido) : "Sin emitir",
            FormatoDocumentoPdf.Fecha(factura.FechaVencimiento),
            comprobanteFiscal is null ? null : $"{comprobanteFiscal.Tipo} {NumeroComprobante(comprobanteFiscal)}",
            factura.Moneda.ToString(),
            Empresa(empresaFacturadora),
            new DocumentoFacturaPdfCliente(
                cliente?.Nombre ?? "Cliente sin nombre",
                documentoCliente,
                cliente?.Direccion,
                cliente?.Email ?? cliente?.Telefono,
                unidadNombre,
                factura.ContratoId is Guid contratoId ? contratoId.ToString("D") : null),
            [.. factura.Lineas
                .OrderBy(l => l.Posicion)
                .Select(l => new DocumentoFacturaPdfLinea(
                    l.Concepto,
                    FormatoDocumentoPdf.Cantidad(l.Cantidad),
                    Importe(l.PrecioUnitario, factura.Moneda),
                    Importe(l.Importe, factura.Moneda)))],
            totales,
            // La marca de agua es de estado, no de color: una factura anulada se sigue descargando
            // (es lo que necesita el expediente del cliente) y lo que dice es que está anulada.
            factura.Estado == EstadoFactura.Anulada ? TextoAnulada : null,
            new DocumentoFacturaPdfPie(serie?.CondicionesPago, factura.ReferenciaEfactura));
    }

    /// <summary>
    /// Dibuja el modelo y devuelve el PDF en memoria. No escribe nada en disco: lo sirve la API.
    /// </summary>
    public static byte[] Generar(DocumentoFacturaPdfModelo modelo)
    {
        // El punto de extensión es real, no decorativo: el compositor elige diseño por el nombre de
        // la plantilla, y un nombre que se colara sin estar en el catálogo es un 409 y no un
        // documento con el diseño de otro.
        return modelo.Plantilla.Nombre switch
        {
            var nombre when nombre == RegistroPlantillasPdf.NombrePorDefecto => Dibujar(modelo),
            var otra => throw new InvalidOperationException(
                $"No hay compositor para la plantilla de PDF «{otra}»."),
        };
    }

    private static byte[] Dibujar(DocumentoFacturaPdfModelo modelo) =>
        Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                // Carta: 612 × 792 puntos (docs/DESIGN.md §7). Las dimensiones vienen de la
                // plantilla y no están escritas aquí, que es lo que hace que el catálogo decida.
                pagina.Size(modelo.Plantilla.Ancho, modelo.Plantilla.Alto);
                pagina.Margin(18, Unit.Millimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(Cuerpo).FontColor(Tinta));

                pagina.Header().Element(c => Cabecera(c, modelo));
                pagina.Content().PaddingVertical(14).Element(c => Contenido(c, modelo));
                pagina.Footer().Element(c => Pie(c, modelo));
            });
        }).GeneratePdf();

    /// <summary>
    /// Lockup de marca y datos de la facturadora a la izquierda; tipo de documento, número, fechas,
    /// comprobante y moneda a la derecha (<c>docs/DESIGN.md §7</c>). Debajo, una línea fina de
    /// acento y nada más de color.
    /// </summary>
    private static void Cabecera(IContainer contenedor, DocumentoFacturaPdfModelo modelo)
    {
        contenedor.Column(columna =>
        {
            columna.Item().Row(fila =>
            {
                fila.RelativeItem().Column(lockup =>
                {
                    // Lockup de marca (docs/DESIGN.md §6). Sin logo dibujado: §6 dice que no se ha
                    // suministrado.
                    lockup.Item().Text(texto => texto.Span("Atlas Rent").FontSize(13).SemiBold());
                    lockup.Item().PaddingTop(1).Text(texto => texto
                        .Span("by Atlas Labs").FontSize(Sobrelinea).FontColor(Apagado));

                    EmpresaFacturadora(lockup, modelo.Empresa);
                });

                fila.ConstantItem(AnchoNumeroDato).Column(derecha =>
                {
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.TipoDocumento).FontSize(AnchoTipo).SemiBold().FontColor(Acento));
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.Numero).FontSize(AnchoNumero).SemiBold());
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span($"Emitida: {modelo.FechaEmision}").FontColor(Apagado));
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span($"Vence: {modelo.FechaVencimiento}").FontColor(Apagado));

                    // El comprobante y la referencia solo salen si los hay: una serie no fiscal no
                    // tiene comprobante y una factura sin e-factura externa no tiene referencia.
                    if (modelo.ComprobanteFiscal is not null)
                    {
                        derecha.Item().AlignRight().Text(texto => texto
                            .Span($"Comprobante: {modelo.ComprobanteFiscal}"));
                    }

                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.Moneda).FontColor(Apagado));
                });
            });

            // El espacio es lo que le da altura al separador: un contenedor sin contenido no se
            // dibuja.
            columna.Item().PaddingTop(6)
                .BorderBottom(0.5f)
                .BorderColor(Acento)
                .Text(" ")
                .FontSize(1);
        });
    }

    /// <summary>
    /// La empresa facturadora bajo el lockup. Sin bloque si no hay empresa o si no hay nada que
    /// decir: se imprime lo que hay, no una lista de líneas vacías.
    /// </summary>
    private static void EmpresaFacturadora(ColumnDescriptor columna, DocumentoFacturaPdfEmpresa? empresa)
    {
        if (empresa is null)
        {
            return;
        }

        columna.Item().PaddingTop(6).Column(bloque =>
        {
            bloque.Item().Text(empresa.Nombre).SemiBold();

            // El RNC va con su etiqueta y no suelto: en una cabecera con dos nombres, «1-31-12345»
            // sin decir qué es no dice nada.
            if (!string.IsNullOrWhiteSpace(empresa.Rnc))
            {
                bloque.Item().Text(texto => texto.Span($"RNC: {empresa.Rnc.Trim()}").FontColor(Apagado));
            }

            foreach (var dato in new[]
            {
                FormatoDocumentoPdf.Dato(empresa.Direccion, "Dirección"),
                FormatoDocumentoPdf.Dato(empresa.Telefono, "Teléfono"),
                FormatoDocumentoPdf.Dato(empresa.Email, "Email"),
            })
            {
                if (dato is not null)
                {
                    bloque.Item().Text(texto => texto.Span(dato).FontColor(Apagado));
                }
            }
        });
    }

    /// <summary>
    /// Cliente, líneas y totales, con la marca de agua debajo del todo. La marca va en su propia
    /// capa para que quede detrás del contenido y no empuje ni una línea hacia abajo.
    /// </summary>
    private static void Contenido(IContainer contenedor, DocumentoFacturaPdfModelo modelo)
    {
        contenedor.Layers(capas =>
        {
            // Capa declarada antes de la principal: se dibuja debajo (fondo). Al revés sería una
            // marca encima del texto, que es justo lo que no se quiere en una factura anulada.
            if (modelo.MarcaAgua is not null)
            {
                capas.Layer().AlignCenter().AlignMiddle().Text(texto => texto
                    .Span(modelo.MarcaAgua)
                    .FontSize(72)
                    .SemiBold()
                    .FontColor(Marca));
            }

            capas.PrimaryLayer().Column(columna =>
            {
                columna.Item().Element(c => Cliente(c, modelo.Cliente));
                columna.Item().PaddingTop(16).Element(c => Lineas(c, modelo));
                columna.Item().PaddingTop(14).Element(c => Totales(c, modelo.Totales));
            });
        });
    }

    private static void Cliente(IContainer contenedor, DocumentoFacturaPdfCliente cliente)
    {
        contenedor.Column(columna =>
        {
            columna.Item().Text("Cliente").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            columna.Item().PaddingTop(4).Text(cliente.Nombre).SemiBold();
            columna.Item().Text(FormatoDocumentoPdf.Dato(cliente.Documento, "Documento"));
            columna.Item().Text(FormatoDocumentoPdf.Dato(cliente.Direccion, "Dirección"));
            columna.Item().Text(FormatoDocumentoPdf.Dato(cliente.Contacto, "Contacto"));
            columna.Item().Text(FormatoDocumentoPdf.Dato(cliente.Unidad, "Unidad"));
            columna.Item().Text(FormatoDocumentoPdf.Dato(cliente.Contrato, "Contrato"));
        });
    }

    /// <summary>
    /// Tabla de líneas sin bandas de color, cabecera en overline gris y separador de 0,5 pt bajo
    /// cada fila (<c>docs/DESIGN.md §7</c>).
    /// </summary>
    private static void Lineas(IContainer contenedor, DocumentoFacturaPdfModelo modelo)
    {
        contenedor.Column(columna =>
        {
            columna.Item().PaddingBottom(4).Row(cabecera =>
            {
                cabecera.RelativeItem().Text("Concepto").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoCantidad).AlignRight().Text("Cant.").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoPrecio).AlignRight().Text("Precio").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Importe").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            });

            foreach (var linea in modelo.Lineas)
            {
                // Separador de 0,5 pt bajo cada línea. Va en la misma cadena que la fila porque el
                // contenedor de un Element solo admite un hijo.
                columna.Item().PaddingVertical(5)
                    .BorderBottom(0.5f)
                    .BorderColor(Separador)
                    .Row(fila =>
                    {
                        fila.RelativeItem().Text(linea.Concepto);
                        fila.ConstantItem(AnchoCantidad).AlignRight().Text(linea.Cantidad);
                        fila.ConstantItem(AnchoPrecio).AlignRight().Text(linea.PrecioUnitario);
                        fila.ConstantItem(AnchoImporte).AlignRight().Text(linea.Importe);
                    });
            }
        });
    }

    /// <summary>Totales a la derecha, con la fila destacada en 600 (<c>docs/DESIGN.md §7</c>).</summary>
    private static void Totales(IContainer contenedor, IReadOnlyList<DocumentoFacturaPdfTotal> totales)
    {
        contenedor.AlignRight().Column(columna =>
        {
            foreach (var total in totales)
            {
                columna.Item().PaddingTop(3).Width(AnchoTotales).Row(fila =>
                {
                    var etiqueta = fila.RelativeItem().Text(total.Etiqueta);
                    var valor = fila.RelativeItem().AlignRight().Text(total.Valor);

                    if (total.Destacado)
                    {
                        etiqueta.SemiBold();
                        valor.SemiBold();
                    }
                });
            }
        });
    }

    /// <summary>
    /// Pie: condiciones de pago de la serie, referencia de e-factura externa (D5) y el número de
    /// página. Ese último lo cuenta la biblioteca mientras dibuja, y por eso no está en el modelo.
    /// </summary>
    private static void Pie(IContainer contenedor, DocumentoFacturaPdfModelo modelo)
    {
        contenedor.BorderTop(0.5f).BorderColor(Separador).PaddingTop(6).Column(columna =>
        {
            if (modelo.Pie.CondicionesPago is not null)
            {
                columna.Item().Text(modelo.Pie.CondicionesPago.Trim()).FontColor(Apagado);
            }

            if (modelo.Pie.ReferenciaEfactura is not null)
            {
                columna.Item().Text(texto => texto
                    .Span($"E-factura: {modelo.Pie.ReferenciaEfactura.Trim()}").FontColor(Apagado));
            }

            columna.Item().PaddingTop(4).Row(fila =>
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
        });
    }

    // ---------------------------------------------------------------- decisiones del modelo

    /// <summary>
    /// «Factura» o «Proforma»: lo dice el tipo de la serie (§3.7), no el estado. Una proforma
    /// convertida sigue siendo una proforma que se emitió: el número que tiene es el de la serie de
    /// la factura, pero el papel que se generó era una proforma.
    /// </summary>
    private static string TipoDocumento(TipoSerie? tipo) => tipo == TipoSerie.Proforma ? "Proforma" : "Factura";

    /// <summary>
    /// Prefijo de serie más número. Un borrador no tiene número (no lo consume, D28) y el documento
    /// lo dice en claro en vez de imprimir un número vacío que parece un fallo.
    /// </summary>
    private static string NumeroDocumento(Factura factura, string? prefijo)
    {
        if (factura.Numero is not int numero)
        {
            return "Borrador sin número";
        }

        return string.IsNullOrWhiteSpace(prefijo) ? numero.ToString() : $"{prefijo.Trim()}-{numero}";
    }

    /// <summary>
    /// El comprobante con su tipo y su número, tal cual. El número no lleva ceros delante porque el
    /// repositorio no fija ese formato en ningún sitio (<c>TODO(A#)</c>, con F0-06): inventar un
    /// relleno que después resulta ser otro en el papel del emisor es peor que imprimir el número
    /// entero.
    /// </summary>
    private static string NumeroComprobante(ComprobanteFiscal comprobante) =>
        comprobante.Numero.ToString(CultureInfo.InvariantCulture);

    private static string Importe(decimal valor, Moneda moneda) => FormatoDocumentoPdf.Importe(valor, moneda);

    private static DocumentoFacturaPdfEmpresa? Empresa(Subempresa? subempresa) =>
        subempresa is null
            ? null
            : new DocumentoFacturaPdfEmpresa(
                subempresa.Nombre,
                subempresa.Rnc,
                subempresa.Direccion,
                subempresa.Telefono,
                subempresa.Email);
}
