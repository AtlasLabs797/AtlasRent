using System.Net.Http.Json;
using System.Text.Json;

namespace AtlasRent.Api.Services;

/// <summary>
/// La API de mensajes de Anthropic: <c>POST {BaseUrl}/v1/messages</c> con <c>x-api-key</c> y
/// <c>anthropic-version</c>. La respuesta se lee de <c>content[].text</c> y los tokens de
/// <c>usage</c>.
/// <para>
/// La clave va en la cabecera de la petición y <b>no sale nunca a la respuesta ni a la auditoría</b>;
/// el modelo que se devuelve al usuario es el configurado, no uno que venga en el cuerpo.
/// </para>
/// </summary>
public sealed class ProveedorAnthropic(IHttpClientFactory http, OpcionesIa opciones) : IProveedorIA
{
    /// <summary>Nombre del cliente de <c>IHttpClientFactory</c>, para poder sustituir el transporte en pruebas.</summary>
    public const string NombreCliente = "ia-anthropic";

    /// <summary>Versión de la API que se pide. Anthropic la exige; sin ella devuelve 400.</summary>
    public const string VersionApi = "2023-06-01";

    private const string RutaMensajes = "/v1/messages";

    public bool Atiende(ProveedorIa proveedor) => proveedor == ProveedorIa.Anthropic;

    public async Task<RespuestaProveedor> ConsultarAsync(PeticionProveedor peticion, CancellationToken ct)
    {
        var configuracion = opciones.Anthropic;
        var cliente = http.CreateClient(NombreCliente);

        using var peticionHttp = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuracion.BaseUrl.TrimEnd('/')}{RutaMensajes}");

        peticionHttp.Headers.TryAddWithoutValidation("x-api-key", configuracion.Clave);
        peticionHttp.Headers.TryAddWithoutValidation("anthropic-version", VersionApi);

        // "system" va fuera de la lista de mensajes en esta API, y "max_tokens" es obligatorio:
        // sin él Anthropic responde 400.
        var cuerpo = new Dictionary<string, object?>
        {
            ["model"] = configuracion.Modelo,
            ["max_tokens"] = opciones.MaxTokensSalida,
            ["system"] = peticion.Sistema,
            ["messages"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["role"] = "user",
                    ["content"] = $"{peticion.Pregunta}\n\nDATOS:\n{peticion.Datos}",
                },
            },
        };

        peticionHttp.Content = JsonContent.Create(cuerpo);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await cliente.SendAsync(peticionHttp, ct);
        }
        catch (HttpRequestException fallo)
        {
            throw new ProveedorIaException("El proveedor de IA no respondió.", fallo);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProveedorIaException("El proveedor de IA tardó demasiado en responder.");
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new ProveedorIaException(
                    $"El proveedor de IA rechazó la consulta (HTTP {(int)respuesta.StatusCode}).");
            }

            await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
            using var documento = await JsonDocument.ParseAsync(flujo, cancellationToken: ct);

            return LeerRespuesta(documento.RootElement);
        }
    }

    /// <summary>
    /// Se concatenan los bloques de texto de <c>content</c>: una respuesta puede venir partida y
    /// quedarse solo con el primero dejaría la respuesta a medias.
    /// </summary>
    private static RespuestaProveedor LeerRespuesta(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("content", out var contenido) || contenido.ValueKind != JsonValueKind.Array)
        {
            throw new ProveedorIaException("El proveedor de IA devolvió una respuesta sin contenido.");
        }

        var texto = string.Concat(contenido.EnumerateArray()
            .Where(bloque => bloque.TryGetProperty("type", out var tipo)
                && tipo.ValueKind == JsonValueKind.String
                && tipo.GetString() == "text")
            .Select(bloque => bloque.GetProperty("text").GetString()));

        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ProveedorIaException("El proveedor de IA devolvió una respuesta vacía.");
        }

        return new RespuestaProveedor(
            texto,
            LeerUso(raiz, "input_tokens"),
            LeerUso(raiz, "output_tokens"));
    }

    /// <summary>
    /// Los tokens son para medir, no para decidir: si el proveedor no los manda, se cuenta cero y
    /// el gasto de esa llamada queda sin registrar en vez de tumbar la respuesta.
    /// </summary>
    private static int LeerUso(JsonElement raiz, string propiedad) =>
        raiz.TryGetProperty("usage", out var uso)
        && uso.TryGetProperty(propiedad, out var valor)
        && valor.TryGetInt32(out var numero)
            ? numero
            : 0;
}