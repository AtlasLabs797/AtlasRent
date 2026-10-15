using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AtlasRent.Api.Data;

/// <summary>
/// Contexto que usa <c>dotnet ef</c> para diseñar migraciones.
/// <para>
/// El diseño de migraciones no abre conexión, así que aquí no hay ninguna credencial
/// (AGENTS.md §6). Para <c>dotnet ef database update</c> contra una base de desarrollo se
/// toma <c>ConnectionStrings__Default</c> del entorno si está definida.
/// </para>
/// </summary>
public sealed class AtlasRentDbContextFactory : IDesignTimeDbContextFactory<AtlasRentDbContext>
{
    private const string CadenaSoloDiseno =
        "Host=localhost;Port=5433;Database=atlas_rent;Username=rent_migrador;Include Error Detail=false";

    public AtlasRentDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? CadenaSoloDiseno;

        var opciones = new DbContextOptionsBuilder<AtlasRentDbContext>()
            .UseNpgsql(cadena)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AtlasRentDbContext(opciones);
    }
}