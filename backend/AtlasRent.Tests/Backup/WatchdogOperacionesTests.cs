using System.IO.Compression;
using System.Text;
using AtlasRent.Api.Services;
using AtlasRent.Watchdog;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasRent.Tests.Backup;

/// <summary>
/// F1-12: el Watchdog deja de ser un placeholder y gana la regla que lo hace un watchdog:
/// <strong>si no hay una copia anterior válida, no toca nada</strong>. Un watchdog que actualiza sin
/// red de seguridad convierte un despliegue malo en una pérdida de datos.
/// </summary>
public sealed class WatchdogOperacionesTests : IDisposable
{
    private const string DumpConDatos = """
        COPY public.cliente (id, nombre) FROM stdin;
        8f0e0000-0000-0000-0000-000000000001	Puerta del Este
        \.
        """;

    private readonly string _raiz =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-watchdog-{Guid.NewGuid():N}");

    private readonly string _copias;

    private readonly string _documentos;

    /// <summary>
    /// Una sola clave para todo el caso: el Watchdog y el servicio de copia tienen que usar la misma,
    /// que es lo que pasa en el servidor. Con dos claves distintas, la copia no abriría y la prueba
    /// probaría otra cosa.
    /// </summary>
    private readonly OpcionesBackup _opciones;

    public WatchdogOperacionesTests()
    {
        _copias = Path.Combine(_raiz, "copias");
        _documentos = Path.Combine(_raiz, "documentos");
        Directory.CreateDirectory(_copias);
        Directory.CreateDirectory(_documentos);

        _opciones = new OpcionesBackup
        {
            CarpetaLocal = _copias,
            ClaveCifrado = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class VolcadoFalso : IVolcadoBaseDatos
    {
        public Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct) =>
            Task.FromResult(Encoding.UTF8.GetBytes(DumpConDatos));
    }

    private ServicioBackup Copias() => new(
        _opciones,
        new VolcadoFalso(),
        new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = _documentos }),
        new ServicioCifradoCopia(_opciones),
        TimeProvider.System);

    private OperacionesWatchdog Watchdog() => new(
        Copias(),
        NullLogger<OperacionesWatchdog>.Instance);

    /// <summary>Sin copia de seguridad que verifique, la actualización se para y no toca la instalación.</summary>
    [Fact]
    public async Task ActualizarSinCopiaPreviaValidaNoTocaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var instalacion = Path.Combine(_raiz, "instalacion");
        var paquete = EscribirPaquete("paquete.zip", "version.txt", "contenido nuevo");

        var accion = () => Watchdog().ActualizarAsync(paquete, instalacion, ct);

        await accion.Should().ThrowAsync<WatchdogSinCopiaPreviaException>();
        Directory.Exists(instalacion + ".prev").Should().BeFalse("no se llegó a hacer la copia previa");
    }

    /// <summary>
    /// Con una copia válida, la actualización copia los archivos de la instalación antes de
    /// sobrescribirlos. Sin ese ".prev" no hay vuelta atrás.
    /// </summary>
    [Fact]
    public async Task ActualizarConCopiaPreviaValidaCopiaLaInstalacionAntesDeTocarla()
    {
        var ct = TestContext.Current.CancellationToken;
        await Copias().EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        var instalacion = Path.Combine(_raiz, "instalacion");
        Directory.CreateDirectory(instalacion);
        await File.WriteAllTextAsync(Path.Combine(instalacion, "AtlasRent.Api.exe"), "versión antigua", ct);

        var paquete = EscribirPaquete("paquete.zip", "version.txt", "contenido nuevo");

        var resultado = await Watchdog().ActualizarAsync(paquete, instalacion, ct);

        File.Exists(Path.Combine(resultado.CarpetaPrevia, "AtlasRent.Api.exe"))
            .Should().BeTrue("los archivos de la instalación anterior quedan guardados");
        (await File.ReadAllTextAsync(Path.Combine(instalacion, "version.txt"), ct))
            .Should().Be("contenido nuevo", "y después el paquete se aplica encima");
        File.Exists(resultado.RutaCopiaSeguridad).Should().BeTrue(
            "la copia de seguridad que ampara la actualización se dice cuál fue");
    }

    /// <summary>
    /// Restaurar una copia que no verifica no escribe nada en el destino. Un Watchdog que restaura
    /// desde una copia corrupta no está restaurando: está destruyendo datos y creyendo que no.
    /// </summary>
    [Fact]
    public async Task RestaurarUnaCopiaIlegibleNoEscribeNadaEnElDestino()
    {
        var ct = TestContext.Current.CancellationToken;
        var destino = Path.Combine(_raiz, "destino");

        var ruta = Path.Combine(_copias, ServicioBackup.NombreDeCopia(new DateOnly(2026, 10, 15)));
        await File.WriteAllBytesAsync(ruta, "no es una copia"u8.ToArray(), ct);

        var accion = () => Watchdog().RestaurarAsync(Path.GetFileName(ruta), destino, ct);

        await accion.Should().ThrowAsync<WatchdogSinCopiaPreviaException>();
        Directory.Exists(destino).Should().BeFalse("no se creó siquiera la carpeta de destino");
    }

    /// <summary>Con una copia buena, la restauración deja los documentos y el volcado en su sitio.</summary>
    [Fact]
    public async Task RestaurarUnaCopiaValidaDejaDocumentosYVolcado()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllBytesAsync(Path.Combine(_documentos, "titulo.pdf"), "%PDF-1.7"u8.ToArray(), ct);

        var hecha = await Copias().EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        var destino = Path.Combine(_raiz, "destino");
        var resultado = await Watchdog().RestaurarAsync(Path.GetFileName(hecha.RutaArchivo), destino, ct);

        resultado.Documentos.Should().Be(1);
        File.Exists(Path.Combine(destino, "documentos", "titulo.pdf")).Should().BeTrue();
        (await File.ReadAllTextAsync(resultado.RutaDump, ct)).Should().Contain("Puerta del Este");
    }

    private string EscribirPaquete(string nombre, string entrada, string contenido)
    {
        var ruta = Path.Combine(_raiz, nombre);
        using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
        {
            using var escritor = new StreamWriter(zip.CreateEntry(entrada).Open());
            escritor.Write(contenido);
        }

        return ruta;
    }
}

/// <summary>
/// El destino remoto de Google Drive. La implementación pendiente dice la verdad en vez de simular
/// una subida: una copia que "está en Drive" cuando no lo está es peor que no tener Drive.
/// </summary>
public sealed class AlmacenRemotoDriveTests
{
    [Fact]
    public async Task ElAlmacenRemotoPendienteDiceQueDependeDeF004()
    {
        var ct = TestContext.Current.CancellationToken;

        var accion = () => new AlmacenRemotoDriveNoDisponible()
            .SubirAsync("C:\\cualquier\\atlasrent.arbk", ct);

        (await accion.Should().ThrowAsync<NotImplementedException>())
            .WithMessage("*F0-04*");
    }

    /// <summary>
    /// Una copia local correcta no se pierde porque Drive todavía no exista: se guarda en local, que
    /// es donde está, y el fallo del remoto no la invalida.
    /// </summary>
    [Fact]
    public async Task UnaCopiaCorrectaSeGuardaAunqueElRemotoNoExista()
    {
        var ct = TestContext.Current.CancellationToken;
        var raiz = Path.Combine(Path.GetTempPath(), $"atlas-rent-remoto-{Guid.NewGuid():N}");
        var documentos = Path.Combine(raiz, "documentos");
        Directory.CreateDirectory(documentos);

        try
        {
            var opciones = new OpcionesBackup
            {
                CarpetaLocal = Path.Combine(raiz, "copias"),
                ClaveCifrado = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            };

            var servicio = new ServicioBackup(
                opciones,
                new VolcadoDeEjemplo(),
                new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = documentos }),
                new ServicioCifradoCopia(opciones),
                TimeProvider.System,
                new AlmacenRemotoDriveNoDisponible());

            var resultado = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

            File.Exists(resultado.RutaArchivo).Should().BeTrue(
                "la copia local se escribe y verifica antes de intentar nada remoto");
            (await servicio.VerificarAsync(resultado.RutaArchivo, ct))
                .Should().NotBeNull("y sigue siendo válida");
        }
        finally
        {
            if (Directory.Exists(raiz))
            {
                Directory.Delete(raiz, recursive: true);
            }
        }
    }

    private sealed class VolcadoDeEjemplo : IVolcadoBaseDatos
    {
        public Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct) => Task.FromResult(
            Encoding.UTF8.GetBytes("COPY public.cliente (id) FROM stdin;\nun-dato\n\\.\n"));
    }
}