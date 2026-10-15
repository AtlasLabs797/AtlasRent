using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AtlasRent.Api.Data;

/// <summary>
/// Contexto de Atlas Rent. La nomenclatura <c>snake_case</c> la pone
/// <c>UseSnakeCaseNamingConvention()</c> (docs/DOC_TECNICA.md §4.2).
/// <para>
/// F1-03 deja solo <see cref="Auditoria"/>; las entidades de usuarios, contratos y facturas
/// llegan en F1-04 y siguientes.
/// </para>
/// </summary>
public sealed class AtlasRentDbContext(DbContextOptions<AtlasRentDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Auditoria> Auditoria => Set<Auditoria>();

    protected override void ConfigureConventions(ModelConfigurationBuilder convenciones)
    {
        // AGENTS.md §6: fechas de negocio en date, marcas de tiempo en timestamptz, dinero en numeric.
        convenciones.Properties<DateTime>().HaveColumnType("timestamp with time zone");
        convenciones.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone");
        convenciones.Properties<DateOnly>().HaveColumnType("date");
        convenciones.Properties<Guid>().HaveColumnType("uuid");
        // El tipo de cambio (numeric(18,6)) se configura en su entidad: lo explícito gana a la convención.
        convenciones.Properties<decimal>().HaveColumnType("numeric(18,2)");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Auditoria>(entidad =>
        {
            entidad.ToTable("auditoria");
            entidad.HasKey(e => e.Id);
            // Columnas de texto sin límite fijo (docs/DOC_TECNICA.md §4.3): los límites
            // de longitud los fija cada dominio, no esta base.
            entidad.Property(e => e.Accion).IsRequired();
            entidad.Property(e => e.Datos).HasColumnType("jsonb");
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.Firma).IsRequired();
            entidad.Property(e => e.FirmaAnterior).IsRequired();
            entidad.Property(e => e.Secuencia).HasColumnName("secuencia");
            // La app fija created_at para que coincida con lo firmado; el default de la tabla
            // solo cubre los inserts hechos a mano (script, job, consola).
            entidad.Property(e => e.CreatedAt).HasColumnName("created_at");
            entidad.HasIndex(e => e.Secuencia).IsUnique();
            entidad.HasIndex(e => e.CreatedAt);
        });

        // docs/DOC_TECNICA.md §4.2: concurrencia optimista con la columna de sistema xmin y sin
        // columna extra. Npgsql mapea a xmin toda propiedad uint OnAddOrUpdate marcada como token
        // de concurrencia (lo mismo que IsRowVersion).
        // Auditoria se queda sin token: es append-only, no hay UPDATE que lo use, y EF Core
        // pediría el valor con RETURNING, que PostgreSQL filtra con la política de SELECT de la
        // tabla; con RETURNING, un empleado no podría registrar su propia auditoría.
        foreach (var tipoEntidad in modelBuilder.Model.GetEntityTypes()
            .Where(t => t.ClrType != typeof(Auditoria)))
        {
            var xmin = tipoEntidad.AddProperty("xmin", typeof(uint));
            xmin.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            xmin.IsConcurrencyToken = true;
        }
    }
}