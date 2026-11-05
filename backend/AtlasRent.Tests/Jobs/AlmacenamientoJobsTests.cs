using AtlasRent.Api.Jobs;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Jobs;

/// <summary>
/// F1-12: el almacenamiento de Hangfire, en su propia base de datos.
/// <para>
/// Este test es el que respalda el SQL que hay que pasar a mano como superusuario (docs/DOC_TECNICA.md
/// §8): la base la crea un superusuario porque <c>rent_migrador</c> no tiene permiso CREATE en ella,
/// y las tablas las crea después el propio planificador con <c>PrepareSchemaIfNecessary</c> al arrancar
/// la API la primera vez.
/// </para>
/// <para>
/// Y comprueba las dos razones por las que el planificador no puede vivir en <c>atlas_rent</c>: el
/// <c>pg_dump</c> del backup diario recorre la base entera y bloquearía el volcado, y
/// <c>rent_app</c> no tiene nada que hacer ahí.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AlmacenamientoJobsTests(ContenedorBdFixture bd)
{
    [Fact]
    public async Task LaBaseLaCreaElSuperusuarioYLasTablasElPlanificadorConSuPropioRol()
    {
        var ct = TestContext.Current.CancellationToken;

        var opciones = new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = true };

        // Montar el almacenamiento es lo que hace que el planificador cree sus tablas. Es el mismo
        // camino que recorre la API al arrancar, sin levantar ningún servidor de jobs.
        var almacenamiento = new PostgreSqlStorage(
            new NpgsqlConnectionFactory(CadenaDeJobs(), opciones),
            opciones);

        (await TablasDe(CadenaDeJobs(), ct))
            .Should().Contain(["hangfire.job", "hangfire.jobqueue", "hangfire.server"],
                "el planificador crea su propio esquema y sus tablas al montar el almacenamiento");

        almacenamiento.Should().NotBeNull();
    }

    [Fact]
    public async Task ElBackupDeAtlasRentNoSeVeAfectadoPorLasTablasDelPlanificador()
    {
        var ct = TestContext.Current.CancellationToken;

        // El motivo de que el planificador tenga su propia base: pg_dump recorre atlas_rent entera
        // y hace LOCK TABLE sobre todo lo que encuentra. Con las tablas de Hangfire dentro, el
        // volcado del backup diario falla con "permission denied for schema" y el backup deja de
        // hacerse. Al estar en otra base, el volcado de la aplicación no las ve.
        var tablasDeLaAplicacion = await TablasDe(bd.CadenaSuperusuario, ct);
        tablasDeLaAplicacion.Should().NotContain("hangfire.job",
            "ninguna tabla del planificador puede acabar en la base de la aplicación");
    }

    [Fact]
    public async Task ElRolDelPlanificadorNoPuedeConectarseAlaBaseDeLaAplicacion()
    {
        var ct = TestContext.Current.CancellationToken;

        // SEC-020: es la credencial que lleva el proceso de la API. Si conectara a atlas_rent con
        // privilegios de propietario podría desactivar el trigger append-only de auditoria.
        var cadena = new NpgsqlConnectionStringBuilder(bd.CadenaJobs) { Database = "atlas_rent" }.ConnectionString;
        await using var conexion = new NpgsqlConnection(cadena);
        var accion = async () => await conexion.OpenAsync(ct);

        (await accion.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ElRolDelPlanificadorNoPerteneceAlPropietarioDelEsquema()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT pg_has_role('rent_jobs', 'rent_owner', 'USAGE') OR (SELECT rolsuper FROM pg_roles WHERE rolname = 'rent_jobs')",
            conexion);

        ((bool)(await comando.ExecuteScalarAsync(ct))!).Should().BeFalse();
    }

    [Fact]
    public async Task ElMigradorNoPuedeConectarseALaBaseDelPlanificador()
    {
        var ct = TestContext.Current.CancellationToken;
        var cadena = new NpgsqlConnectionStringBuilder(bd.CadenaMigrador) { Database = ProgramacionJobs.BaseDeDatos }.ConnectionString;
        await using var conexion = new NpgsqlConnection(cadena);
        var accion = async () => await conexion.OpenAsync(ct);

        (await accion.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ElRolDeLaAplicacionNoPuedeNiConectarseALaBaseDelPlanificador()
    {
        var ct = TestContext.Current.CancellationToken;

        var cadenaApp = new NpgsqlConnectionStringBuilder(bd.CadenaApp)
        {
            Database = ProgramacionJobs.BaseDeDatos,
        }.ConnectionString;

        await using var conexion = new NpgsqlConnection(cadenaApp);
        var accion = async () =>
        {
            await conexion.OpenAsync(ct);
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM job", conexion);
            await comando.ExecuteScalarAsync(ct);
        };

        (await accion.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>Cadena del planificador: la del rol rent_jobs (SEC-020), sobre su base.</summary>
    private string CadenaDeJobs() => ProgramacionJobs.ConexionDeJobs(bd.CadenaJobs);

    /// <summary>Nombres de las tablas que esa conexión puede ver, de todos los esquemas.</summary>
    private static async Task<List<string>> TablasDe(string cadena, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            SELECT schemaname || '.' || tablename
            FROM pg_catalog.pg_tables
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY schemaname, tablename
            """,
            conexion);

        var nombres = new List<string>();
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            nombres.Add(lector.GetString(0));
        }

        return nombres;
    }
}