using System.Text;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Backup;

/// <summary>
/// F1-12: la copia de seguridad de verdad, con PostgreSQL de verdad y el rol <c>rent_backup</c> de
/// verdad.
/// <para>
/// Esta clase cubre el aviso heredado de F1-03: <c>rent_backup</c> no tenía permisos de lectura, así
/// que un <c>pg_dump</c> con ese rol no sacaba la base. La migración
/// <c>PermisosLecturaBackup</c> los concede.
/// </para>
/// <para>
/// Y cubre el segundo problema, más fino: con la RLS activa, <c>pg_dump</c> falla al llegar a
/// <c>auditoria</c> y a <c>documento_inmueble</c> porque saca los datos con <c>COPY ... TO STDOUT</c>,
/// que PostgreSQL no deja hacer sobre una tabla con RLS a un rol que no la ignora. Eso se arregla
/// con <c>ALTER ROLE rent_backup BYPASSRLS</c>, que solo puede dar un superusuario y que sigue
/// pendiente de <c>db/scripts/01_roles.sql</c> (TODO(A#)). Estos tests lo hacen explícito en vez de
/// dejar que el fallo se descubra en producción.
/// </para>
/// <para>
/// Esta máquina no tiene instaladas las herramientas de cliente de PostgreSQL, así que el
/// <c>pg_dump</c> se lanza dentro del propio contenedor con <c>docker exec</c>. La imagen trae el
/// cliente y su <c>pg_hba.conf</c> acepta el socket local sin contraseña, de modo que la clave del
/// rol no sale de la máquina ni aparece en ninguna línea de órdenes.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RespaldoCifradoTests(ContenedorBdFixture bd)
{
    /// <summary>Fila de referencia que se mete a propósito para poder buscarla en el volcado.</summary>
    private const string NombreDePrueba = "Cliente-de-respaldo-F1-12";

    /// <summary>
    /// Con BYPASSRLS puesto (que es como debe quedar en producción, ver 01_roles.sql), la copia se
    /// genera entera: sale el volcado con la fila sembrada y el documento de propiedad.
    /// </summary>
    [Fact]
    public async Task LaCopiaSeGeneraCifradaYTraeLaFilaSembrada()
    {
        var ct = TestContext.Current.CancellationToken;
        await sembrarClienteAsync(ct);
        await ActivarBypassRlsAsync(ct);

        var carpeta = CarpetaTemporal();
        var documentos = Path.Combine(carpeta, "documentos");
        Directory.CreateDirectory(documentos);
        await File.WriteAllBytesAsync(
            Path.Combine(documentos, "titulo.pdf"),
            "%PDF-1.7 documento de respaldo"u8.ToArray(),
            ct);

        var servicio = Servicio(carpeta, documentos);

        var resultado = await servicio.EjecutarAsync(bd.CadenaBackupDentro, ct);

        // 1) El archivo existe y no está vacío.
        File.Exists(resultado.RutaArchivo).Should().BeTrue();
        var enDisco = await File.ReadAllBytesAsync(resultado.RutaArchivo, ct);
        enDisco.Length.Should().BeGreaterThan(
            100,
            "una copia cifrada de una base con datos no puede ser un archivo diminuto");

        // 2) Lo que hay en disco está cifrado: ni el volcado ni el documento aparecen en claro.
        var textoEnDisco = Encoding.UTF8.GetString(enDisco);
        textoEnDisco.Should().NotContain("COPY public.cliente", "el volcado no puede estar en claro");
        textoEnDisco.Should().NotContain(NombreDePrueba, "los datos no pueden estar en claro");
        textoEnDisco.Should().NotContain("%PDF-1.7 documento de respaldo", "tampoco los documentos");
        textoEnDisco.Should().StartWith("ARBK", "el archivo lleva la firma del formato de copia");

        // 3) Y con la clave se recupera todo: el volcado con la fila y el documento.
        var copia = await servicio.VerificarAsync(resultado.RutaArchivo, ct);
        copia.Should().NotBeNull();

        Encoding.UTF8.GetString(copia!.Dump())
            .Should().Contain(NombreDePrueba, "la fila sembrada tiene que estar en la copia descifrada");
        copia.Documentos().Should().ContainSingle();
        copia.Documentos()[0].Datos
            .Should().Equal("%PDF-1.7 documento de respaldo"u8.ToArray());
    }

    /// <summary>
    /// Sin BYPASSRLS el volcado no sale. Lo importante es que <strong>falla ruidosamente</strong> y no
    /// deja una copia que parezca buena: es justo el fallo silencioso que esta tarea viene a
    /// tapar, y por eso la copia no llega ni a escribirse.
    /// </summary>
    [Fact]
    public async Task SinPermisoDeRlElVolcadoFallaEnVezDeSalirVacio()
    {
        var ct = TestContext.Current.CancellationToken;
        await sembrarClienteAsync(ct);
        await QuitarBypassRlsAsync(ct);

        var carpeta = CarpetaTemporal();

        var accion = () => Servicio(carpeta, Path.Combine(carpeta, "documentos"))
            .EjecutarAsync(bd.CadenaBackupDentro, ct);

        (await accion.Should().ThrowAsync<CopiaSeguridadFallidaException>())
            .WithMessage("*row-level security*");

        Directory.GetFiles(carpeta, "*.arbk")
            .Should().BeEmpty("no se escribe una copia que no tiene datos dentro");
    }

    /// <summary>
    /// El permiso de SELECT sobre la migración de F1-12: sin él el rol no lee ni las tablas
    /// normales, y el volcado no sale ni de lejos.
    /// </summary>
    [Fact]
    public async Task ElRolDeBackupLeeLasTablasConcedidasPorLaMigracion()
    {
        var ct = TestContext.Current.CancellationToken;
        await sembrarClienteAsync(ct);

        (await ContarComoAsync("cliente", ct))
            .Should().Be(1, "la migración de F1-12 le da a rent_backup SELECT sobre las tablas");
    }

    /// <summary>
    /// El rol sigue siendo de solo lectura: el permiso es de SELECT y nada más. Un rol de copia que
    /// además pudiera escribir sería una puerta más en la base.
    /// </summary>
    [Fact]
    public async Task ElRolDeBackupNoPuedeEscribir()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var conexion = new NpgsqlConnection(bd.CadenaBackup);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("UPDATE cliente SET nombre = 'no debe'", conexion);

        var accion = () => comando.ExecuteNonQueryAsync(ct);
        await accion.Should().ThrowAsync<PostgresException>();
    }

    private VolcadoPgDump VolcadoDelContenedor() =>
        new("docker", ["exec", "-i", bd.IdContenedor, "pg_dump"]);

    private ServicioBackup Servicio(string carpeta, string carpetaDocumentos)
    {
        var opciones = new OpcionesBackup
        {
            CarpetaLocal = carpeta,
            ClaveCifrado = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            Conservar = OpcionesBackup.ConservarPorDefecto,
        };

        return new ServicioBackup(
            opciones,
            VolcadoDelContenedor(),
            new AlmacenDocumentos(new OpcionesAlmacen { RutaDocumentos = carpetaDocumentos }),
            new ServicioCifradoCopia(opciones),
            TimeProvider.System);
    }

    /// <summary>
    /// Carpeta temporal propia de la prueba. Nunca <c>C:\AtlasRent\data\backups</c>: una prueba que
    /// rota y borra archivos no escribe en la carpeta de desarrollo ni en la de producción.
    /// </summary>
    private static string CarpetaTemporal() =>
        Path.Combine(Path.GetTempPath(), $"atlas-rent-respaldo-{Guid.NewGuid():N}");

    /// <summary>
    /// Pone BYPASSRLS como el superusuario, simulando lo que tiene que hacer
    /// <c>db/scripts/01_roles.sql</c> (TODO(A#)). Lo hace el superusuario del contenedor porque
    /// rent_migrador no tiene CREATEROLE y no podría.
    /// </summary>
    private async Task ActivarBypassRlsAsync(CancellationToken ct)
    {
        await EjecutarComoSuperusuarioAsync("ALTER ROLE rent_backup BYPASSRLS;", ct);
    }

    private async Task QuitarBypassRlsAsync(CancellationToken ct)
    {
        await EjecutarComoSuperusuarioAsync("ALTER ROLE rent_backup NOBYPASSRLS;", ct);
    }

    private async Task EjecutarComoSuperusuarioAsync(string sql, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Una fila conocida en la base. Es la que después se busca dentro del volcado: si el dump
    /// saliera vacío, esta fila no aparecería y el respaldo no valdría.
    /// </summary>
    private async Task sembrarClienteAsync(CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO cliente (id, numero, nombre, tipo, agente_retencion, en_gestion_cobro,
                                  recordatorios_pausados, activo, created_at)
            SELECT gen_random_uuid(), nextval('cliente_numero_seq'), @nombre, 1,
                   false, false, false, true, now()
            WHERE NOT EXISTS (SELECT 1 FROM cliente WHERE nombre = @nombre)
            """,
            conexion);
        comando.Parameters.AddWithValue("nombre", NombreDePrueba);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private async Task<long> ContarComoAsync(string tabla, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaBackup);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            $"SELECT count(*) FROM {tabla} WHERE nombre = @nombre",
            conexion);
        comando.Parameters.AddWithValue("nombre", NombreDePrueba);
        return (long)(await comando.ExecuteScalarAsync(ct))!;
    }
}