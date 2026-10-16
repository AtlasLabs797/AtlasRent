using System.Net;
using System.Security.Cryptography;
using AtlasRent.Api.Jobs;
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
    public IpFija Resolutor { get; } = new(ContadorInstancias.IpSiguiente());

    public string IpCliente => Resolutor.Ip;

    private readonly string _claveJwt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>
    /// Carpeta de documentos de esta instancia. Es una temporal del sistema y no
    /// <c>C:\AtlasRent\data\documentos</c>: una prueba que sube un archivo escribe donde sea y lo
    /// borra al terminar, nunca en la carpeta de desarrollo.
    /// </summary>
    public string CarpetaDocumentos { get; } =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-docs-{Guid.NewGuid():N}");

    /// <summary>
    /// Clave de cifrado en reposo que usa esta instancia. La aplicación la exige al arrancar; la
    /// prueba la necesita para descifrar y comprobar que lo guardado es un cifrado válido.
    /// </summary>
    public byte[] ClaveCifrado { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>Clave del índice ciego del documento, propia y distinta de la de cifrado.</summary>
    public byte[] ClaveHmacDocumento { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Carpeta de las copias de seguridad de esta instancia. Es una temporal del sistema y no
    /// <c>C:\AtlasRent\data\backups</c>: las pruebas de rotación y de borrado escriben aquí.
    /// </summary>
    public string CarpetaCopias { get; } =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-copias-{Guid.NewGuid():N}");

    /// <summary>
    /// Clave con la que se cifran las copias de esta instancia. Distinta de la de los documentos,
    /// como en producción.
    /// </summary>
    public byte[] ClaveCifradoCopia { get; } = RandomNumberGenerator.GetBytes(32);

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
            // F1-09: claves de cifrado en reposo e índice ciego. Aleatorias y de 32 bytes, como
            // las de producción; la aplicación no arranca si no están.
            ["Seguridad:CifradoClave"] = Convert.ToBase64String(ClaveCifrado),
            ["Seguridad:CifradoVersion"] = "1",
            ["Seguridad:HmacDocumentoClave"] = Convert.ToBase64String(ClaveHmacDocumento),
            // F1-10: los documentos de propiedad se escriben en una temporal propia de la
            // instancia, para que una prueba no deje nada en la carpeta de desarrollo.
            [$"{OpcionesAlmacen.Seccion}:Documentos"] = CarpetaDocumentos,
            // F1-12: la copia de seguridad exige su carpeta y su clave al arrancar. Aquí van a una
            // temporal propia de la instancia y con una clave generada aquí: una prueba de backup
            // nunca escribe en C:\AtlasRent\data ni con la clave de nadie.
            [$"{OpcionesBackup.Seccion}:CarpetaLocal"] = CarpetaCopias,
            [$"{OpcionesBackup.Seccion}:ClaveCifrado"] = Convert.ToBase64String(ClaveCifradoCopia),
            // F1-12: sin servidor de jobs. Apagado del todo, no "inactivo": así los tests ni
            // conectan con el almacenamiento del planificador ni ejecutan la copia de seguridad
            // sobre la máquina de pruebas.
            [$"{OpcionesJobs.Seccion}:ServidorActivo"] = "false",
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

    /// <summary>Clave de cifrado de prueba: exactamente 32 bytes en base64, como AES-256 exige.</summary>
    public static string ClaveDe32Bytes() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Al terminar, la carpeta de documentos de esta instancia se borra entera. Las pruebas la
    /// usan como almacén, no como un sitio donde dejar cosas.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(CarpetaDocumentos))
        {
            Directory.Delete(CarpetaDocumentos, recursive: true);
        }

        if (Directory.Exists(CarpetaCopias))
        {
            Directory.Delete(CarpetaCopias, recursive: true);
        }
    }
}

/// <summary>Resolutor de pruebas: la IP que le pone a esta instancia.</summary>
public sealed class IpFija(string ip) : IResolutorIpPeticion
{
    /// <summary>Se puede cambiar entre peticiones para simular entrar desde otra IP.</summary>
    public string Ip { get; set; } = ip;

    public string Resolver(HttpContext http) => Ip;
}

/// <summary>
/// IPs de prueba distintas por instancia de <see cref="ApiTestFactory"/>.
/// <para>
/// Antes el contador daba la última cifra de <c>198.51.100.0/24</c>: a partir de la instancia 254
/// volvía a empezar, y como las pruebas de bloqueo dejan filas en <c>bloqueo_ip</c>, una instancia
/// que reutilizaba la IP de otra convertía en <c>403</c> los logins de la que la estaba usando. Se
/// reparten ahora por los dos últimos octetos de <c>198.51.0.0/16</c>, evitando la red 100.
/// </para>
/// </summary>
internal static class ContadorInstancias
{
    private static int _siguiente;

    private static int Next() => Interlocked.Increment(ref _siguiente);

    /// <summary>
    /// Dirección de la instancia siguiente, repartida por los dos últimos octetos de
    /// <c>198.51.0.0/16</c> (RFC 5737, rango reservado para pruebas): unas 49.000 direcciones
    /// distintas.
    /// <para>
    /// Se salta a propósito la red <c>100</c>: hay pruebas que escriben a mano
    /// <c>198.51.100.201</c> (<c>Seguridad/IpNuevaTests.cs</c>) y <c>198.51.100.250</c> (la lista
    /// blanca de <see cref="ApiTestFactory"/>), y una instancia que cayera ahí se encontraría con
    /// filas de <c>bloqueo_ip</c> que no son suyas. Sin el salto, la red 100 salía igual en la
    /// instancia número 25.386; era alcanzable, solo que improbable.
    /// </para>
    /// </summary>
    public static string IpSiguiente()
    {
        var n = Next();
        var indice = (n / 254) % 199; // 199 bloques de 254 = 50.546, menos uno por la red 100.
        var red = indice >= 100 ? indice + 2 : indice + 1;
        var host = 1 + n % 254;
        return $"198.51.{red}.{host}";
    }
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
    /// <summary>
    /// Instante de partida para las pruebas que dependen del día: las 09:00 de Santo Domingo
    /// (UTC-4, sin cambio de hora) del día de hoy.
    /// <para>
    /// Importa porque el día de negocio va por salto de día, no por reloj: una prueba que arranca
    /// "a las 20:30" y avanza seis horas cruza medianoche y su segunda ejecución cae en otro día.
    /// Con el reloj arrancando a media mañana, avanzar horas nunca cambia de día y la prueba
    /// comprueba lo que dice comprobar, no la hora a la que se ejecutó (BUG-009).
    /// </para>
    /// </summary>
    public static DateTimeOffset InicioDelDia =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddHours(13);

    private DateTimeOffset _ahora;

    public RelojPrueba(DateTimeOffset? inicio = null) => _ahora = inicio ?? DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan delta) => _ahora += delta;
}

/// <summary>
/// API sin base de datos, solo para arrancar. Comparte con <see cref="ApiTestFactory"/> la
/// clave del JWT y las de cifrado, que la aplicación exige para arrancar.
/// </summary>
public sealed class ApiTestFactorySinBd : WebApplicationFactory<Program>
{
    /// <summary>
    /// Carpeta de documentos temporal. La aplicación exige <c>Almacen__Documentos</c> para
    /// arrancar, y aquí no se escribe nada: solo tiene que existir la opción.
    /// </summary>
    private static readonly string CarpetaDocumentos =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-docs-sinbd-{Guid.NewGuid():N}");

    /// <summary>
    /// Carpeta de copias de seguridad temporal. La aplicación exige también
    /// <c>Backup__CarpetaLocal</c> y <c>Backup__ClaveCifrado</c> para arrancar; aquí no se escribe
    /// nada: solo tienen que existir las opciones.
    /// </summary>
    private static readonly string CarpetaCopias =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-copias-sinbd-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "Seguridad:JwtClave",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        builder.UseSetting("Seguridad:CifradoClave", ApiTestFactory.ClaveDe32Bytes());
        builder.UseSetting("Seguridad:CifradoVersion", "1");
        builder.UseSetting("Seguridad:HmacDocumentoClave", ApiTestFactory.ClaveDe32Bytes());
        builder.UseSetting($"{OpcionesAlmacen.Seccion}:Documentos", CarpetaDocumentos);
        builder.UseSetting($"{OpcionesBackup.Seccion}:CarpetaLocal", CarpetaCopias);
        builder.UseSetting($"{OpcionesBackup.Seccion}:ClaveCifrado", ApiTestFactory.ClaveDe32Bytes());
        // F1-12: sin servidor de jobs, igual que en ApiTestFactory. Aquí además no hay base de
        // datos a la que conectarse, así que ni la cadena de migraciones se llega a leer.
        builder.UseSetting($"{OpcionesJobs.Seccion}:ServidorActivo", "false");
    }
}