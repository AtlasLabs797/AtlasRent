using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.IA;

/// <summary>
/// Doble de <see cref="HttpMessageHandler"/> para el transporte de los proveedores de IA.
/// <para>
/// <b>Ninguna prueba habla con un proveedor de verdad</b> (AGENTS.md §8): se sustituye el
/// <c>HttpMessageHandler</c> del cliente de <c>IHttpClientFactory</c>, así que la petición se puede
/// inspeccionar y la respuesta es prefabricada.
/// </para>
/// <para>
/// Contesta con la forma que corresponde a cada familia de API, según la ruta que le llega: la de
/// Anthropim (<c>/v1/messages</c>) o la de <c>chat/completions</c>.
/// </para>
/// </summary>
public sealed class ManejadorFalso : HttpMessageHandler
{
    /// <summary>Cuerpo de la petición, tal cual se envió al proveedor.</summary>
    public List<string> Cuerpos { get; } = [];

    /// <summary>URL completa de la petición.</summary>
    public List<string> Urls { get; } = [];

    /// <summary>Cabeceras de la petición, con su nombre en minúsculas.</summary>
    public List<Dictionary<string, string>> Cabeceras { get; } = [];

    /// <summary>
    /// Texto que devuelve el proveedor si no se indica otro. Por defecto repite el primer seudónimo
    /// que aparece en la petición, que es justo lo que hace un modelo cuando le dicen que responda
    /// con los datos: así se comprueba que el reificado devuelve al usuario el nombre de verdad.
    /// </summary>
    public string? RespuestaFija { get; set; }

    /// <summary>Código de estado con el que contesta, para probar el 503 de proveedor caído.</summary>
    public HttpStatusCode Estado { get; set; } = HttpStatusCode.OK;

    /// <summary>Si no es nulo, el transporte lanza esta excepción (para probar el 500).</summary>
    public Exception? Fallo { get; set; }

    public void Limpiar()
    {
        Fallo = null;
        Cuerpos.Clear();
        Urls.Clear();
        Cabeceras.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion,
        CancellationToken ct)
    {
        if (Fallo is not null)
        {
            throw Fallo;
        }

        lock (Urls)
        {
            Urls.Add(peticion.RequestUri!.AbsoluteUri);
        }
        Cuerpos.Add(await peticion.Content!.ReadAsStringAsync(ct));
        Cabeceras.Add(peticion.Headers
            .Concat(peticion.Content!.Headers)
            .ToDictionary(c => c.Key.ToLowerInvariant(), c => string.Join(",", c.Value), StringComparer.Ordinal));

        var texto = TextoDeRespuesta(Cuerpos[^1]);

        Dictionary<string, object?> cuerpo = PrimeraFamilia(peticion.RequestUri.AbsolutePath)
            ? new()
            {
                ["id"] = "msg_prueba",
                ["content"] = new[] { new Dictionary<string, string> { ["type"] = "text", ["text"] = texto } },
                ["usage"] = new Dictionary<string, int> { ["input_tokens"] = 120, ["output_tokens"] = 45 },
            }
            : new()
            {
                ["id"] = "chatcmpl-prueba",
                ["choices"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["message"] = new Dictionary<string, string> { ["role"] = "assistant", ["content"] = texto },
                    },
                },
                ["usage"] = new Dictionary<string, int> { ["prompt_tokens"] = 120, ["completion_tokens"] = 45 },
            };

        return new HttpResponseMessage(Estado)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json"),
        };
    }

    private static bool PrimeraFamilia(string ruta) => ruta.EndsWith("/v1/messages", StringComparison.Ordinal);

    private string TextoDeRespuesta(string cuerpo) =>
        RespuestaFija ?? $"El cliente {AyudaIa.PrimerSeudonimo(cuerpo)} tiene saldo pendiente.";
}

/// <summary>
/// Fábrica de pruebas de IA. Configura los cuatro proveedores con claves de mentira y sustituye el
/// transporte de los dos clientes de <c>IHttpClientFactory</c> por el doble, de modo que ninguna
/// prueba salga a internet.
/// </summary>
public abstract class ApiTestFactoryIaBase(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    /// <summary>Clave de mentira. No es una credencial ni se parece a una: nunca sale de la máquina.</summary>
    public const string ClaveDePrueba = "clave-de-prueba-que-no-existe";

    public const string ModeloDePrueba = "modelo-de-prueba";

    public const string RefererDePrueba = "https://atlas-rent.example.test";

    public const string TituloDePrueba = "Atlas Rent";

    /// <summary>Proveedor que se configura como el de la instalación.</summary>
    protected abstract ProveedorIa ProveedorElegido { get; }

    protected virtual bool Activado { get; } = true;

    protected virtual bool ConClave { get; } = true;

    /// <summary>Tope global del mes. Alto en las pruebas que no miden el límite.</summary>
    protected virtual int TopeConsultasMes { get; } = 100000;

    public ManejadorFalso Proveedor { get; } = new();

    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes)
    {
        ajustes[$"{OpcionesIa.Seccion}:Activado"] = Activado ? "true" : "false";
        ajustes[$"{OpcionesIa.Seccion}:Proveedor"] = ProveedorElegido.ToString().ToLowerInvariant();
        ajustes[$"{OpcionesIa.Seccion}:TopeConsultasMes"] = TopeConsultasMes.ToString();
        ajustes[$"{OpcionesIa.Seccion}:MaxTokensSalida"] = "1024";

        foreach (var seccion in new[] { "Anthropic", "Openai", "Openrouter", "Minimax" })
        {
            ajustes[$"{OpcionesIa.Seccion}:{seccion}:Modelo"] = ModeloDePrueba;
            ajustes[$"{OpcionesIa.Seccion}:{seccion}:Clave"] = ConClave ? ClaveDePrueba : null;
        }

        ajustes[$"{OpcionesIa.Seccion}:Openrouter:Referer"] = RefererDePrueba;
        ajustes[$"{OpcionesIa.Seccion}:Openrouter:Titulo"] = TituloDePrueba;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Se añade después de ConfigureWebHost de la base (que ya quita el TimeProvider real y pone
        // el de la prueba) y vuelve a aplicar ConfigureTestServices, que se ejecuta al final.
        builder.ConfigureTestServices(servicios =>
        {
            servicios.AddHttpClient(ProveedorAnthropic.NombreCliente)
                .ConfigurePrimaryHttpMessageHandler(() => Proveedor);
            servicios.AddHttpClient(ProveedorChatCompletions.NombreCliente)
                .ConfigurePrimaryHttpMessageHandler(() => Proveedor);
        });
    }
}

public sealed class ApiTestFactoryIaAnthropic(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Anthropic;
}

public sealed class ApiTestFactoryIaOpenai(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Openai;
}

public sealed class ApiTestFactoryIaOpenrouter(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Openrouter;
}

public sealed class ApiTestFactoryIaMinimax(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Minimax;
}

/// <summary>IA apagada por configuración: 503, no 500.</summary>
public sealed class ApiTestFactoryIaDesactivada(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Anthropic;

    protected override bool Activado => false;
}

/// <summary>Sin clave configurada para el proveedor elegido: 503, no 500.</summary>
public sealed class ApiTestFactoryIaSinClave(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    protected override ProveedorIa ProveedorElegido => ProveedorIa.Anthropic;

    protected override bool ConClave => false;
}

/// <summary>
/// Tope global del mes igual a lo que ya se ha consultado este mes, así que la siguiente consulta
/// está pasándolo por definición.
/// <para>
/// Hace falta porque el tope es <b>global de la instalación</b> y la base de la colección la
/// comparten todas las pruebas: un tope fijo de «1» daría 429 o 200 según el orden en que unlucky
/// que le toque, y una prueba cuyo resultado depende del orden no prueba nada. Leyendo el contador
/// en el arranque, el 429 es el único desenlace posible.
/// </para>
/// </summary>
public sealed class ApiTestFactoryIaTopeMes(ContenedorBdFixture bd) : ApiTestFactoryIaBase(bd)
{
    private readonly ContenedorBdFixture _bd = bd;

    protected override ProveedorIa ProveedorElegido => ProveedorIa.Anthropic;

    protected override int TopeConsultasMes => ConsultadasEsteMes();

    private int ConsultadasEsteMes()
    {
        using var conexion = new NpgsqlConnection(_bd.CadenaSuperusuario);
        conexion.Open();

        // El mismo corte que usa el servicio: primer día del mes en la zona de negocio.
        using var comando = new NpgsqlCommand(
            """
            SELECT count(*) FROM auditoria
            WHERE accion = 'ia.consulta' AND resultado = 'reservado'
              AND created_at >= (date_trunc('month', now() AT TIME ZONE 'America/Santo_Domingo')
                                 AT TIME ZONE 'America/Santo_Domingo')
            """,
            conexion);
        return Convert.ToInt32(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Ayudas compartidas por las pruebas del bloque.</summary>
internal static class AyudaIa
{
    public const string RutaSesion = "/api/v1/ia/consulta";

    public const string RutaAgente = "/api/v1/agente/consulta";

    /// <summary>Primer seudónimo <c>[[prefijo_n]]</c> de un texto.</summary>
    public static string PrimerSeudonimo(string texto)
    {
        var inicio = texto.IndexOf("[[", StringComparison.Ordinal);
        if (inicio < 0)
        {
            return string.Empty;
        }

        var fin = texto.IndexOf("]]", inicio, StringComparison.Ordinal);
        return fin < 0 ? string.Empty : texto[inicio..(fin + 2)];
    }

    /// <summary>JSON del cuerpo que se mandó al proveedor, para poder recorrer sus campos.</summary>
    public static JsonElement Cuerpo(JsonElement peticionProveedor)
    {
        var contenido = peticionProveedor.GetProperty("messages")[0].GetProperty("content").GetString()!;
        var datos = contenido[(contenido.IndexOf("DATOS:", StringComparison.Ordinal) + 6)..];
        return JsonDocument.Parse(datos).RootElement.Clone();
    }

    /// <summary>Instrucciones de sistema que se mandaron.</summary>
    public static string Sistema(JsonElement peticionProveedor) =>
        peticionProveedor.TryGetProperty("system", out var sistema)
            ? sistema.GetString()!
            : peticionProveedor.GetProperty("messages")[0].GetProperty("content").GetString()!;

    /// <summary>Nombres de todos los clientes activos, por la API, sin pasar por el asistente.</summary>
    public static async Task<List<string>> NombresDeClientesAsync(SesionCliente sesion, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync("/api/v1/clientes?page=1&pageSize=200", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagina = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        return [.. pagina.GetProperty("data").EnumerateArray()
            .Select(fila => fila.GetProperty("nombre").GetString()!)
            .Where(nombre => !string.IsNullOrWhiteSpace(nombre))];
    }

    /// <summary>
    /// Token escrito directamente en la base de datos, como en <c>Agentes/TokenAgenteTests</c>: así
    /// se puede dar un límite de consultas concreto o quitar una acción de la lista, cosas que el
    /// alta normal no deja hacer.
    /// </summary>
    public static async Task<(string Secreto, Guid Id)> TokenEnBaseAsync(
        ContenedorBdFixture bd,
        Guid usuarioId,
        string[] acciones,
        CancellationToken ct,
        int limiteConsultasDia = 100)
    {
        var secreto = EmisorTokens.TokenAleatorio();
        var id = Guid.NewGuid();

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.AgenteToken.Add(new AgenteToken
        {
            Id = id,
            UsuarioId = usuarioId,
            Nombre = "Token de prueba de IA",
            HashToken = EmisorTokens.HashToken(secreto),
            Acciones = acciones,
            Creado = DateTimeOffset.UtcNow,
            LimiteConsultasDia = limiteConsultasDia,
        });
        await contexto.SaveChangesAsync(ct);

        return (secreto, id);
    }
}