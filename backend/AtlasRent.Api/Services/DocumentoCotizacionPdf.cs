using System.Globalization;
using System.Text;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AtlasRent.Api.Services;

/// <summary>
/// PDF de la cotización con el diseño de <c>docs/DESIGN.md §7</c>: carta, márgenes de 18 mm, tinta
/// <c>#1D1D1F</c> y el acento solo en la marca. Sin HTML ni plantillas: se compone por código.
/// <para>
/// TODO(A#): las fuentes del design system son Geist y Geist Mono y hay que incrustarlas. Los
/// archivos no están en el repositorio (docs/DESIGN.md §8 los deja como pendiente), así que el
/// documento sale con la tipografía que trae la librería. Cuando lleguen, se añaden con
/// <c>FontManager.RegisterFont</c> y aquí se separan los importes en mono.
/// </para>
/// <para>
/// TODO(A#): este PDF es el básico sin plantillas por serie. F4-01 mete la plantilla, los datos de
/// la empresa facturadora y las condiciones de pago de la serie.
/// </para>
/// <para>
/// Todo lo que venga del usuario (descripciones y nombres) se escribe con los elementos de texto de
/// la librería, que no interpretan nada: ni HTML ni formato. Regla de seguridad de
/// <c>docs/SEGURIDAD.md</c>, nada de lo que escriba un cliente llega al PDF como instrucción.
/// </para>
/// </summary>
internal static class DocumentoCotizacionPdf
{
    private const string Tinta = "#1D1D1F";
    private const string Apagado = "#6E6E73";
    private const string Acento = "#AD2275";
    private const string Separador = "#E0E0E0";

    private const float Cuerpo = 9.5f;
    private const float Sobrelinea = 7.5f;

    /// <summary>
    /// Dibuja la cotización y devuelve el PDF en memoria. No escribe nada en disco: lo sirve la API.
    /// </summary>
    /// <param name="documentoDocumento">Documento de identidad del cliente, ya descifrado (D16).</param>
    /// <param name="documentoProspecto">Documento de identidad del prospecto, si es el destinatario.</param>
    public static byte[] Generar(Cotizacion cotizacion, string? documentoDocumento, string? documentoProspecto)
    {
        var lineas = cotizacion.Lineas.OrderBy(l => l.Posicion).ToList();
        var total = Redondeo.Importe(cotizacion.Total);

        return Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                // Carta: 8,5 × 11 pulgadas, que en puntos son 612 × 792 (docs/DESIGN.md §7).
                pagina.Size(612, 792);
                pagina.Margin(18, Unit.Millimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(Cuerpo).FontColor(Tinta));

                pagina.Header().Column(columna =>
                {
                    columna.Item().Row(fila => Cabecera(fila, cotizacion));
                    // Una línea fina de acento bajo la cabecera y nada más de color (§7). El espacio es lo que le
                    // da altura al separador: un contenedor sin contenido no se dibuja.
                    columna.Item().PaddingTop(6)
                        .BorderBottom(0.5f)
                        .BorderColor(Acento)
                        .Text(" ")
                        .FontSize(1);
                });

                pagina.Content().PaddingVertical(14).Column(columna =>
                {
                    columna.Item().Element(c => Destinatario(c, cotizacion, documentoDocumento, documentoProspecto));
                    columna.Item().PaddingTop(16).Element(c => Lineas(c, lineas, cotizacion.Moneda));
                    columna.Item().PaddingTop(14).Element(c => Totales(c, total, cotizacion.Moneda));
                });

                pagina.Footer().Element(c => Pie(c));
            });
        }).GeneratePdf();
    }

    /// <summary>Lockup de la cabecera: marca a la izquierda y el tipo de documento a la derecha.</summary>
    private static void Cabecera(RowDescriptor fila, Cotizacion cotizacion)
    {
        fila.RelativeItem().Column(lockup =>
        {
            // Lockup de marca (docs/DESIGN.md §6). Sin logo dibujado: §6 dice que no se ha suministrado.
            lockup.Item().Text(texto => texto.Span("Atlas Rent").FontSize(13).SemiBold());
            lockup.Item().PaddingTop(1).Text(texto => texto
                .Span("by Atlas Labs").FontSize(7.5f).FontColor(Apagado));
        });

        fila.ConstantItem(190).Column(derecha =>
        {
            derecha.Item().AlignRight().Text(texto => texto
                .Span("Cotización").FontSize(16).SemiBold().FontColor(Acento));
            derecha.Item().AlignRight().Text(texto => texto
                .Span(Fecha(cotizacion.CreatedAt)).FontColor(Apagado));
            // Sin número: la especificación no pide uno (ver el TODO de la entidad).
            derecha.Item().AlignRight().Text(texto => texto
                .Span(cotizacion.Moneda.ToString()).FontColor(Apagado));
        });
    }

    private static void Destinatario(
        IContainer contenedor,
        Cotizacion cotizacion,
        string? documentoCliente,
        string? documentoProspecto)
    {
        var nombre = cotizacion.Cliente?.Nombre ?? cotizacion.Prospecto?.Nombre ?? "Destinatario sin nombre";
        var documento = cotizacion.Cliente is not null ? documentoCliente : documentoProspecto;
        var contacto = cotizacion.Cliente?.Email ?? cotizacion.Prospecto?.Email;

        contenedor.Column(columna =>
        {
            columna.Item().Text("Destinatario").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            columna.Item().PaddingTop(4).Text(nombre).SemiBold();
            columna.Item().Text(Dato(documento, "Documento"));
            columna.Item().Text(Dato(contacto, "Contacto"));
            if (cotizacion.Cliente is not null)
            {
                columna.Item().Text(Dato(cotizacion.Cliente.Direccion, "Dirección"));
            }

            columna.Item().Text(Dato(cotizacion.Unidad?.Nombre, "Unidad"));
            columna.Item().PaddingTop(6).Text(Dato(TextoEstado(cotizacion.Estado), "Estado")).FontColor(Apagado);
        });
    }

    private static void Lineas(IContainer contenedor, List<CotizacionLinea> lineas, Moneda moneda)
    {
        contenedor.Column(columna =>
        {
            // Cabecera de la tabla en overline gris, sin bandas de color (§7).
            columna.Item().PaddingBottom(4).Row(cabecera =>
            {
                cabecera.RelativeItem().Text("Concepto").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(60).AlignRight().Text("Cant.").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(85).AlignRight().Text("Precio").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(95).AlignRight().Text("Importe").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            });

            foreach (var linea in lineas)
            {
                // Separador de 0,5 pt bajo cada línea (docs/DESIGN.md §7). Va en la misma cadena que
                // la fila porque el contenedor de un Element solo admite un hijo.
                columna.Item().PaddingVertical(5)
                    .BorderBottom(0.5f)
                    .BorderColor(Separador)
                    .Row(fila =>
                    {
                        fila.RelativeItem().Text(linea.Descripcion);
                        fila.ConstantItem(60).AlignRight().Text(Numero(linea.Cantidad));
                        fila.ConstantItem(85).AlignRight().Text(Importe(linea.PrecioUnitario, moneda));
                        fila.ConstantItem(95).AlignRight().Text(Importe(linea.Importe, moneda));
                    });
            }
        });
    }

    private static void Totales(IContainer contenedor, decimal total, Moneda moneda)
    {
        contenedor.AlignRight().Column(columna =>
        {
            columna.Item().Width(200).Row(fila =>
            {
                fila.RelativeItem().Text("Subtotal");
                fila.RelativeItem().AlignRight().Text(Importe(total, moneda));
            });

            // Una cotización no lleva ITBIS ni retenciones: la especificación solo las pide en facturas
            // y proformas (§3.8), así que el subtotal y el total son la misma suma.
            columna.Item().PaddingTop(3).Width(200).Row(fila =>
            {
                fila.RelativeItem().Text("Total").SemiBold();
                fila.RelativeItem().AlignRight().Text(Importe(total, moneda)).SemiBold();
            });
        });
    }

    private static void Pie(IContainer contenedor)
    {
        contenedor.BorderTop(0.5f).BorderColor(Separador).PaddingTop(6).Row(fila =>
        {
            fila.RelativeItem().Text("Atlas Rent").FontColor(Apagado);
            fila.ConstantItem(160).AlignRight().Text(texto =>
            {
                texto.Span("Página ").FontSize(8).FontColor(Apagado);
                texto.CurrentPageNumber().FontSize(8).FontColor(Apagado);
                texto.Span(" de ").FontSize(8).FontColor(Apagado);
                texto.TotalPages().FontSize(8).FontColor(Apagado);
            });
        });
    }

    // ---------------------------------------------------------------- formato

    /// <summary>Importe con el formato de docs/DESIGN.md §5: <c>1.250,00 USD</c>.</summary>
    private static string Importe(decimal valor, Moneda moneda) => $"{Numero(valor)} {moneda}";

    /// <summary>
    /// Número con punto de miles y coma decimal (docs/DESIGN.md §5). Se hace a mano y no con la
    /// cultura del servidor: un documento con <c>1.250,00</c> en Windows y <c>1,250.00</c> en otro
    /// equipo no es el mismo documento.
    /// </summary>
    private static string Numero(decimal valor)
    {
        var texto = Math.Abs(valor).ToString("F2", CultureInfo.InvariantCulture);
        var corte = texto.IndexOf('.', StringComparison.Ordinal);
        var entero = corte < 0 ? texto : texto[..corte];
        var decimales = corte < 0 ? string.Empty : texto[corte..];

        var conSeparadores = new StringBuilder();
        for (var i = 0; i < entero.Length; i++)
        {
            if (i > 0 && (entero.Length - i) % 3 == 0)
            {
                conSeparadores.Append('.');
            }

            conSeparadores.Append(entero[i]);
        }

        return (valor < 0 ? "-" : string.Empty) + conSeparadores + decimales.Replace('.', ',');
    }

    /// <summary>Fecha en <c>dd/mm/aaaa</c> (docs/DESIGN.md §5).</summary>
    private static string Fecha(DateTimeOffset momento) =>
        momento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Etiqueta: valor", o nada si el valor no está.</summary>
    private static string? Dato(string? valor, string etiqueta) =>
        string.IsNullOrWhiteSpace(valor) ? null : $"{etiqueta}: {valor.Trim()}";

    /// <summary>Los estados son texto, nunca un bloque de color (docs/DESIGN.md §7).</summary>
    private static string TextoEstado(EstadoCotizacion estado) => estado switch
    {
        EstadoCotizacion.Borrador => "Borrador",
        EstadoCotizacion.Enviada => "Enviada",
        EstadoCotizacion.Aceptada => "Aceptada",
        EstadoCotizacion.Rechazada => "Rechazada",
        _ => estado.ToString(),
    };
}