using System.Globalization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AtlasRent.Api.Services;

/// <summary>
/// Lo que el recibo de un cobro <b>va a decir</b> (F4-01, <c>docs/ESPECIFICACION.md</c> §3.10.1), todo
/// en texto ya formateado. No sabe nada de QuestPDF.
/// <para>
/// Separado del render por la misma razón que en <see cref="DocumentoFacturaPdf"/>: el texto de un PDF
/// de QuestPDF no se puede leer del archivo, así que con el modelo delante las pruebas afirman sobre lo
/// que el papel dice y del archivo solo comprueban lo estructural.
/// </para>
/// </summary>
/// <param name="Numero">
/// Identificador estable del recibo, <c>REC-XXXXXXXX</c> derivado del identificador del cobro.
/// </param>
/// <param name="MarcaAgua">«ANULADO» en un cobro anulado; nulo en los demás.</param>
internal sealed record DocumentoReciboPdfModelo(
    PlantillaPdf Plantilla,
    string Numero,
    string FechaEfectiva,
    string MonedaRecibida,
    string ImporteRecibido,
    DocumentoFacturaPdfEmpresa? Empresa,
    DocumentoFacturaPdfCliente Cliente,
    IReadOnlyList<DocumentoReciboPdfDato> Datos,
    IReadOnlyList<DocumentoReciboPdfAsignacion> Asignaciones,
    IReadOnlyList<DocumentoFacturaPdfTotal> Totales,
    string? MarcaAgua,
    string? MotivoAnulacion)
{
    /// <summary>
    /// Si el cobro se registró en moneda distinta de la de las facturas (§3.10.2). Entonces cada línea
    /// lleva su importe recibido y su cambio, y las columnas de la tabla cambian.
    /// </summary>
    public bool ConConversion => Asignaciones.Any(a => a.ImporteRecibido is not null);
}

/// <summary>
/// Un dato del cobro, con su etiqueta: forma de pago, cuenta receptora, referencia, tipo de cambio del
/// día, saldo a favor usado y excedente.
/// </summary>
internal sealed record DocumentoReciboPdfDato(string Etiqueta, string Valor);

/// <summary>
/// Una fila de la tabla de asignaciones: a qué documento se aplicó el importe y con qué par de importes.
/// <para>
/// <b>Una asignación anulada sale igual y con su marca</b>, como en <c>GET /api/v1/cobros/{id}</c>:
/// lo que deja de contar no se esconde (§3.10.1, AGENTS.md §6).
/// </para>
/// </summary>
/// <param name="ImporteRecibido">
/// Parte del cobro en la moneda recibida, o nulo cuando el cobro y el documento comparten moneda.
/// </param>
/// <param name="Vigente">Si la asignación cuenta.</param>
internal sealed record DocumentoReciboPdfAsignacion(
    string Destino,
    string Moneda,
    string Importe,
    string? ImporteRecibido,
    string? Cambio,
    bool Vigente);

/// <summary>
/// Recibo de cobro con el diseño de <c>docs/DESIGN.md §7</c>: carta, márgenes de 18 mm, tinta
/// <c>#1D1D1F</c> y el acento solo en la marca. Sin HTML ni plantillas: se compone por código.
/// <para>
/// <b>Modelo y render van separados a propósito</b>, igual que en <see cref="DocumentoFacturaPdf"/>:
/// <see cref="Modelo"/> decide y formatea, <see cref="Generar"/> solo pinta.
/// </para>
/// <para>
/// El bloque de empresa y el de cliente son los de <c>docs/DESIGN.md §7</c>, los mismos que imprime la
/// factura, y se reutilizan sus registros (<see cref="DocumentoFacturaPdfEmpresa"/> y
/// <see cref="DocumentoFacturaPdfCliente"/>) en vez de copiar unos campos que son iguales.
/// </para>
/// <para>
/// <b>Lo que viene del usuario (referencia, nombre del cliente) sale con los elementos de texto de la
/// librería, que no interpretan nada</b>: ni HTML, ni marcado, ni plantillas. Regla de
/// <c>docs/SEGURIDAD.md</c> (SEC-001) y de <c>docs/DESIGN.md §7</c>.
/// </para>
/// <para>
/// TODO(A#): las fuentes del design system son Geist y Geist Mono y hay que incrustarlas; los archivos no
/// están en el repositorio (<c>docs/DESIGN.md §8</c>), así que el número no va en mono. Es el mismo
/// pendiente que el PDF de factura.
/// </para>
/// </summary>
internal static class DocumentoReciboPdf
{
    private const string Tinta = "#1D1D1F";
    private const string Apagado = "#6E6E73";
    private const string Acento = "#AD2275";
    private const string Separador = "#E0E0E0";
    private const string Marca = "#D0D0D5";

    private const float Cuerpo = 9.5f;
    private const float Sobrelinea = 7.5f;
    private const float TamTitulo = 20f;
    private const float TamNumero = 13f;

    private const float AnchoNumeroDato = 175f;
    private const float AnchoDato = 240f;
    private const float AnchoMoneda = 46f;
    private const float AnchoImporte = 100f;
    private const float AnchoCambio = 56f;
    private const float AnchoPie = 150f;

    private const string TextoAnulado = "ANULADO";

    /// <summary>
    /// Monta el modelo: decide qué dice el recibo y con qué formato. No toca QuestPDF.
    /// </summary>
    /// <param name="cobro">
    /// Cobro con cliente, contrato y facturadora, forma de pago, cuenta receptora y asignaciones
    /// cargadas.
    /// </param>
    /// <param name="empresaFacturadora">
    /// Empresa de la cabecera, ya resuelta por el servicio, o nula si el cobro no tiene contrato con
    /// facturadora.
    /// </param>
    /// <param name="documentoCliente">Documento de identidad del cliente, ya descifrado (D16).</param>
    public static DocumentoReciboPdfModelo Modelo(
        Cobro cobro,
        PlantillaPdf plantilla,
        Subempresa? empresaFacturadora,
        string? documentoCliente)
    {
        var datos = new List<DocumentoReciboPdfDato>();

        // Cada dato sale solo si lo hay. Un cero con su etiqueta («Saldo a favor usado: 0,00») es ruido:
        // el lector lo que quiere saber es si se usó crédito o quedó dinero sin aplicar.
        if (Dato(cobro.FormaPago?.Nombre) is { } formaPago)
        {
            datos.Add(new DocumentoReciboPdfDato("Forma de pago", formaPago));
        }

        if (Dato(cobro.CuentaReceptora?.Nombre) is { } cuenta)
        {
            datos.Add(new DocumentoReciboPdfDato("Cuenta receptora", cuenta));
        }

        if (Dato(cobro.Referencia) is { } referencia)
        {
            datos.Add(new DocumentoReciboPdfDato("Referencia", referencia));
        }

        var asignaciones = Asignaciones(cobro);

        // El tipo de cambio del día solo cuando hubo conversión: en USD vale 1 y no hay fila de
        // «tipo_cambio» de la que venga (F3-01, D9), y si el cobro es en USD pero la factura no, el
        // cambio que explica el pago es el de cada línea, que es donde §3.10.2 dice que se guarda.
        if (cobro.MonedaRecibida != Moneda.USD || asignaciones.Any(a => a.ImporteRecibido is not null))
        {
            datos.Add(new DocumentoReciboPdfDato(
                $"Tipo de cambio del {FormatoDocumentoPdf.Fecha(cobro.TipoCambioFecha)}",
                Cambio(cobro.TipoCambioValor)));
        }

        if (cobro.SaldoFavorUsado != 0m)
        {
            datos.Add(new DocumentoReciboPdfDato(
                "Saldo a favor usado", Importe(cobro.SaldoFavorUsado, cobro.MonedaRecibida)));
        }

        if (cobro.Excedente != 0m)
        {
            datos.Add(new DocumentoReciboPdfDato("Excedente", Importe(cobro.Excedente, cobro.MonedaRecibida)));
        }

        return new DocumentoReciboPdfModelo(
            plantilla,
            Identificador(cobro.Id),
            FormatoDocumentoPdf.Fecha(cobro.FechaEfectiva),
            cobro.MonedaRecibida.ToString(),
            Importe(cobro.ImporteRecibido, cobro.MonedaRecibida),
            Empresa(empresaFacturadora),
            new DocumentoFacturaPdfCliente(
                cobro.Cliente?.Nombre ?? "Cliente sin nombre",
                documentoCliente,
                cobro.Cliente?.Direccion,
                cobro.Cliente?.Email ?? cobro.Cliente?.Telefono,
                cobro.Contrato?.Unidad?.Nombre,
                cobro.ContratoId is Guid contratoId ? contratoId.ToString("D") : null),
            datos,
            asignaciones,
            Totales(cobro, asignaciones),
            // La marca de agua es de estado, no de color: un cobro anulado también se descarga, porque el
            // papel marcado es lo que necesita el expediente del cliente, y lo que dice es que está anulado.
            cobro.Estado == EstadoCobro.Anulado ? TextoAnulado : null,
            Dato(cobro.MotivoAnulacion));
    }

    /// <summary>
    /// Dibuja el modelo y devuelve el PDF en memoria. No escribe nada en disco: lo sirve la API.
    /// </summary>
    public static byte[] Generar(DocumentoReciboPdfModelo modelo) =>
        modelo.Plantilla.Nombre == RegistroPlantillasPdf.NombrePorDefecto
            ? Dibujar(modelo)
            : throw new InvalidOperationException(
                $"No hay compositor para la plantilla de PDF «{modelo.Plantilla.Nombre}».");

    private static byte[] Dibujar(DocumentoReciboPdfModelo modelo) =>
        Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(modelo.Plantilla.Ancho, modelo.Plantilla.Alto);
                pagina.Margin(18, Unit.Millimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(Cuerpo).FontColor(Tinta));

                pagina.Header().Element(c => Cabecera(c, modelo));
                pagina.Content().PaddingVertical(14).Element(c => Contenido(c, modelo));
                pagina.Footer().Element(c => Pie(c, modelo));
            });
        }).GeneratePdf();

    /// <summary>
    /// Lockup de marca y empresa a la izquierda; tipo de documento, identificador, fecha y money a la
    /// derecha. El importe recibido es lo que el cliente viene a saldar, así que va destacado.
    /// </summary>
    private static void Cabecera(IContainer contenedor, DocumentoReciboPdfModelo modelo)
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

                    EmpresaFacturadora(lockup, modelo.Empresa);
                });

                fila.ConstantItem(AnchoNumeroDato).Column(derecha =>
                {
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span("Recibo").FontSize(TamTitulo).SemiBold().FontColor(Acento));
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.Numero).FontSize(TamNumero).SemiBold());
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span($"Recibido: {modelo.FechaEfectiva}").FontColor(Apagado));
                    derecha.Item().AlignRight().PaddingTop(2).Text(texto => texto
                        .Span(modelo.ImporteRecibido).SemiBold());
                    derecha.Item().AlignRight().Text(texto => texto
                        .Span(modelo.MonedaRecibida).FontColor(Apagado));
                });
            });

            // El espacio es lo que le da altura al separador: un contenedor sin contenido no se dibuja.
            columna.Item().PaddingTop(6)
                .BorderBottom(0.5f)
                .BorderColor(Acento)
                .Text(" ")
                .FontSize(1);
        });
    }

    /// <summary>
    /// La empresa facturadora bajo el lockup. Sin bloque si no hay empresa o si no hay nada que decir:
    /// se imprime lo que hay, no una lista de líneas vacías.
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
    /// Cliente, datos del cobro, tabla de asignaciones y totales, con la marca de agua debajo del todo.
    /// La marca va en su propia capa para que quede detrás del contenido y no empuje ni una línea.
    /// </summary>
    private static void Contenido(IContainer contenedor, DocumentoReciboPdfModelo modelo)
    {
        contenedor.Layers(capas =>
        {
            // Capa declarada antes de la principal: se dibuja debajo (fondo). Al revés sería una marca
            // encima del texto, que es justo lo que no se quiere en un cobro anulado.
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
                columna.Item().PaddingTop(14).Element(c => Datos(c, modelo.Datos));
                columna.Item().PaddingTop(16).Element(c => Asignaciones(c, modelo));
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

    private static void Datos(IContainer contenedor, IReadOnlyList<DocumentoReciboPdfDato> datos)
    {
        if (datos.Count == 0)
        {
            return;
        }

        contenedor.AlignRight().Column(columna =>
        {
            foreach (var dato in datos)
            {
                columna.Item().PaddingTop(3).Width(AnchoDato).Row(fila =>
                {
                    fila.RelativeItem().Text(dato.Etiqueta);
                    fila.RelativeItem().AlignRight().Text(dato.Valor);
                });
            }
        });
    }

    /// <summary>
    /// Tabla de asignaciones sin bandas de color, cabecera en overline gris y separador de 0,5 pt bajo
    /// cada fila (<c>docs/DESIGN.md §7</c>). Las columnas del par de importes y la del cambio solo
    /// aparecen cuando el cobro se registró en otra moneda (§3.10.2).
    /// </summary>
    private static void Asignaciones(IContainer contenedor, DocumentoReciboPdfModelo modelo)
    {
        if (modelo.Asignaciones.Count == 0)
        {
            // Un cobro sin asignaciones es legal: puede ser un pago suelto o todo crédito. La tabla
            // vacía con su cabecera no diría nada, así que sale una línea que sí lo dice.
            contenedor.Text(texto => texto
                .Span("Este cobro no tiene importes aplicados a facturas ni fianzas.")
                .FontColor(Apagado));

            return;
        }

        contenedor.Column(columna =>
        {
            columna.Item().PaddingBottom(4).Row(cabecera =>
            {
                cabecera.RelativeItem().Text("Aplicado a").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoMoneda).AlignRight().Text("Mon.").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Importe").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);

                if (modelo.ConConversion)
                {
                    cabecera.ConstantItem(AnchoImporte).AlignRight().Text("Recibido").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                    cabecera.ConstantItem(AnchoCambio).AlignRight().Text("Cambio").FontSize(Sobrelinea).SemiBold().FontColor(Apagado);
                }
            });

            foreach (var asignacion in modelo.Asignaciones)
            {
                // Separador de 0,5 pt bajo cada fila, en la misma cadena porque un contenedor de un
                // Element solo admite un hijo.
                columna.Item().PaddingVertical(5)
                    .BorderBottom(0.5f)
                    .BorderColor(Separador)
                    .Row(fila =>
                    {
                        fila.RelativeItem().Text(asignacion.Destino);
                        fila.ConstantItem(AnchoMoneda).AlignRight().Text(asignacion.Moneda);
                        fila.ConstantItem(AnchoImporte).AlignRight().Text(asignacion.Importe);

                        if (modelo.ConConversion)
                        {
                            fila.ConstantItem(AnchoImporte).AlignRight().Text(asignacion.ImporteRecibido);
                            fila.ConstantItem(AnchoCambio).AlignRight().Text(asignacion.Cambio);
                        }
                    });
            }
        });
    }

    /// <summary>Totales a la derecha, con la fila destacada en 600 (<c>docs/DESIGN.md §7</c>).</summary>
    private static void Totales(IContainer contenedor, IReadOnlyList<DocumentoFacturaPdfTotal> totales)
    {
        if (totales.Count == 0)
        {
            return;
        }

        contenedor.AlignRight().Column(columna =>
        {
            foreach (var total in totales)
            {
                columna.Item().PaddingTop(3).Width(AnchoDato).Row(fila =>
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
    /// Pie: el motivo de la anulación si la hay y el número de página. Ese último lo cuenta la
    /// biblioteca mientras dibuja, y por eso no está en el modelo.
    /// </summary>
    private static void Pie(IContainer contenedor, DocumentoReciboPdfModelo modelo)
    {
        contenedor.BorderTop(0.5f).BorderColor(Separador).PaddingTop(6).Column(columna =>
        {
            if (modelo.MotivoAnulacion is not null)
            {
                columna.Item().Text(texto => texto
                    .Span($"Anulado: {modelo.MotivoAnulacion.Trim()}").FontColor(Apagado));
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
    /// Identificador del recibo: <c>REC-</c> y los ocho primeros caracteres del identificador del cobro
    /// en mayúsculas.
    /// <para>
    /// <b>Un cobro no tiene número</b> (<c>Data/Cobro.cs</c>) y la especificación no le pone uno
    /// (§3.10.1 solo dice «recibo PDF opcional»), así que no hay secuencia que consultar ni que gastar.
    /// Lo que hace falta es algo estable: el mismo cobro reimprimido tiene que decir lo mismo, porque
    /// es un documento que la gente archiva y compara con el anterior.
    /// </para>
    /// <para>
    /// TODO(A#): si el responsable quiere una numeración propia de recibos —«REC-000123» con su
    /// contador—, el lugar es este método y una secuencia transaccional como la de facturas (§3.7,
    /// AGENTS.md §6). Un identificador derivado no es un número de documento y no debe presentarse
    /// como si lo fuera.
    /// </para>
    /// </summary>
    private static string Identificador(Guid cobroId) => $"REC-{cobroId.ToString("N")[..8].ToUpperInvariant()}";

    /// <summary>
    /// Las asignaciones del cobro, ordenadas como en <c>GET /api/v1/cobros/{id}</c> (vencimiento, número
    /// y destino) y con los dos importes de §3.10.2 cuando el cobro es en otra moneda.
    /// </summary>
    private static IReadOnlyList<DocumentoReciboPdfAsignacion> Asignaciones(Cobro cobro) =>
        [.. cobro.Asignaciones
            .OrderBy(a => a.Factura?.FechaVencimiento)
            .ThenBy(a => a.Factura?.Numero)
            .ThenBy(a => a.FacturaId)
            .ThenBy(a => a.FianzaId)
            .Select(a =>
            {
                // La tabla lo dice con la moneda de cada destino. Una asignación sin destino cargado solo
                // puede venir de datos escritos por fuera de la API, y entonces se muestra en la moneda
                // del cobro, que es la única que el papel conoce con certeza.
                var moneda = a.Factura?.Moneda ?? a.Fianza?.Moneda ?? cobro.MonedaRecibida;

                return new DocumentoReciboPdfAsignacion(
                    Destino(a, a.Vigente),
                    moneda.ToString(),
                    FormatoDocumentoPdf.Importe(a.Importe, moneda),
                    a.ImporteRecibido is { } recibido
                        ? FormatoDocumentoPdf.Importe(recibido, cobro.MonedaRecibida)
                        : null,
                    a.TipoCambioValor is { } cambio ? Cambio(cambio) : null,
                    a.Vigente);
            })];

    /// <summary>
    /// «Serie 3 · nº 45» de una factura, o el destino de una fianza (§3.11).
    /// <para>
    /// <b>La asignación que no cuenta sale con su marca</b>, y la marca va en el modelo y no en el
    /// dibujo: lo que no cuenta tiene que verse en el papel, y para que eso sea comprobable tiene que
    /// estar en lo que el modelo dice. Es lo mismo criterio que <c>GET /api/v1/cobros/{id}</c>.
    /// </para>
    /// </summary>
    private static string Destino(CobroAsignacion asignacion, bool vigente)
    {
        var destino = Destino(asignacion);

        return vigente ? destino : $"{destino} — anulada";
    }

    private static string Destino(CobroAsignacion asignacion)
    {
        if (asignacion.Factura is Factura factura)
        {
            if (factura.Numero is not int numero)
            {
                return "Factura sin número";
            }

            var prefijo = factura.Serie?.Prefijo;

            return string.IsNullOrWhiteSpace(prefijo) ? $"nº {numero}" : $"{prefijo.Trim()}-{numero}";
        }

        return "Fianza";
    }

    /// <summary>
    /// Totales de lo aplicado, <b>por moneda</b> y sin convertir entre ellas (§6): una línea por
    /// moneda de las facturas y, si el cobro fue en otra moneda, otra por la recibida.
    /// <para>
    /// <b>Suman solo las asignaciones vigentes.</b> Las que no vigoran salen en la tabla con su marca, y
    /// un total que incluyera dinero que no cuenta sería peor que no dar total (§3.13).
    /// </para>
    /// </summary>
    private static IReadOnlyList<DocumentoFacturaPdfTotal> Totales(
        Cobro cobro,
        IReadOnlyList<DocumentoReciboPdfAsignacion> asignaciones)
    {
        var vigentes = asignaciones.Where(a => a.Vigente).ToList();
        if (vigentes.Count == 0)
        {
            return [];
        }

        var totales = new List<DocumentoFacturaPdfTotal>();
        var cobrosPorMoneda = new Dictionary<Moneda, decimal>();

        foreach (var asignacion in cobro.Asignaciones.Where(a => a.Vigente))
        {
            var moneda = asignacion.Factura?.Moneda ?? asignacion.Fianza?.Moneda;
            if (moneda is not { } destino)
            {
                continue;
            }

            cobrosPorMoneda[destino] = Redondeo.Importe(cobrosPorMoneda.GetValueOrDefault(destino) + asignacion.Importe);
        }

        foreach (var moneda in cobrosPorMoneda.Keys.OrderBy(m => m.ToString(), StringComparer.Ordinal))
        {
            totales.Add(new DocumentoFacturaPdfTotal(
                $"Total aplicado ({moneda})",
                FormatoDocumentoPdf.Importe(cobrosPorMoneda[moneda], moneda),
                Destacado: false));
        }

        // El total de lo recibido solo tiene sentido cuando hubo conversión: sin ella, el importe
        // recibido es el propio total aplicado y una fila repetida solo ocupa espacio.
        if (asignaciones.Any(a => a.ImporteRecibido is not null))
        {
            var recibido = Redondeo.Importe(cobro.Asignaciones
                .Where(a => a.Vigente)
                .Sum(a => a.ImporteRecibido ?? 0m));

            totales.Add(new DocumentoFacturaPdfTotal(
                $"Total recibido ({cobro.MonedaRecibida})",
                FormatoDocumentoPdf.Importe(recibido, cobro.MonedaRecibida),
                Destacado: true));
        }

        return totales;
    }

    private static string? Dato(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string Importe(decimal valor, Moneda moneda) => FormatoDocumentoPdf.Importe(valor, moneda);

    /// <summary>
    /// Tipo de cambio con sus decimales y sin miles: 59,50 y 60,123456 (D9, <c>numeric(18,6)</c>). Al
    /// menos dos decimales, que es como se lee un cambio, y hasta seis si los tiene.
    /// </summary>
    private static string Cambio(decimal valor) =>
        valor.ToString("0.00######", CultureInfo.InvariantCulture).Replace('.', ',');

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