using System.Globalization;
using System.Text;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AtlasRent.Api.Services;

/// <summary>
/// PDF del contrato con el diseño de <c>docs/DESIGN.md §7</c>: carta, márgenes de 18 mm, tinta
/// <c>#1D1D1F</c> y el acento solo en la marca.
/// <para>
/// <b>Sale de la copia congelada</b> (<see cref="ContratoPlantillaCongelada"/>), no del catálogo: el
/// texto que se imprime es el que había en la plantilla el día en que se aprobó, aunque la
/// plantilla se haya editado o desactivado después.
/// </para>
/// <para>
/// <b>Contenido de usuario, nunca instrucción.</b> El texto de la plantilla y el de los anexos los
/// escribe un empleado y los firma un cliente, así que van con los elementos de texto de QuestPDF,
/// que no interpretan nada: ni HTML, ni marcado, ni plantillas. Regla de <c>docs/DESIGN.md §7</c>
/// ("Los textos de usuario (descripciones, notas) se escriben como texto plano; nunca se
/// interpretan como formato") y de <c>docs/SEGURIDAD.md</c>. No hay nada en este archivo que
/// evalúe lo que viene de la base: los títulos y los cuerpos se imprimen tal cual.
/// </para>
/// <para>
/// TODO(A#): las fuentes del design system son Geist y Geist Mono y hay que incrustarlas; cuando
/// lleguen, se separan los importes en mono, igual que en <see cref="DocumentoCotizacionPdf"/>.
/// </para>
/// </summary>
internal static class DocumentoContratoPdf
{
    private const string Tinta = "#1D1D1F";
    private const string Apagado = "#6E6E73";
    private const string Acento = "#AD2275";
    private const string Separador = "#E0E0E0";

    private const float Cuerpo = 9.5f;
    private const float Sobrelinea = 7.5f;

    /// <summary>
    /// Dibuja el contrato y devuelve el PDF en memoria. No escribe nada en disco: lo sirve la API.
    /// </summary>
    /// <param name="contrato">Contrato con sus datos de cliente, unidad y facturadora cargados.</param>
    /// <param name="congelada">Copia congelada de la plantilla y de sus anexos (§3.5).</param>
    /// <param name="documentoCliente">Documento de identidad del cliente, ya descifrado (D16).</param>
    public static byte[] Generar(
        Contrato contrato,
        ContratoPlantillaCongelada congelada,
        string? documentoCliente)
    {
        return Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                // Carta: 8,5 × 11 pulgadas (docs/DESIGN.md §7).
                pagina.Size(612, 792);
                pagina.Margin(18, Unit.Millimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(Cuerpo).FontColor(Tinta));

                pagina.Header().Column(columna =>
                {
                    columna.Item().Row(fila => Cabecera(fila, contrato));
                    columna.Item().PaddingTop(6)
                        .BorderBottom(0.5f)
                        .BorderColor(Acento)
                        .Text(" ")
                        .FontSize(1);
                });

                pagina.Content().PaddingVertical(14).Column(columna =>
                {
                    columna.Item().Element(c => Cliente(c, contrato, documentoCliente));
                    columna.Item().PaddingTop(14).Element(c => Condiciones(c, contrato));
                    columna.Item().PaddingTop(14).Element(c => TextoCongelado(c, congelada));
                    Anexos(columna, congelada);

                    // TODO(A#) F2-08: aquí va la relación de inventario del contrato. No se inventa
                    // nada: el inventario lo copia y congela el propio F2-08 al activar, y hasta que
                    // exista el PDF sale sin él en vez de con un hueco que parece un olvido.
                });

                pagina.Footer().Element(c => Pie(c, congelada));
            });
        }).GeneratePdf();
    }

    private static void Cabecera(RowDescriptor fila, Contrato contrato)
    {
        fila.RelativeItem().Column(lockup =>
        {
            lockup.Item().Text(texto => texto.Span("Atlas Rent").FontSize(13).SemiBold());
            lockup.Item().PaddingTop(1).Text(texto => texto
                .Span("by Atlas Labs").FontSize(Sobrelinea).FontColor(Apagado));
            lockup.Item().PaddingTop(1).Text(texto => texto
                .Span(contrato.Facturadora?.Nombre ?? string.Empty).FontSize(Sobrelinea).FontColor(Apagado));
        });

        fila.ConstantItem(190).Column(derecha =>
        {
            derecha.Item().AlignRight().Text(texto => texto
                .Span("Contrato").FontSize(16).SemiBold().FontColor(Acento));
            derecha.Item().AlignRight().Text(texto => texto
                .Span(Fecha(contrato.CreatedAt)).FontColor(Apagado));
            derecha.Item().AlignRight().Text(texto => texto
                .Span(contrato.Moneda.ToString()).FontColor(Apagado));
        });
    }

    private static void Cliente(IContainer contenedor, Contrato contrato, string? documentoCliente)
    {
        contenedor.Column(columna =>
        {
            columna.Item().Text("Arrendatario").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
            columna.Item().PaddingTop(4).Text(contrato.Cliente?.Nombre ?? "Cliente sin nombre").SemiBold();
            columna.Item().Text(Dato(documentoCliente, "Documento"));
            columna.Item().Text(Dato(contrato.Cliente?.Direccion, "Dirección"));
            columna.Item().Text(Dato(contrato.Cliente?.Email, "Contacto"));
            columna.Item().Text(Dato(contrato.Unidad?.Nombre, "Unidad"));
        });
    }

    /// <summary>
    /// Los pactos del contrato: fechas, renta con su moneda, fianza y renovación (§3.5). Los
    /// importes van con el formato de <c>docs/DESIGN.md §5</c>.
    /// </summary>
    private static void Condiciones(IContainer contenedor, Contrato contrato)
    {
        contenedor.Column(columna =>
        {
            columna.Item().PaddingBottom(4)
                .Text("Condiciones").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);

            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Inicio", Fecha(contrato.FechaInicio)));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Duración inicial", $"{contrato.DuracionInicialMeses} meses"));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Fin vigente", Fecha(contrato.FechaFinVigente)));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Renta", $"{Importe(contrato.Renta, contrato.Moneda)} al mes"));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Día de pago", $"El día {contrato.DiaPago} de cada mes"));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(fila, "Fianza", Importe(contrato.Fianza, contrato.Moneda)));
            columna.Item().PaddingVertical(5).BorderBottom(0.5f).BorderColor(Separador)
                .Row(fila => Condicion(
                    fila,
                    "Renovación",
                    $"{TextoRenovacion(contrato)}: {contrato.RenovacionMeses} meses"));
        });
    }

    private static void Condicion(RowDescriptor fila, string etiqueta, string valor)
    {
        fila.RelativeItem().Text(etiqueta).FontColor(Apagado);
        fila.RelativeItem().AlignRight().Text(valor);
    }

    /// <summary>
    /// El texto de la plantilla congelada. <see cref="Text"/> de QuestPDF escribe la cadena tal
    /// cual: es el punto donde el contenido de usuario entra en el documento y no hay nada que lo
    /// interprete.
    /// </summary>
    private static void TextoCongelado(IContainer contenedor, ContratoPlantillaCongelada congelada)
    {
        contenedor.Column(columna =>
        {
            columna.Item().PaddingBottom(4).Row(fila =>
            {
                fila.RelativeItem().Text(congelada.Nombre).SemiBold();
                fila.RelativeItem().AlignRight().Text(texto => texto
                    .Span($"Plantilla {congelada.Codigo} · congelada el {Fecha(congelada.CongeladaAt)}")
                    .FontSize(Sobrelinea).FontColor(Apagado));
            });

            foreach (var seccion in congelada.Secciones)
            {
                columna.Item().PaddingTop(8).Element(c => Seccion(c, seccion));
            }
        });
    }

    private static void Anexos(ColumnDescriptor columna, ContratoPlantillaCongelada congelada)
    {
        // Cada anexo es un elemento más de la columna del documento, y empieza en página nueva:
        // son documentos que van detrás del contrato, no apartados de él.
        foreach (var anexo in congelada.Anexos)
        {
            columna.Item().PageBreak();
            columna.Item().Column(caja =>
            {
                caja.Item().PaddingBottom(4).Row(fila =>
                {
                    fila.RelativeItem().Text($"Anexo · {anexo.Nombre}").SemiBold();
                    fila.RelativeItem().AlignRight().Text(texto => texto
                        .Span(anexo.Tipo.ToString()).FontSize(Sobrelinea).FontColor(Apagado));
                });

                foreach (var seccion in anexo.Secciones)
                {
                    caja.Item().PaddingTop(8).Element(c => Seccion(c, seccion));
                }
            });
        }
    }

    /// <summary>Un bloque de texto de la plantilla o del anexo, impreso como texto plano.</summary>
    private static void Seccion(IContainer contenedor, SeccionTexto seccion)
    {
        contenedor.Column(columna =>
        {
            if (!string.IsNullOrWhiteSpace(seccion.Titulo))
            {
                columna.Item().Text(seccion.Titulo.Trim()).SemiBold();
            }

            columna.Item().Text(seccion.Texto);
        });
    }

    private static void Pie(IContainer contenedor, ContratoPlantillaCongelada congelada)
    {
        contenedor.BorderTop(0.5f).BorderColor(Separador).PaddingTop(6).Row(fila =>
        {
            fila.RelativeItem().Text(texto => texto
                .Span($"Atlas Rent · plantilla {congelada.Codigo} congelada el {Fecha(congelada.CongeladaAt)}")
                .FontColor(Apagado));
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

    /// <summary>Importe con el formato de <c>docs/DESIGN.md §5</c>: <c>1.250,00 USD</c>.</summary>
    private static string Importe(decimal valor, Moneda moneda) => $"{Numero(valor)} {moneda}";

    /// <summary>
    /// Número con punto de miles y coma decimal (<c>docs/DESIGN.md §5</c>), a mano y no con la
    /// cultura del servidor.
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

    /// <summary>Fecha de negocio en <c>dd/mm/aaaa</c> (<c>docs/DESIGN.md §5</c>).</summary>
    private static string Fecha(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Fecha(DateTimeOffset momento) =>
        momento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// La regla de renovación en palabras. Se escribe el nombre del bloque que describe
    /// (<see cref="ReglaRenovacion"/>) y no los meses: los meses van aparte, en su propia línea, que
    /// es donde el lector los busca.
    /// </summary>
    private static string TextoRenovacion(Contrato contrato) => contrato.ReglaRenovacion switch
    {
        ReglaRenovacion.Mensual => "Bloques de un mes",
        ReglaRenovacion.Trimestral => "Bloques de tres meses",
        ReglaRenovacion.Semestral => "Bloques de seis meses",
        ReglaRenovacion.Anual => "Bloques de doce meses",
        ReglaRenovacion.Personalizada => "Bloques del tamaño pactado",
        _ => contrato.ReglaRenovacion.ToString(),
    };

    /// <summary>"Etiqueta: valor", o nada si el valor no está.</summary>
    private static string? Dato(string? valor, string etiqueta) =>
        string.IsNullOrWhiteSpace(valor) ? null : $"{etiqueta}: {valor.Trim()}";
}