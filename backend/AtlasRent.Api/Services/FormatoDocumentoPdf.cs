using System.Globalization;
using System.Text;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Services;

/// <summary>
/// Formato de los datos de un documento PDF (F4-01, <c>docs/DESIGN.md §5</c>).
/// <para>
/// Vive fuera del compositor a propósito: el PDF de recibo y de estado de cuenta de F4-01, y
/// cualquier otro que venga, imprimen importes y fechas y tienen que imprimirlos igual. Si el
/// formato se duplica por compositor, el día que cambie uno solo cambia en uno.
/// </para>
/// <para>
/// <b>Los dos PDF que ya existían (cotización y contrato) no se tocan</b> y siguen con su copia
/// privada de estas tres funciones:<F4-01> no era la tarea de moverlas y un cambio ahí es un
/// cambio en documentos que ya tienen pruebas byte a byte. Cuando esos archivos se toquen por
/// otra razón, esta es la copia que sobra.
/// </para>
/// </summary>
internal static class FormatoDocumentoPdf
{
    /// <summary>Importe con el formato de <c>docs/DESIGN.md §5</c>: <c>1.250,00 USD</c>.</summary>
    public static string Importe(decimal valor, Moneda moneda) => $"{Numero(valor)} {moneda}";

    /// <summary>
    /// Número con punto de miles y coma decimal (<c>docs/DESIGN.md §5</c>). Se hace a mano y no con
    /// la cultura del servidor: un documento con <c>1.250,00</c> en Windows y <c>1,250.00</c> en otro
    /// equipo no es el mismo documento.
    /// </summary>
    public static string Numero(decimal valor)
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

    /// <summary>
    /// Cantidad de una línea con sus decimales pero sin miles: una cantidad no es un importe y
    /// <c>1.234,00</c> unidades no dice nada que <c>1.234</c> no diga.
    /// </summary>
    public static string Cantidad(decimal valor) =>
        valor.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Fecha de negocio en <c>dd/mm/aaaa</c> (<c>docs/DESIGN.md §5</c>).</summary>
    public static string Fecha(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Momento en <c>dd/mm/aaaa</c> (<c>docs/DESIGN.md §5</c>).</summary>
    public static string Fecha(DateTimeOffset momento) =>
        momento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Etiqueta: valor", o nada si el valor no está.</summary>
    public static string? Dato(string? valor, string etiqueta) =>
        string.IsNullOrWhiteSpace(valor) ? null : $"{etiqueta}: {valor.Trim()}";
}
