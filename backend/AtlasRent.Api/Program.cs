using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Serilog;
using Serilog.Events;

// Punto de entrada de AtlasRent.Api. Orden del pipeline documentado en docs/DOC_TECNICA.md §7.
// F1-01 deja el esqueleto: Serilog, health checks y conexión a PostgreSQL de desarrollo.
var builder = WebApplication.CreateBuilder(args);

// Serilog primero: cualquier fallo posterior queda registrado.
// ReadFrom.Configuration ya lee también las variables Serilog__* del entorno.
builder.Host.UseSerilog((context, configuracion) => configuracion
    .ReadFrom.Configuration(context.Configuration)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console());

// Conexión a PostgreSQL. El data source lo usan EF Core, los jobs y el health check; Npgsql 10
// no expone una extensión de DI, así que se registra a mano.
var cadenaConexion = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'Default'. Definir ConnectionStrings__Default (user-secrets en desarrollo).");
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(cadenaConexion).Build());

// F1-03: contexto, convenciones de columna (snake_case, numeric, timestamptz, uuid) y el interceptor
// que fija app.usuario_id / app.rol al abrir cada transacción para las políticas de RLS.
builder.Services.AddSingleton<ContextoSesion>();
builder.Services.AddSingleton<ContextoTransaccionInterceptor>();
builder.Services.AddDbContext<AtlasRentDbContext>((servicios, opciones) =>
    opciones
        .UseNpgsql(cadenaConexion)
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(servicios.GetRequiredService<ContextoTransaccionInterceptor>()));

builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<IntegridadAuditoria>();

builder.Services.AddHealthChecks()
    .AddCheck<VivoCheck>("vivo", tags: ["vivo"])
    .AddCheck<ConexionBdCheck>("bd");

builder.Services.AddControllers();

var app = builder.Build();

app.UseSerilogRequestLogging();

// /health/live no toca dependencias: sirve para saber que el proceso está vivo.
// /health incluye la comprobación de PostgreSQL.
app.MapHealthChecks("/health/live", new() { Predicate = c => c.Tags.Contains("vivo") });
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();

/// <summary>Expuesto para <c>WebApplicationFactory</c> en los tests de integración.</summary>
public partial class Program;

/// <summary>Comprobación de vida del proceso: no depende de nada externo.</summary>
internal sealed class VivoCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}

/// <summary>Abre una conexión real contra PostgreSQL y ejecuta <c>SELECT 1</c>.</summary>
internal sealed class ConexionBdCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conexion = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var comando = new NpgsqlCommand("SELECT 1", conexion);
            await comando.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            // Sin datos de conexión ni credenciales en el mensaje: solo el tipo de fallo.
            return HealthCheckResult.Unhealthy(
                "No hay conexión con PostgreSQL",
                data: new Dictionary<string, object> { ["tipo_error"] = e.GetType().Name });
        }
    }
}