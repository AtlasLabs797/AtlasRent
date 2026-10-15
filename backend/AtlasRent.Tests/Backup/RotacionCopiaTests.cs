using System.Text;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Backup;

/// <summary>
/// F1-12: rotación, idempotencia por fecha y qué pasa cuando la copia falla.
/// <para>
/// Aquí el volcado es falso a propósito: estas reglas son de la rotación y del fallo, no de
/// PostgreSQL, y probarlas contra la base real solo las haría lentas.
/// </para>
/// </summary>
public sealed class RotacionCopiaTests : IDisposable
{
    private const string DumpConDatos = """
        COPY public.cliente (id, nombre) FROM stdin;
        8f0e0000-0000-0000-0000-000000000001	Puerta del Este
        \.
        """;

    private readonly string _carpeta =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-rotacion-{Guid.NewGuid():N}");

    private readonly OpcionesBackup _opciones;

    public RotacionCopiaTests()
    {
        _opciones = new OpcionesBackup
        {
            CarpetaLocal = _carpeta,
            ClaveCifrado = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            Conservar = OpcionesBackup.ConservarPorDefecto,
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Volcado falso: sirve bytes en claro con filas, o falla, según se le pida.</summary>
    private sealed class VolcadoFalso(string? contenido) : IVolcadoBaseDatos
    {
        public Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct) =>
            contenido is null
                ? throw new CopiaSeguridadFallidaException("la base no responde")
                : Task.FromResult(Encoding.UTF8.GetBytes(contenido));
    }

    private ServicioBackup Crear(IVolcadoBaseDatos volcado, TimeProvider reloj) => Crear(volcado, reloj, null);

    /// <summary>Como <see cref="Crear(IVolcadoBaseDatos, TimeProvider)"/>, injecting el registro de alertas.</summary>
    private ServicioBackup Crear(IVolcadoBaseDatos volcado, TimeProvider reloj, IRegistroAlertas? alertas) => new(
        _opciones,
        volcado,
        new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = Path.Combine(_carpeta, "documentos") }),
        new ServicioCifradoCopia(_opciones),
        reloj,
        almacenRemoto: null,
        alertas: alertas);

    /// <summary>Alerta en memoria: guarda lo que se le pasó para poder mirarlo en el test.</summary>
    private sealed class AlertaEnMemo(List<(string Tipo, string Clave, string Mensaje)> registradas) : IRegistroAlertas
    {
        public List<(string Tipo, string Clave, string Mensaje)> Registradas { get; } = registradas;

        public Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct)
        {
            Registradas.Add((tipo, clave, mensaje));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Un respaldo que falla tiene que dejar rastro, no solo una línea de log que nadie mira: es el
    /// "fallo → alerta" del criterio de aceptación de F1-12. Y como la clave lleva la fecha, el
    /// mismo fallo de mañana es otra alerta y no se pisan.
    /// </summary>
    [Fact]
    public async Task UnaCopiaQueFallaDejaAlertaConLaFechaDelDia()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var alertas = new AlertaEnMemo([]);
        var servicio = Crear(new VolcadoFalso(null), reloj, alertas);

        var accion = () => servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        await accion.Should().ThrowAsync<CopiaSeguridadFallidaException>();

        var registro = alertas.Registradas.Should().ContainSingle().Subject;
        registro.Tipo.Should().Be("backup");
        registro.Clave.Should().Be($"backup:{servicio.FechaDeHoy:yyyy-MM-dd}", "una alerta por día de respaldo");
        registro.Mensaje.Should().Contain("copia de seguridad", "el motivo tiene que explicar qué pasó");
    }

    /// <summary>
    /// Un respaldo que sí sale bien no genera alertas. Si las generara, el día que el backup va bien
    /// habría que limpiar y la señal dejaría de significar algo.
    /// </summary>
    [Fact]
    public async Task UnaCopiaQueSaleBienNoDejaNingunaAlerta()
    {
        var ct = TestContext.Current.CancellationToken;
        var alertas = new AlertaEnMemo([]);
        var servicio = Crear(new VolcadoFalso(DumpConDatos), new RelojPrueba(), alertas);

        await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        alertas.Registradas.Should().BeEmpty();
    }

    /// <summary>
    /// Tras cinco días quedan cuatro copias, y son las cuatro más recientes. La más reciente nunca
    /// se borra: es la que acaba de salir bien y verificada.
    /// </summary>
    [Fact]
    public async Task LaRotacionDejaSoloLasCuatroMasRecientes()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var servicio = Crear(new VolcadoFalso(DumpConDatos), reloj);

        var fechas = new List<string>();
        for (var dia = 0; dia < 5; dia++)
        {
            reloj.Avanzar(TimeSpan.FromDays(1));
            var resultado = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
            fechas.Add(Path.GetFileName(resultado.RutaArchivo));
        }

        var copias = Directory.GetFiles(_carpeta, "*.arbk").Select(Path.GetFileName).ToList();

        copias.Should().HaveCount(4, "Backup__Conservar es 4");
        copias.Should().BeEquivalentTo(fechas.Skip(1), "son las cuatro últimas, no cuatro cualesquiera");
        Directory.GetFiles(_carpeta, "*.arbk").Should().NotContain(f => Path.GetFileName(f) == fechas[0]);
    }

    /// <summary>
    /// Dos ejecuciones del mismo día no crean dos archivos ni se pisan: la segunda se da por buena
    /// con la que ya hay, porque es la copia del día.
    /// </summary>
    [Fact]
    public async Task DosEjecucionesDelMismoDiaSonIdempotentes()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var servicio = Crear(new VolcadoFalso(DumpConDatos), reloj);

        var primera = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        var contenidoTrasLaPrimera = await File.ReadAllBytesAsync(primera.RutaArchivo, ct);

        reloj.Avanzar(TimeSpan.FromHours(6));
        var segunda = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        segunda.YaExistia.Should().BeTrue("ya había copia de hoy y verifica");
        segunda.RutaArchivo.Should().Be(primera.RutaArchivo, "una sola copia por fecha");
        (await File.ReadAllBytesAsync(segunda.RutaArchivo, ct)).Should().Equal(contenidoTrasLaPrimera);
        Directory.GetFiles(_carpeta, "*.arbk").Should().HaveCount(1, "no se duplica el archivo");
    }

    /// <summary>
    /// Un backup fallido no rota nada. Esto es lo que separa una copia de seguridad de un destructor
    /// con buena prensa: si el volcado falla y aun así se borraran copias viejas, un fallo de la
    /// base se convertiría en quedarse sin ninguna copia.
    /// </summary>
    [Fact]
    public async Task UnaCopiaQueFallaNoRotaNiBorraNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();

        // Tres días buenos, para tener con qué rottenar.
        var bueno = Crear(new VolcadoFalso(DumpConDatos), reloj);
        for (var dia = 0; dia < 3; dia++)
        {
            reloj.Avanzar(TimeSpan.FromDays(1));
            await bueno.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        }

        var antes = Directory.GetFiles(_carpeta, "*.arbk").Order().ToList();
        antes.Should().HaveCount(3);

        // Ahora el cuarto día falla al volcar.
        reloj.Avanzar(TimeSpan.FromDays(1));
        var queFalla = Crear(new VolcadoFalso(null), reloj);

        var accion = () => queFalla.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        await accion.Should().ThrowAsync<CopiaSeguridadFallidaException>();

        Directory.GetFiles(_carpeta, "*.arbk").Order().Should().Equal(antes, "no se borró ninguna copia buena");
        Directory.GetFiles(_carpeta, "*.tmp").Should().BeEmpty("no quedó un archivo a medias");
    }

    /// <summary>
    /// Un volcado sin filas se rechaza y no escribe copia. Es el fallo silencioso de la RLS: el
    /// proceso termina con código 0 y el archivo parecería bueno.
    /// </summary>
    [Fact]
    public async Task UnVolcadoSinFilasNoEscribeCopia()
    {
        var ct = TestContext.Current.CancellationToken;
        var soloEsquema = "COPY public.cliente (id, nombre) FROM stdin;\n\\.\n";
        var servicio = Crear(new VolcadoFalso(soloEsquema), new RelojPrueba());

        var accion = () => servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        await accion.Should().ThrowAsync<CopiaSeguridadFallidaException>();

        Directory.GetFiles(_carpeta, "*.arbk").Should().BeEmpty(
            "una copia vacía que dice que es correcta es peor que no tener copia");
    }

    /// <summary>
    /// Una copia que no se puede descifrar no cuenta como válida ni como anterior: el Watchdog no
    /// puede restaurar desde ella.
    /// </summary>
    [Fact]
    public async Task UnaCopiaIlegibleNoCuentaComoValida()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var servicio = Crear(new VolcadoFalso(DumpConDatos), reloj);
        var resultado = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        await File.WriteAllBytesAsync(resultado.RutaArchivo, "basura que no es una copia"u8.ToArray(), ct);

        (await servicio.VerificarAsync(resultado.RutaArchivo, ct)).Should().BeNull();
        (await servicio.UltimaCopiaValidaAsync(ct)).Should().BeNull();
    }

    /// <summary>
    /// Renombrar la copia de ayer con el nombre de hoy no la convierte en la copia de hoy. Antes de
    /// que la fecha viviera dentro del contenedor, ese archivo verificaba —es auténtico y descifrable—
    /// y el servicio se lo creaba: la copia del día no se generaba, el log decía que todo estaba bien y
    /// la rotación acababa borrando las copias buenas creyendo que eran viejas.
    /// </summary>
    [Fact]
    public async Task UnaCopiaRenombradaConLaFechaDeHoyNoCuentaComoLaCopiaDeHoy()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var servicio = Crear(new VolcadoFalso(DumpConDatos), reloj);

        reloj.Avanzar(TimeSpan.FromDays(1));
        var ayer = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        // Se renombra con el nombre de mañana, que es lo que haría alguien con escritura en la carpeta.
        var manana = ayer.Fecha.AddDays(1);
        var rutaConNombreDeManana = Path.Combine(_carpeta, ServicioBackup.NombreDeCopia(manana));
        File.Move(ayer.RutaArchivo, rutaConNombreDeManana, overwrite: true);

        (await servicio.VerificarAsync(rutaConNombreDeManana, ct)).Should().BeNull(
            "el nombre dice una fecha y el contenido otra: no es una copia legítima");

        reloj.Avanzar(TimeSpan.FromDays(1));
        var resultado = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        resultado.YaExistia.Should().BeFalse("una copia de ayer no es la copia de hoy, aunque se renombre");
        resultado.Fecha.Should().Be(manana);
        (await servicio.VerificarAsync(resultado.RutaArchivo, ct)).Should().NotBeNull();
        Directory.GetFiles(_carpeta, "*.arbk").Should().HaveCount(1);
    }

    /// <summary>
    /// Los documentos de propiedad entran en la copia con su nombre. Si no entraran, un respaldo de la
    /// base sin los títulos de propiedad sería media copia de seguridad.
    /// </summary>
    [Fact]
    public async Task LosDocumentosEntranEnLaCopiaYSeRecuperan()
    {
        var ct = TestContext.Current.CancellationToken;
        var documentos = new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = Path.Combine(_carpeta, "documentos") });
        documentos.AsegurarCarpeta();
        await File.WriteAllBytesAsync(documentos.RutaDe("abc123.pdf"), "%PDF-1.7 titulo"u8.ToArray(), ct);

        var servicio = Crear(new VolcadoFalso(DumpConDatos), new RelojPrueba());
        var resultado = await servicio.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);

        resultado.Documentos.Should().Be(1);

        var copia = await servicio.VerificarAsync(resultado.RutaArchivo, ct);
        copia.Should().NotBeNull();
        copia!.Documentos().Should().ContainSingle()
            .Which.Nombre.Should().Be("abc123.pdf", "el nombre en disco es el que se conserva");
        copia.Documentos()[0].Datos.Should().Equal("%PDF-1.7 titulo"u8.ToArray());
    }
}