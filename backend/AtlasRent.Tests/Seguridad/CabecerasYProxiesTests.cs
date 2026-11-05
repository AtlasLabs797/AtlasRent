using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// SEC-014: X-Forwarded-For solo de los proxies de confianza, cabeceras de seguridad en todas las
/// respuestas, HSTS y redirección a HTTPS fuera de Development y un manejador global de errores que
/// no devuelve trazas.
/// </summary>
public static class CabecerasEsperadas
{
    public static void Comprobar(HttpResponseMessage respuesta)
    {
        string Cabecera(string nombre) =>
            respuesta.Headers.TryGetValues(nombre, out var valores) ? string.Join(",", valores) : string.Empty;

        Cabecera("X-Content-Type-Options").Should().Be("nosniff");
        Cabecera("Referrer-Policy").Should().Be("no-referrer");
        Cabecera("X-Frame-Options").Should().Be("DENY");
        Cabecera("Permissions-Policy").Should().NotBeEmpty();

        var csp = Cabecera("Content-Security-Policy");
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("object-src 'none'");
        csp.Should().Contain("base-uri 'self'");
        csp.Should().Contain("script-src 'self'");
        csp.Should().NotContain("script-src 'self' 'unsafe-inline'", "los scripts no pueden ir en línea");
        csp.Should().NotContain("'unsafe-eval'");
    }
}

/// <summary>Pone la IP de la conexión: <c>TestServer</c> no trae ninguna y un proxy se reconoce por ella.</summary>
public sealed class FiltroIpConexion(IPAddress ip) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) =>
        app =>
        {
            app.Use((http, next) =>
            {
                http.Connection.RemoteIpAddress = ip;
                return next(http);
            });
            siguiente(app);
        };
}

/// <summary>Fábrica con el resolutor de IP real y la conexión llegando desde <see cref="IpConexion"/>.</summary>
public abstract class ApiTestFactoryIpReal(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    public IPAddress IpConexion { get; } = IPAddress.Parse(ContadorInstancias.IpSiguiente());

    protected override bool UsaResolutorFijo => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(servicios => servicios.AddSingleton<IStartupFilter>(new FiltroIpConexion(IpConexion)));
    }
}

/// <summary>La conexión viene del proxy de confianza.</summary>
public sealed class ApiTestFactoryTrasProxy(ContenedorBdFixture bd) : ApiTestFactoryIpReal(bd)
{
    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes) =>
        ajustes["Seguridad:ProxiesConfianza"] = IpConexion.ToString();
}

/// <summary>Sin <c>Seguridad__ProxiesConfianza</c>: no se cree ninguna cabecera X-Forwarded-*.</summary>
public sealed class ApiTestFactorySinProxy(ContenedorBdFixture bd) : ApiTestFactoryIpReal(bd);

[Collection(nameof(ColeccionBd))]
public sealed class ForwardedHeadersTests(ContenedorBdFixture bd, ApiTestFactoryTrasProxy tras, ApiTestFactorySinProxy sin)
    : IClassFixture<ApiTestFactoryTrasProxy>, IClassFixture<ApiTestFactorySinProxy>
{
    private static string IpCliente() => ContadorInstancias.IpSiguiente();

    private static async Task FallarLoginAsync(HttpClient cliente, string xff, int intentos, CancellationToken ct)
    {
        for (var i = 0; i < intentos; i++)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { nombreAcceso = $"nadie-{Guid.NewGuid():N}", contrasena = "mala" }),
            };
            peticion.Headers.Add("X-Forwarded-For", xff);
            var respuesta = await cliente.SendAsync(peticion, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private static Task<HttpResponseMessage> PedirAsync(HttpClient cliente, string xff, CancellationToken ct)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        peticion.Headers.Add("X-Forwarded-For", xff);
        return cliente.SendAsync(peticion, ct);
    }

    private async Task<List<string>> IpsBloqueadasAsync(IEnumerable<string> candidatas, CancellationToken ct)
    {
        var lista = candidatas.ToList();
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.BloqueoIp.AsNoTracking().Where(b => lista.Contains(b.Ip)).Select(b => b.Ip).ToListAsync(ct);
    }

    [Fact]
    public async Task TrasUnProxyDeConfianzaCadaClienteSeBloqueaPorSuCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = tras.CrearCliente();
        var atacante = IpCliente();
        var otro = IpCliente();

        await FallarLoginAsync(cliente, atacante, 10, ct);

        (await PedirAsync(cliente, atacante, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PedirAsync(cliente, otro, ct)).StatusCode.Should().Be(HttpStatusCode.OK,
            "dos clientes tras el mismo proxy no se bloquean entre sí");

        var bloqueadas = await IpsBloqueadasAsync([atacante, otro, tras.IpConexion.ToString()], ct);
        bloqueadas.Should().BeEquivalentTo([atacante], "se bloquea la IP del cliente, no la del proxy");
    }

    [Fact]
    public async Task UnaCadenaForjadaPorElClienteNoCambiaLaIpQueAnotaElProxy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = tras.CrearCliente();
        var real = IpCliente();
        var forjada = IpCliente();

        // Caddy añade al final la IP que ve; lo que el cliente escribió antes no cuenta.
        await FallarLoginAsync(cliente, $"{forjada}, {real}", 10, ct);

        (await IpsBloqueadasAsync([real, forjada], ct)).Should().BeEquivalentTo([real]);
    }

    [Fact]
    public async Task SinProxiesConfiguradosXForwardedForSeIgnora()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = sin.CrearCliente();
        var inventadas = Enumerable.Range(0, 10).Select(_ => IpCliente()).ToList();

        // Diez fallos, cada uno con una IP inventada distinta: si se creyera la cabecera nadie se bloquearía.
        foreach (var ip in inventadas)
        {
            await FallarLoginAsync(cliente, ip, 1, ct);
        }

        (await PedirAsync(cliente, IpCliente(), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "los fallos se cuentan contra la IP de la conexión");
        (await IpsBloqueadasAsync([.. inventadas, sin.IpConexion.ToString()], ct))
            .Should().BeEquivalentTo([sin.IpConexion.ToString()]);
    }
}

[Collection(nameof(ColeccionBd))]
public sealed class CabecerasSeguridadTests(ContenedorBdFixture bd, ApiTestFactory api) : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task LasRespuestasDeLaApiLlevanLasCabecerasDeSeguridad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.CrearCliente();

        foreach (var ruta in new[] { "/health/live", "/api/v1/auth/yo", "/no-existe" })
        {
            var respuesta = await cliente.GetAsync(ruta, ct);
            CabecerasEsperadas.Comprobar(respuesta);
        }
    }

    [Fact]
    public async Task IndexHtmlTambienLlevaLasCabecerasDeSeguridad()
    {
        var ct = TestContext.Current.CancellationToken;
        var carpeta = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var existia = Directory.Exists(carpeta);
        Directory.CreateDirectory(carpeta);
        var indice = Path.Combine(carpeta, "index.html");
        var habiaIndice = File.Exists(indice);
        if (!habiaIndice)
        {
            await File.WriteAllTextAsync(indice, "<!doctype html><title>t</title>", ct);
        }

        try
        {
            // El bloque de archivos estáticos se decide al construir el host, con el índice ya escrito.
            await using var conIndice = new ApiTestFactoryConWebRoot(bd, carpeta);
            using var cliente = conIndice.CrearCliente();
            var respuesta = await cliente.GetAsync("/", ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain("<title>");
            CabecerasEsperadas.Comprobar(respuesta);
        }
        finally
        {
            if (!habiaIndice)
            {
                File.Delete(indice);
            }

            if (!existia)
            {
                Directory.Delete(carpeta);
            }
        }
    }
}

/// <summary>
/// Sirve el frontend desde otra carpeta: en las pruebas la raíz de contenido es la del proyecto Api y no
/// la de salida, que es donde Program.cs busca wwwroot.
/// </summary>
public sealed class ApiTestFactoryConWebRoot(ContenedorBdFixture bd, string webRoot) : ApiTestFactory(bd)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseWebRoot(webRoot);
    }
}

/// <summary>Entorno elegido y un resolutor que falla solo si la petición lo pide.</summary>
public abstract class ApiTestFactoryEntorno(ContenedorBdFixture bd, string entorno) : ApiTestFactory(bd)
{
    public const string CabeceraProvocarError = "X-Provocar-Error";

    protected override bool UsaResolutorFijo => false;

    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes) =>
        ajustes["HTTPS_PORT"] = "443";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(entorno);
        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IResolutorIpPeticion>();
            servicios.AddSingleton<IResolutorIpPeticion>(new ResolutorQueFalla(Resolutor));
        });
    }

    private sealed class ResolutorQueFalla(IResolutorIpPeticion normal) : IResolutorIpPeticion
    {
        public string Resolver(HttpContext http) =>
            http.Request.Headers.ContainsKey(CabeceraProvocarError)
                ? throw new InvalidOperationException("detalle-interno-secreto Host=bd;Password=xxx")
                : normal.Resolver(http);
    }
}

public sealed class ApiTestFactoryProduccion(ContenedorBdFixture bd) : ApiTestFactoryEntorno(bd, "Production");

public sealed class ApiTestFactoryDesarrollo(ContenedorBdFixture bd) : ApiTestFactoryEntorno(bd, "Development");

[Collection(nameof(ColeccionBd))]
public sealed class ErroresYHttpsTests(ApiTestFactoryProduccion produccion, ApiTestFactoryDesarrollo desarrollo)
    : IClassFixture<ApiTestFactoryProduccion>, IClassFixture<ApiTestFactoryDesarrollo>
{
    private static async Task ComprobarProblema500Async(ApiTestFactory fabrica, CancellationToken ct)
    {
        using var cliente = fabrica.CrearCliente();
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        peticion.Headers.Add(ApiTestFactoryEntorno.CabeceraProvocarError, "1");

        var respuesta = await cliente.SendAsync(peticion, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        cuerpo.Should().NotContain("detalle-interno-secreto").And.NotContain("Password")
            .And.NotContain("InvalidOperationException").And.NotContain("   at ");
        using var json = JsonDocument.Parse(cuerpo);
        json.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        json.RootElement.GetProperty("codigoReferencia").GetString().Should().Be(Problemas.ErrorInterno);
        CabecerasEsperadas.Comprobar(respuesta);
    }

    [Fact]
    public Task UnaExcepcionNoControladaDevuelve500SinTrazaEnProduccion() =>
        ComprobarProblema500Async(produccion, TestContext.Current.CancellationToken);

    [Fact]
    public Task UnaExcepcionNoControladaDevuelve500SinTrazaTambienEnDesarrollo() =>
        ComprobarProblema500Async(desarrollo, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EnProduccionHayHstsYSeRedirigeAHttps()
    {
        var ct = TestContext.Current.CancellationToken;

        using var seguro = produccion.CrearCliente();
        seguro.DefaultRequestHeaders.Host = "atlas.example.test";
        var https = await seguro.GetAsync("/health/live", ct);
        https.Headers.TryGetValues("Strict-Transport-Security", out var hsts).Should().BeTrue();
        string.Join(",", hsts!).Should().Contain("max-age=");

        using var plano = produccion.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://atlas.example.test"),
            AllowAutoRedirect = false,
        });
        var redireccion = await plano.GetAsync("/health/live", ct);
        ((int)redireccion.StatusCode).Should().BeOneOf(301, 302, 307, 308);
        redireccion.Headers.Location!.ToString().Should().StartWith("https://atlas.example.test");
    }

    [Fact]
    public async Task EnDesarrolloNoSeRedirigeNiHayHsts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var plano = desarrollo.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://atlas.example.test"),
            AllowAutoRedirect = false,
        });
        var respuesta = await plano.GetAsync("/health/live", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        respuesta.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }
}
