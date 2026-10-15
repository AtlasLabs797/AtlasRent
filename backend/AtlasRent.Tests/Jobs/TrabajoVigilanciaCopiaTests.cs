using System.Text;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Jobs;

/// <summary>
/// F1-12: el trabajo que detecta que la copia del día <b>no se hizo</b>.
/// <para>
/// Es el único mecanismo del proyecto que puede indistinguishablemente dejar pasar el peor fallo de
/// un respaldo: el proceso apagado a las 02:00 no lanza nada, no deja log de error y no da
/// excepción. Lo único que lo delata es que a las 04:00 el archivo no está.
/// </para>
/// <para>
/// Aquí el volcado es falso, como en <c>RotacionCopiaTests</c>: lo que se prueba es la vigilancia,
/// no PostgreSQL.
/// </para>
/// </summary>
public sealed class TrabajoVigilanciaCopiaTests : IDisposable
{
    private const string DumpConDatos = """
        COPY public.cliente (id, nombre) FROM stdin;
        8f0e0000-0000-0000-0000-000000000001	Puerta del Este
        \.
        """;

    private readonly string _carpeta =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-vigilancia-{Guid.NewGuid():N}");

    private readonly OpcionesBackup _opciones;

    public TrabajoVigilanciaCopiaTests()
    {
        _opciones = new OpcionesBackup
        {
            CarpetaLocal = _carpeta,
            ClaveCifrado = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
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

    /// <summary>Volcado falso con filas, para poder generar una copia buena sin PostgreSQL.</summary>
    private sealed class VolcadoFalso : IVolcadoBaseDatos
    {
        public Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct) =>
            Task.FromResult(Encoding.UTF8.GetBytes(DumpConDatos));
    }

    private sealed class AlertaEnMemo : IRegistroAlertas
    {
        public List<(string Tipo, string Clave, string Mensaje)> Registradas { get; } = [];

        public Task RegistrarAsync(string tipo, string clave, string mensaje, CancellationToken ct)
        {
            Registradas.Add((tipo, clave, mensaje));
            return Task.CompletedTask;
        }

        // El trabajo de vigilancia solo registra; el cierre de alertas es de F2-09.
        public Task ResolverAsync(string clave, CancellationToken ct) => Task.CompletedTask;
    }

    private ServicioBackup CrearServicio(TimeProvider reloj) => new(
        _opciones,
        new VolcadoFalso(),
        new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = Path.Combine(_carpeta, "documentos") }),
        new ServicioCifradoCopia(_opciones),
        reloj);

    /// <summary>
    /// El caso bueno: la copia existe y verifica, así que la vigilancia no dice nada. Si lo hiciera,
    /// el operador recibiría una alerta diaria que no significa nada.
    /// </summary>
    [Fact]
    public async Task ConLaCopiaDelDiaEnVerdeNoSeRegistraNingunaAlerta()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var alertas = new AlertaEnMemo();
        var copias = CrearServicio(reloj);

        await copias.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        await new TrabajoVigilanciaCopia(copias, alertas).EjecutarAsync(ct);

        alertas.Registradas.Should().BeEmpty("la copia del día está y verifica");
    }

    /// <summary>
    /// El caso que motiva el trabajo: la copia del día no existe porque el trabajo de las 02:00 no
    /// llegó a ejecutarse. No hubo excepción ni log de fallo en ningún sitio, así que esta alerta es
    /// la única señal.
    /// </summary>
    [Fact]
    public async Task SiNoHayCopiaDelDiaSeAvisaYSeDiceQueNoSeEjecuto()
    {
        var ct = TestContext.Current.CancellationToken;
        var alertas = new AlertaEnMemo();
        var vigilancia = new TrabajoVigilanciaCopia(CrearServicio(new RelojPrueba()), alertas);

        await vigilancia.EjecutarAsync(ct);

        var alerta = alertas.Registradas.Should().ContainSingle().Subject;
        alerta.Tipo.Should().Be("backup");
        alerta.Clave.Should().StartWith("backup-ausente:");
        alerta.Mensaje.Should().Contain("no hay ningun archivo");
        alerta.Mensaje.Should().Contain("no llegara a ejecutarse",
            "el mensaje tiene que apuntar a la causa más probable, no solo al síntoma");
    }

    /// <summary>
    /// Existe pero no verifica es un problema distinto de que no exista: el archivo está pero está
    /// dañado o no se pudo descifrar. El operador actúa de otra forma, así que el motivo se distingue.
    /// </summary>
    [Fact]
    public async Task UnaCopiaQueExistePeroNoVerificaSeDistingueDeLaQueNoExiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var alertas = new AlertaEnMemo();
        var copias = CrearServicio(reloj);

        // Se genera una copia buena y luego se destruye su contenido: el archivo sigue ahí.
        var resultado = await copias.EjecutarAsync("Host=localhost;Database=atlas_rent", ct);
        await File.WriteAllBytesAsync(resultado.RutaArchivo, "basura"u8.ToArray(), ct);

        await new TrabajoVigilanciaCopia(copias, alertas).EjecutarAsync(ct);

        var mensaje = alertas.Registradas.Should().ContainSingle().Subject.Mensaje;
        mensaje.Should().Contain("existe pero no supera la verificacion");
        mensaje.Should().NotContain("no hay ningun archivo");
    }

    /// <summary>
    /// La clave lleva la fecha, así que la vigilancia de un día y la de otro no se pisan y el mismo
    /// día no genera una fila por cada ejecución.
    /// </summary>
    [Fact]
    public async Task LaClaveDeLaAlertaLlevaLaFechaDelDia()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        var alertas = new AlertaEnMemo();
        var copias = CrearServicio(reloj);

        // Dos días seguidos sin copia, y dos ejecuciones el segundo: son dos claves y tres filas en
        // la alerta real (la segunda del segundo día suma repeticiones).
        await new TrabajoVigilanciaCopia(copias, alertas).EjecutarAsync(ct);
        reloj.Avanzar(TimeSpan.FromDays(1));
        await new TrabajoVigilanciaCopia(copias, alertas).EjecutarAsync(ct);
        await new TrabajoVigilanciaCopia(copias, alertas).EjecutarAsync(ct);

        alertas.Registradas.Should().HaveCount(3);
        alertas.Registradas.Select(a => a.Clave).Distinct().Should().HaveCount(2, "una clave por día");
    }
}