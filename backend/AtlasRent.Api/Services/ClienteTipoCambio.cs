using System.Globalization;
using System.Text.Json;
using AtlasRent.Api.Dominio;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cliente de ExchangeRate-API (F3-01, D22: "ExchangeRate-API con fallback manual").
/// <para>
/// <b>Entiende las dos formas de respuesta</b> porque son dos servicios distintos detrás del mismo
/// nombre: sin clave, el endpoint abierto devuelve la tabla en <c>rates</c>; con clave, el plan de
/// pago la devuelve en <c>conversion_rates</c>. Preguntar por una y no por la otra habría dejado
/// funcionando el modo que nadie.configuró.
/// </para>
/// <para>
/// <b>Todo lo que va mal sale como <see cref="FalloTipoCambioException"/></b>, con un motivo escrito
/// para que alguien lo lea en la bandeja. De ahí no sale nunca la URL completa: con clave, la URL la
/// es. Como mucho el servidor, que sí sirve para comparar contra el proveedor.
/// </para>
/// <para>
/// No reintenta. La fuente se consulta una vez al día y, si falla, la carga manual está ahí para lo
/// que haga falta (docs/ESPECIFICACION.md §3.12): reintentar dentro del trabajo solo alarga el día
/// que ya está perdido.
/// </para>
/// </summary>
public sealed class ClienteTipoCambio(HttpClient http, OpcionesTipoCambio opciones) : IFuenteTipoCambio
{
    /// <summary>Propiedad de la tabla de valores del endpoint abierto.</summary>
    private const string Rates = "rates";

    /// <summary>Propiedad de la tabla de valores del plan con clave.</summary>
    private const string ConversionRates = "conversion_rates";

    /// <summary>
    /// Corte de <see cref="HttpClient.Timeout"/> (30 s) que fija <c>Program.cs</c>. Va holgado
    /// porque un fallo aquí no rompe nada: se avisa y se carga a mano. Apretarlo solo convertiría un
    /// arranque lento en una falsa alarma.
    /// </summary>
    public static readonly TimeSpan TiempoEspera = TimeSpan.FromSeconds(30);

    public async Task<decimal> ObtenerAsync(Moneda moneda, CancellationToken ct)
    {
        if (moneda == Moneda.USD)
        {
            return 1m;
        }

        var url = opciones.UrlDeConsulta;
        var servidor = ServidorDe(url);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.GetAsync(url, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // El corte es del propio HttpClient, no de quien llamó: nadie estaba cancelando.
            throw new FalloTipoCambioException($"{servidor} no respondió en {TiempoEspera.TotalSeconds:0} s.");
        }
        catch (HttpRequestException e)
        {
            throw new FalloTipoCambioException($"No se pudo contactar con {servidor}: {e.Message}");
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new FalloTipoCambioException(
                    $"{servidor} respondió {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}).");
            }

            return await LeerAsync(await respuesta.Content.ReadAsStreamAsync(ct), moneda, servidor, ct);
        }
    }

    /// <summary>
    /// Saca el valor del JSON. Se hace a mano y no con una clase de deserialización porque las dos
    /// formas de respuesta traen la tabla con un nombre distinto y el resto de campos no se usan:
    /// deserializar el documento entero para leer un número sería aceptar medio contrato de un
    /// proveedor que puede cambiarlo sin avisar.
    /// </summary>
    private static async Task<decimal> LeerAsync(
        Stream cuerpo,
        Moneda moneda,
        string servidor,
        CancellationToken ct)
    {
        JsonDocument documento;
        try
        {
            documento = await JsonDocument.ParseAsync(cuerpo, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new FalloTipoCambioException($"{servidor} no devolvió un JSON legible.");
        }

        using (documento)
        {
            var raiz = documento.RootElement;

            // El proveedor responde 200 con un "error-type" cuando la clave está mal o le queda
            // poca cuota. Sin este corte se guardaría un tipo de cambio inventado a partir de una
            // respuesta que no es un tipo de cambio.
            if (!raiz.TryGetProperty("result", out var resultado)
                || !string.Equals(resultado.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                throw new FalloTipoCambioException($"{servidor} no confirmó el resultado correcto.");
            }

            var codigo = moneda.ToString();
            var valor = LeerValor(raiz, codigo);

            if (valor is null)
            {
                throw new FalloTipoCambioException($"{servidor} no trae el valor de {codigo}.");
            }

            if (valor <= 0m)
            {
                throw new FalloTipoCambioException($"{servidor} devolvió un valor no positivo para {codigo}.");
            }

            return valor.Value;
        }
    }

    private static decimal? LeerValor(JsonElement raiz, string codigo)
    {
        foreach (var propiedad in (string[])[Rates, ConversionRates])
        {
            if (!raiz.TryGetProperty(propiedad, out var tabla)
                || tabla.ValueKind != JsonValueKind.Object
                || !tabla.TryGetProperty(codigo, out var valor))
            {
                continue;
            }

            if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var numero))
            {
                return numero;
            }

            // La API abierta devuelve algunos números como texto. Se acepta solo si es un número
            // limpio: un "61.2 aprox" tiene que ser un fallo, no un tipo de cambio.
            if (valor.ValueKind == JsonValueKind.String
                && decimal.TryParse(
                    valor.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var texto))
            {
                return texto;
            }

            return null;
        }

        return null;
    }

    /// <summary>Solo el servidor, nunca la URL entera: con clave, la URL es la credencial.</summary>
    private static string ServidorDe(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch (UriFormatException)
        {
            Log.Warning("La URL configurada del tipo de cambio no es válida.");
            return "la fuente del tipo de cambio";
        }
    }
}
