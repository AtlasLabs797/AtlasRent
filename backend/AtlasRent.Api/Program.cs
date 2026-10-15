using AtlasRent.Api.Data;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
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
// F1-04: el contexto de sesión pasa a ser de ámbito de petición, porque lo rellena el pipeline de
// autenticación con el usuario y el rol de esa petición y no con los de otra.
builder.Services.AddScoped<ContextoSesion>();
builder.Services.AddScoped<ContextoTransaccionInterceptor>();
builder.Services.AddDbContext<AtlasRentDbContext>((servicios, opciones) =>
    opciones
        .UseNpgsql(cadenaConexion)
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(servicios.GetRequiredService<ContextoTransaccionInterceptor>()));

builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<IntegridadAuditoria>();

// F1-04: autenticación y sesiones. La clave del JWT se lee de Seguridad__JwtClave y no tiene valor
// por defecto: si falta, la aplicación falla al arrancar en lugar de firmar con una clave inventada.
var opcionesSeguridad = OpcionesSeguridad.Desde(builder.Configuration);
builder.Services.AddSingleton(opcionesSeguridad);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ServicioContrasenas>();
builder.Services.AddScoped<ServicioSesiones>();
builder.Services.AddScoped<ServicioAutenticacion>();
builder.Services.AddScoped<EmisorTokens>();
// HaveIBeenPwned por k-anonimato: HttpClient sin paquete adicional (docs/DOC_TECNICA.md §7).
builder.Services.AddHttpClient<ServicioComplicidad>();

// El JWT de acceso viaja en la cookie HttpOnly atlas-rent-at, no en la cabecera Authorization.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opciones =>
    {
        opciones.MapInboundClaims = false;
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = OpcionesSeguridad.Emisor,
            ValidateAudience = true,
            ValidAudience = OpcionesSeguridad.Audiencia,
            ValidateLifetime = true,
            // Sin margen: un token de 15 minutos queexpires en 15 minutos.
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(opcionesSeguridad.ClaveJwt),
            RoleClaimType = "rol",
            NameClaimType = "sub",
        };
        opciones.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                var token = contexto.Request.Cookies[NombresCookies.Acceso];
                if (!string.IsNullOrEmpty(token))
                {
                    contexto.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddControllers();

// Los 400 de validación de modelo también llevan código de referencia (docs/DOC_TECNICA.md §6).
builder.Services.Configure<ApiBehaviorOptions>(opciones =>
{
    opciones.InvalidModelStateResponseFactory = contexto =>
    {
        var problema = new ValidationProblemDetails(contexto.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Datos no válidos",
        };
        problema.Extensions["codigoReferencia"] = Problemas.Validacion;

        return new BadRequestObjectResult(problema) { ContentTypes = { "application/problem+json" } };
    };
});

builder.Services.AddHealthChecks()
    .AddCheck<VivoCheck>("vivo", tags: ["vivo"])
    .AddCheck<ConexionBdCheck>("bd");

var app = builder.Build();

app.UseSerilogRequestLogging();

// Orden del pipeline de docs/DOC_TECNICA.md §7 (la parte de F1-04; el rate limiting de IP de
// F1-05 y la autorización de F1-07 todavía no están): autenticación → sesión en BD → CSRF.
app.UseAuthentication();
app.UseMiddleware<ValidacionSesionMiddleware>();
app.UseMiddleware<CsrfMiddleware>();

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