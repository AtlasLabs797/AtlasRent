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
    /// <summary>
    /// El paso de superusuario. Sobre el contenedor de pruebas; en el servidor es el bloque de SQL
    /// de docs/DOC_TECNICA.md §8. Se rehace en cada test porque la base es del contenedor compartido
    /// y PostgreSQL no tiene <c>CREATE DATABASE IF NOT EXISTS</c>.
    /// </summary>
    private async Task PrepararBaseAsync(CancellationToken ct)
    {
        var mantenimiento = new NpgsqlConnectionStringBuilder(bd.CadenaSuperusuario)
        {
            Database = "postgres",
        }.ConnectionString;

        await using var conexion = new NpgsqlConnection(mantenimiento);
        await conexion.OpenAsync(ct);

        // PostgreSQL no tiene CREATE DATABASE IF NOT EXISTS, y el paso es idempotente porque el
        // contenedor de pruebas es el mismo para toda la ejecución.
        bool existe;
        await using (var consulta = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @nombre)", conexion))
        {
            consulta.Parameters.AddWithValue("@nombre", ProgramacionJobs.BaseDeDatos);
            existe = (bool)(await consulta.ExecuteScalarAsync(ct))!;
        }

        if (!existe)
        {
            await using var crear = new NpgsqlCommand(
                $"""
                CREATE DATABASE {ProgramacionJobs.BaseDeDatos}
                  OWNER rent_owner ENCODING 'UTF8' TEMPLATE template0
                """,
                conexion);
            await crear.ExecuteNonQueryAsync(ct);
        }

        // El resto, dentro de la base nueva y como superusuario, que es lo que hace 02_basedatos.sql
        // con atlas_rent.
        var enJobs = new NpgsqlConnectionStringBuilder(bd.CadenaSuperusuario)
        {
            Database = ProgramacionJobs.BaseDeDatos,
        }.ConnectionString;

        await using var conexionJobs = new NpgsqlConnection(enJobs);
        await conexionJobs.OpenAsync(ct);
        await using var ajuste = new NpgsqlCommand(
            $"""
            REVOKE ALL ON DATABASE {ProgramacionJobs.BaseDeDatos} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {ProgramacionJobs.BaseDeDatos} TO rent_migrador;
            """,
            conexionJobs);
        await ajuste.ExecuteNonQueryAsync(ct);
    }

    [Fact]
    public async Task LaBaseLaCreaElSuperusuarioYLasTablasElPlanificadorConElRolDeMigraciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await PrepararBaseAsync(ct);

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
        await PrepararBaseAsync(ct);

        // El motivo de que el planificador tenga su propia base: pg_dump recorre atlas_rent entera
        // y hace LOCK TABLE sobre todo lo que encuentra. Con las tablas de Hangfire dentro, el
        // volcado del backup diario falla con "permission denied for schema" y el backup deja de
        // hacerse. Al estar en otra base, el volcado de la aplicación no las ve.
        var tablasDeLaAplicacion = await TablasDe(bd.CadenaSuperusuario, ct);
        tablasDeLaAplicacion.Should().NotContain("hangfire.job",
            "ninguna tabla del planificador puede acabar en la base de la aplicación");
    }

    [Fact]
    public async Task ElRolDeLaAplicacionNoPuedeNiConectarseALaBaseDelPlanificador()
    {
        var ct = TestContext.Current.CancellationToken;
        await PrepararBaseAsync(ct);

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

    /// <summary>Cadena del planificador: la del migrador con la base de los jobs.</summary>
    private string CadenaDeJobs() => ProgramacionJobs.ConexionDeJobs(bd.CadenaMigrador);

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