using AtlasRent.Api.Data;

namespace AtlasRent.Api.Services;

/// <summary>
/// Los seis valores con los que se sustituyen las variables de una plantilla de mensaje
/// (docs/ESPECIFICACION.md §3.14): <c>{cliente}</c>, <c>{numero}</c>, <c>{total}</c>,
/// <c>{pendiente}</c>, <c>{vencimiento}</c> y <c>{unidad}</c>.
/// <para>
/// <b>Está fuera de <see cref="ServicioEnvios"/> a propósito.</b> F4-03 compone el mismo texto en la
/// pantalla de resumen del envío masivo, y la vista previa que enseña esa pantalla es una promesa:
/// es exactamente lo que se va a mandar. Si cada sitio tuviera su propia sustitución, esa promesa
/// valdría hasta el día en que las dos copias dejaran de decir lo mismo.
/// </para>
/// <para>
/// <b>Sustitución textual y literal</b>: no se evalúa nada, no se escapa nada y una variable que el
/// documento no tiene se deja vacía. Es la misma regla que los PDF por código y no por HTML
/// (SEC-001): si el nombre del cliente trae una etiqueta, sale con la etiqueta al lado, y en texto
/// plano no hay nada que la ejecute.
/// </para>
/// </summary>
internal sealed record VariablesMensaje(
    string Cliente,
    string Numero,
    string Total,
    string Pendiente,
    string Vencimiento,
    string Unidad)
{
    /// <summary>
    /// Los valores de una factura o una proforma, los que dice <see cref="DocumentoFacturaPdfModelo"/>.
    /// Sale del modelo y no de la tabla: lo que el correo pone y lo que el papel pone tienen que
    /// salir de la misma fuente.
    /// </summary>
    public static VariablesMensaje DeFactura(DocumentoFacturaPdfModelo modelo)
    {
        var total = modelo.Totales.FirstOrDefault(t => t.Etiqueta == "Total");
        var pendiente = modelo.Totales.FirstOrDefault(t => t.Etiqueta == "Pendiente");

        return new VariablesMensaje(
            modelo.Cliente.Nombre,
            modelo.Numero,
            total?.Valor ?? string.Empty,
            pendiente?.Valor ?? string.Empty,
            modelo.FechaVencimiento,
            modelo.Cliente.Unidad ?? string.Empty);
    }

    /// <summary>
    /// Los valores de un aviso de pago (F4-05, §3.14). No es una factura: no hay número y no se
    /// habla del total, porque con pago parcial lo que se menciona es lo que falta por pagar.
    /// <para>
    /// <c>{unidad}</c> lleva delante la palabra «Unidad:» y va vacío cuando el cliente no tiene
    /// contrato con unidad, para que la línea desaparezca en vez de decir «pendiente de 1.000,00 USD
    /// en la unidad». La plantilla de recordatorio la pone en su propia línea por eso.
    /// </para>
    /// </summary>
    public static VariablesMensaje DeRecordatorio(
        string cliente,
        string pendiente,
        string vencimiento,
        string? unidad) =>
        new(
            cliente,
            string.Empty,
            string.Empty,
            pendiente,
            vencimiento,
            unidad is null ? string.Empty : $"Unidad: {unidad}");

    /// <summary>El asunto y el cuerpo con las variables ya sustituidas.</summary>
    public (string Asunto, string Cuerpo) Sustituir(PlantillaMensaje plantilla) =>
        (Sustituir(plantilla.Asunto), Sustituir(plantilla.Cuerpo));

    // Una sola pasada: un valor que contiene el nombre de otra variable ("{total}" en el nombre del
    // cliente) sale tal cual y no se vuelve a sustituir (BUG-052). Una variable desconocida se deja.
    private static readonly System.Text.RegularExpressions.Regex Marcador =
        new(@"\{(cliente|numero|total|pendiente|vencimiento|unidad)\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private string Sustituir(string texto) => Marcador.Replace(texto, coincidencia => coincidencia.Groups[1].Value switch
    {
        "cliente" => Cliente,
        "numero" => Numero,
        "total" => Total,
        "pendiente" => Pendiente,
        "vencimiento" => Vencimiento,
        _ => Unidad,
    });
}