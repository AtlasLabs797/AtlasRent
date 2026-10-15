using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AtlasRent.Tests.Infra;

/// <summary>
/// PostgreSQL de un solo uso para la clase, con la estructura real de producción:
/// roles y base creados desde <c>db/scripts/01_roles.sql</c> y <c>02_basedatos.sql</c> y luego la
/// migración de F1-03 aplicada con <c>rent_migrador</c>.
/// <para>
/// Las contraseñas se generan aquí y no salen de la máquina: nunca se lee <c>.env.dev</c>
/// (AGENTS.md §6).
/// </para>
/// </summary>
public sealed class ContenedorBdFixture : IAsyncLifetime
{
    // Imagen fijada en docs/DOC_TECNICA.md §2 para desarrollo.
    private const string ImagenPostgres = "postgres:16-alpine";
    private const int PuertoPostgres = 5432;

    /// <summary>Códigos de plantilla que las pruebas de contratos citan en sus contratos.</summary>
    private static readonly string[] PlantillasDePrueba = ["estandar", "larga-2026"];

    private PostgreSqlContainer _contenedor = null!;

    /// <summary>Superusuario del contenedor (<c>postgres</c>). Solo para montar la estructura y para las pruebas de manipulación por debajo.</summary>
    public string CadenaSuperusuario { get; private set; } = string.Empty;

    /// <summary>Rol de migraciones: aplica el DDL, no debería tocar datos.</summary>
    public string CadenaMigrador { get; private set; } = string.Empty;

    /// <summary>Rol con el que corre la API.</summary>
    public string CadenaApp { get; private set; } = string.Empty;

    /// <summary>
    /// Rol con el que se hace el <c>pg_dump</c> de la copia de seguridad, con su puerto publicado
    /// en el host. F1-12 lo necesita para comprobar que el volcado sale con datos de verdad y no
    /// vacío por la RLS.
    /// </summary>
    public string CadenaBackup { get; private set; } = string.Empty;

    /// <summary>
    /// La misma base, pero con el <c>host</c> y el <c>puerto</c> que se ven desde dentro del propio
    /// contenedor. Sirve para lanzar <c>pg_dump</c> con <c>docker exec</c>: la imagen de PostgreSQL
    /// trae el cliente y su <c>pg_hba.conf</c> acepta el socket local sin contraseña, así que la
    /// clave de este rol no sale de la máquina ni aparece en ninguna línea de órdenes.
    /// </summary>
    public string CadenaBackupDentro { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var claveSuperusuario = ContrasenaAleatoria();
        var claveMigrador = ContrasenaAleatoria();
        var claveApp = ContrasenaAleatoria();
        var claveBackup = ContrasenaAleatoria();

        _contenedor = new PostgreSqlBuilder(ImagenPostgres)
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword(claveSuperusuario)
            .Build();

        await _contenedor.StartAsync();

        var builderSuperusuario = new NpgsqlConnectionStringBuilder
        {
            Host = _contenedor.Hostname,
            Port = _contenedor.GetMappedPublicPort(PuertoPostgres),
            Database = "postgres",
            Username = "postgres",
            Password = claveSuperusuario,
        };
        var cadenaSuperusuarioMantenimiento = builderSuperusuario.ConnectionString;

        builderSuperusuario.Database = "atlas_rent";
        CadenaSuperusuario = builderSuperusuario.ConnectionString;

        CadenaMigrador = Conectar("rent_migrador", claveMigrador).ConnectionString;
        CadenaApp = Conectar("rent_app", claveApp).ConnectionString;
        CadenaBackup = Conectar("rent_backup", claveBackup).ConnectionString;
        CadenaBackupDentro = new NpgsqlConnectionStringBuilder(CadenaBackup)
        {
            // localhost y 5432 son los que ve el proceso que corre dentro del contenedor.
            Host = "localhost",
            Port = PuertoPostgres,
        }.ConnectionString;

        // 1) Roles, como superusuario. El script deja las contraseñas en psql -v; aquí se sustituyen
        //    por literales generados aquí.
        var scriptRoles = (await LeerScriptAsync("01_roles.sql"))
            .Replace(":pw_migrador", $"'{claveMigrador}'", StringComparison.Ordinal)
            .Replace(":pw_app", $"'{claveApp}'", StringComparison.Ordinal)
            .Replace(":pw_backup", $"'{claveBackup}'", StringComparison.Ordinal);
        await EjecutarScriptAsync(cadenaSuperusuarioMantenimiento, scriptRoles);

        // 2) Base atlas_rent. El script tiene un \connect que parte el fichero en dos: la primera
        //    parte va contra 'postgres' (CREATE DATABASE) y la segunda contra 'atlas_rent'.
        var partesBase = (await LeerScriptAsync("02_basedatos.sql"))
            .Split("\\connect atlas_rent");
        await EjecutarScriptAsync(cadenaSuperusuarioMantenimiento, partesBase[0]);
        await EjecutarScriptAsync(CadenaSuperusuario, partesBase[1]);

        // 3) Migración de F1-03 con el rol de migraciones.
        await using (var contexto = CrearContexto(CadenaMigrador))
        {
            await contexto.Database.MigrateAsync();
        }

        // 4) F2-07: las dos plantillas de contrato que citan las pruebas de contratos. A partir de
        //    F2-07 aprobar un contrato exige que su plantilla exista en el catálogo (§3.5, congelación
        //    al aprobar), así que sin ellas ninguna prueba de contratos llegaría a aprobado. Se siembran
        //    sin texto: un catálogo de plantillas lo escribe el responsable, no la infraestructura.
        await SembrarPlantillasDePruebaAsync();
    }

    public async ValueTask DisposeAsync() => await _contenedor.DisposeAsync();

    /// <summary>
    /// Identificador del contenedor, para lanzar el <c>pg_dump</c> de dentro con
    /// <c>docker exec</c>. Esta máquina no tiene las herramientas de cliente de PostgreSQL
    /// instaladas y el cliente que trae la imagen sí está.
    /// </summary>
    public string IdContenedor => _contenedor.Id;

    /// <summary>Contexto con la misma nomenclatura e interceptor que usa la API.</summary>
    public AtlasRentDbContext CrearContexto(string cadenaConexion, ContextoSesion? sesion = null)
    {
        var opciones = new DbContextOptionsBuilder<AtlasRentDbContext>()
            .UseNpgsql(cadenaConexion)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new ContextoTransaccionInterceptor(sesion ?? new ContextoSesion()))
            .Options;

        return new AtlasRentDbContext(opciones);
    }


    /// <summary>
    /// Plantillas de contrato que usan las pruebas de contratos, sembradas si no están. Es dato de
    /// prueba, no del catálogo real: el texto de una plantilla legal no lo escribe la base.
    /// </summary>
    private async Task SembrarPlantillasDePruebaAsync()
    {
        var ahora = DateTimeOffset.UtcNow;
        await using var contexto = CrearContexto(CadenaMigrador);
        foreach (var codigo in PlantillasDePrueba)
        {
            if (await contexto.PlantillaContrato.AnyAsync(p => p.Codigo == codigo))
            {
                continue;
            }

            contexto.PlantillaContrato.Add(new PlantillaContrato
            {
                Id = Guid.NewGuid(),
                Codigo = codigo,
                Nombre = $"Plantilla de prueba {codigo}",
                Secciones = [],
                AnexosTipos = [],
                Activa = true,
                CreatedAt = ahora,
            });
        }

        await contexto.SaveChangesAsync();
    }

    private NpgsqlConnectionStringBuilder Conectar(string rol, string clave) => new()
    {
        Host = _contenedor.Hostname,
        Port = _contenedor.GetMappedPublicPort(PuertoPostgres),
        Database = "atlas_rent",
        Username = rol,
        Password = clave,
    };

    private static async Task<string> LeerScriptAsync(string nombreArchivo) =>
        await File.ReadAllTextAsync(Path.Combine(RutaRaizRepo(), "db", "scripts", nombreArchivo));

    private static async Task EjecutarScriptAsync(string cadenaConexion, string sql)
    {
        await using var conexion = new NpgsqlConnection(cadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Sube desde el directorio de salida del test hasta la raíz del repositorio, donde están
    /// los scripts de base de datos.
    /// </summary>
    private static string RutaRaizRepo()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null
            && !File.Exists(Path.Combine(directorio.FullName, "db", "scripts", "01_roles.sql")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName
            ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio (db/scripts/01_roles.sql).");
    }

    private static string ContrasenaAleatoria() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
}