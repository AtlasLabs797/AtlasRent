using System.Security.Cryptography;
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
public sealed class ApiTestFactory(ContenedorBdFixture bd) : WebApplicationFactory<Program>
{
    /// <summary>Reloj controlado: las pruebas de caducidad avanzan el tiempo en vez de esperar.</summary>
    public RelojPrueba Reloj { get; } = new();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting porque Program.cs lee la cadena de conexión y la clave antes de builder.Build().
        builder.UseSetting("ConnectionStrings:Default", bd.CadenaApp);
        builder.UseSetting("Seguridad:JwtClave", _claveJwt);

        builder.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = bd.CadenaApp,
                ["Seguridad:JwtClave"] = _claveJwt,
            }));

        builder.ConfigureTestServices(servicios =>
        {
            // ConfigureTestServices (y no ConfigureServices) porque se aplica después de los
            // registros del propio Program.cs: si no, el TimeProvider real del programa gana.
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(Reloj);
        });
    }
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