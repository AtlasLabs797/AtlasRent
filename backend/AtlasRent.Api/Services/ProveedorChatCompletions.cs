using System.Net.Http.Json;
using System.Text.Json;

namespace AtlasRent.Api.Services;

/// <summary>
/// El dialecto <c>POST {BaseUrl}/chat/completions</c>, que es el que ya comparten <b>OpenAI</b>
/// (<c>https://api.openai.io/v1</c>), <b>OpenRouter</b> (<c>https://openrouter.ai/api/v1</c>) y
/// <b>MiniMax</b> (<c>https://api.minimax.io/v1</c>).
/// <para>
/// Una sola clase para los tres: la petición, la respuesta y el formato del cuerpo son idénticos y
/// lo único que cambia es la URL base, la clave y el nombre del modelo, que salen de
/// <see cref="OpcionesProveedorIa"/>. Los tres autentican con <c>Authorization: Bearer</c>;
/// OpenRouter solo añade dos cabeceras opcionales de atribución.
/// </para>
/// <para>
/// El límite de salida va en <c>max_completion_tokens</c> y no en el <c>max_tokens</c> antiguo: es el
/// campo que aceptan los modelos actuales de OpenAI y de MiniMax, y el que OpenRouter traduce a
/// cada modelo que enruta. El campo viejo sigue siendo el único válido en los modelos
/// <b>anteriores</b> a <c>gpt-4o</c>, así que si algún día hay que hablar con uno, el lugar de
/// tocar es esta clase y su nombre, no los tres proveedores.
/// </para>
/// </summary>
public sealed class ProveedorChatCompletions(IHttpClientFactory http, OpcionesIa opciones) : IProveedorIA
{
    /// <summary>Nombre del cliente de <c>IHttpClientFactory</c>, para poder sustituir el transporte en pruebas.</summary>
    public const string NombreCliente = "ia-chat-completions";

    private const string RutaChat = "/chat/completions";

    public bool Atiende(ProveedorIa proveedor) => proveedor is ProveedorIa.Openai or ProveedorIa.Openrouter or ProveedorIa.Minimax;

    public async Task<RespuestaProveedor> ConsultarAsync(PeticionProveedor peticion, CancellationToken ct)
    {
        // La configuración dice cuál de los tres es: los tres comparten este código y lo único que
        // cambia son la URL base, la clave y el modelo.
        var configuracion = opciones.Del(opciones.Proveedor);
        var cliente = http.CreateClient(NombreCliente);

        using var peticionHttp = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuracion.BaseUrl.TrimEnd('/')}{RutaChat}");

        peticionHttp.Headers.TryAddWithoutValidation("Authorization", $"Bearer {configuracion.Clave}");

        foreach (var (cabecera, valor) in configuracion.Cabeceras)
        {
            peticionHttp.Headers.TryAddWithoutValidation(cabecera, valor);
        }

        var cuerpo = new Dictionary<string, object?>
        {
            ["model"] = configuracion.Modelo,
            ["max_completion_tokens"] = opciones.MaxTokensSalida,
            ["messages"] = new[]
            {
                new Dictionary<string, string> { ["role"] = "system", ["content"] = peticion.Sistema },
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

    private static RespuestaProveedor LeerRespuesta(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("choices", out var opciones)
            || opciones.ValueKind != JsonValueKind.Array
            || opciones.GetArrayLength() == 0)
        {
            throw new ProveedorIaException("El proveedor de IA devolvió una respuesta sin contenido.");
        }

        var primero = opciones[0];
        if (!primero.TryGetProperty("message", out var mensaje)
            || !mensaje.TryGetProperty("content", out var contenido)
            || contenido.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(contenido.GetString()))
        {
            throw new ProveedorIaException("El proveedor de IA devolvió una respuesta vacía.");
        }

        return new RespuestaProveedor(
            contenido.GetString()!,
            LeerUso(raiz, "prompt_tokens"),
            LeerUso(raiz, "completion_tokens"));
    }

    private static int LeerUso(JsonElement raiz, string propiedad) =>
        raiz.TryGetProperty("usage", out var uso)
        && uso.TryGetProperty(propiedad, out var valor)
        && valor.TryGetInt32(out var numero)
            ? numero
            : 0;
}