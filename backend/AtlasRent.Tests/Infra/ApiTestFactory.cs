using System.Net;
using System.Security.Cryptography;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// PostgreSQL de un solo uso compartida por todas las clases de la colección: un contenedor por
/// ejecución en lugar de uno por clase.
/// </summary>
[CollectionDefinition(nameof(ColeccionBd))]
public sealed class ColeccionBd : ICollectionFixture<ContenedorBdFixture>
{
}

/// <summary>
/// API real arrancada contra el contenedor, con la clave del JWT y el reloj sustituidos.
/// <para>
/// La base de datos de la conexión no se lee de <c>.env.dev</c> ni de <c>appsettings.json</c>: la
/// genera el propio contenedor de pruebas.
/// </para>
/// </summary>
public class ApiTestFactory(ContenedorBdFixture bd) : WebApplicationFactory<Program>
{
    /// <summary>Reloj controlado: las pruebas de caducidad avanzan el tiempo en vez de esperar.</summary>
    public RelojPrueba Reloj { get; } = new();

    /// <summary>
    /// IP desde la que "vienen" las peticiones de esta instancia. <see cref="TestServer"/> no pone
    /// ninguna, así que sin esto todos los tests de la máquina serían la misma IP y los bloqueos
    /// de un test romperían los de otro. Cada instancia toma la suya y un test puede cambiarla con
    /// <see cref="Resolutor"/> para simular entrar desde otro sitio.
    /// </summary>
    public IpFija Resolutor { get; } = new($"198.51.100.{ContadorInstancias.Next()}");

    public string IpCliente => Resolutor.Ip;

    private readonly string _claveJwt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>Cliente con cookies (flujo real de navegador: base https para que guarde cookies Secure).</summary>
    public HttpClient CrearCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    /// <summary>Cliente sin cookies, para poder reenviar tokens concretos a mano.</summary>
    public HttpClient CrearClienteSinCookies() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    /// <summary>
    /// Ajustes de configuración propios de una instancia. Las subclases los usan para probar caminos
    /// que con los valores de producción no se pueden ver (un límite de 3 peticiones por minuto,
    /// una IP en la lista blanca).
    /// </summary>
    protected virtual void ConfigurarAjustes(IDictionary<string, string?> ajustes)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var ajustes = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = bd.CadenaApp,
            ["Seguridad:JwtClave"] = _claveJwt,
        };
        ConfigurarAjustes(ajustes);

        // UseSetting porque Program.cs lee la cadena de conexión y la clave antes de builder.Build().
        foreach (var (clave, valor) in ajustes)
        {
            builder.UseSetting(clave, valor);
        }

        builder.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(ajustes));

        builder.ConfigureTestServices(servicios =>
        {
            // ConfigureTestServices (y no ConfigureServices) porque se aplica después de los
            // registros del propio Program.cs: si no, el TimeProvider real del programa gana.
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(Reloj);

            // TestServer no pone IP de origen a la petición, así que sin esto todos los tests de la
            // máquina serían la misma IP y los bloqueos de uno romperían los de otro. Se sustituye el
            // resolutor en lugar de añadir middleware: con hosting mínimo, un IStartupFilter
            // (builder.Configure) deja el pipeline sin endpoints y todo responde 404.
            servicios.RemoveAll<IResolutorIpPeticion>();
            servicios.AddSingleton<IResolutorIpPeticion>(Resolutor);
        });
    }
}

/// <summary>Resolutor de pruebas: la IP que le pone a esta instancia.</summary>
public sealed class IpFija(string ip) : IResolutorIpPeticion
{
    /// <summary>Se puede cambiar entre peticiones para simular entrar desde otra IP.</summary>
    public string Ip { get; set; } = ip;

    public string Resolver(HttpContext http) => Ip;
}

/// <summary>IPs de prueba distintas por instancia de <see cref="ApiTestFactory"/>.</summary>
internal static class ContadorInstancias
{
    private static int _siguiente;

    public static int Next() => Interlocked.Increment(ref _siguiente) % 254 + 1;
}

/// <summary>
/// Fábrica con la IP en la lista blanca de <c>Seguridad__IpListaBlanca</c>: sus IP nunca se
/// bloquean, ni por cuenta ni por IP.
/// </summary>
public sealed class ApiTestFactoryIpPermitida(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes) =>
        ajustes["Seguridad:IpListaBlanca"] = $"{IpCliente},198.51.100.250";
}

/// <summary>Fábrica con límites estrechos, para poder provocar un 429 sin miles de peticiones.</summary>
public sealed class ApiTestFactoryLimites(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes)
    {
        ajustes[$"{OpcionesLimites.Seccion}:LoginPorMinuto"] = "3";
        ajustes[$"{OpcionesLimites.Seccion}:GlobalPorMinuto"] = "600";
    }
}

/// <summary>
/// Fábrica sin el límite estrecho de login. Las pruebas de bloqueo necesitan más de 20 intentos de
/// login seguidos (30 en el escalado de IP), y el limitador los cortaría por lo medio: esos tests
/// miden el bloqueo, no el límite.
/// </summary>
public sealed class ApiTestFactoryBloqueos(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes) =>
        ajustes[$"{OpcionesLimites.Seccion}:LoginPorMinuto"] = "500";
}

/// <summary>Reloj con el que se puede viajar en el tiempo sin esperar.</summary>
public sealed class RelojPrueba : TimeProvider
{
    private DateTimeOffset _ahora = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan delta) => _ahora += delta;
}

/// <summary>
/// API sin base de datos, solo para arrancar. Comparte con <see cref="ApiTestFactory"/> la
/// clave del JWT, que la aplicación exige para arrancar.
/// </summary>
public sealed class ApiTestFactorySinBd : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting(
            "Seguridad:JwtClave",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
}