using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;

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
var opcionesLimites = OpcionesLimites.Desde(builder.Configuration);
builder.Services.AddSingleton(opcionesSeguridad);
builder.Services.AddSingleton(opcionesLimites);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ServicioContrasenas>();
builder.Services.AddScoped<ServicioSesiones>();
builder.Services.AddScoped<ServicioBloqueos>();
// F1-06: segundo factor. ServicioEmisionSesion es el camino único para abrir sesión, así que el
// login normal, el segundo factor y el acceso de emergencia auditan igual. ServicioAutenticacion
// usa ServicioMfa.NecesitaMfa, que es estático: no hay ciclo entre los dos.
builder.Services.AddScoped<ServicioEmisionSesion>();
builder.Services.AddScoped<ServicioMfa>();
builder.Services.AddScoped<ServicioEmergencia>();
builder.Services.AddSingleton<CookiesSesion>();
builder.Services.AddScoped<ServicioAutenticacion>();
builder.Services.AddScoped<EmisorTokens>();
// Sin estado: un solo sitio donde se decide qué es la IP real del cliente.
builder.Services.AddSingleton<IResolutorIpPeticion, ResolutorIpPeticion>();
// HaveIBeenPwned por k-anonimato: HttpClient sin paquete adicional (docs/DOC_TECNICA.md §7).
builder.Services.AddHttpClient<ServicioComplicidad>();

// F1-05: límite de peticiones. El global va por usuario si la petición está autenticada y por IP si
// no; el login lleva además su política, más estrecha (docs/DOC_TECNICA.md §7).
// Los contadores viven en memoria de la instancia: con una sola instancia es lo correcto; si se
// añade otra, hay que pasar esto a almacenamiento compartido (ver OpcionesLimites).
builder.Services.AddRateLimiter(opciones =>
{
    opciones.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(ClavesLimite.Global(http), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = opcionesLimites.GlobalPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    opciones.AddPolicy(PoliticasLimite.Login, http =>
        RateLimitPartition.GetFixedWindowLimiter(ClavesLimite.Login(http), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = opcionesLimites.LoginPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.OnRejected = async (contexto, ct) => await Problemas.EscribirAsync(
        contexto.HttpContext,
        Problemas.Construir(
            StatusCodes.Status429TooManyRequests,
            Problemas.LimitePeticiones,
            "Demasiadas peticiones",
            "Se superó el límite de peticiones. Espera un minuto antes de reintentar."));
});

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

// F1-07: control de acceso. La política (rol + permisos por usuario) es de ámbito de petición,
// como el contexto de sesión que lee; el filtro global es la única puerta de entrada y deniega lo
// que no declare [Accion].
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<ServicioUsuarios>();
// F1-08: configuración. ServicioIdempotencia guarda el reintento de una mutación en su misma
// transacción; ServicioConfiguracion y ServicioSeries escriben auditoría ahí dentro (AGENTS.md §6).
builder.Services.AddScoped<ServicioIdempotencia>();
builder.Services.AddScoped<ServicioConfiguracion>();
builder.Services.AddScoped<ServicioSeries>();
// F1-09: cifrado en reposo (AES-256-GCM) e índice ciego del documento. Sin estado y con las
// claves ya validadas al arrancar, por eso van como singleton.
builder.Services.AddSingleton<ServicioCifrado>();
builder.Services.AddSingleton<IndiceCiegoDocumento>();
builder.Services.AddScoped<ServicioClientes>();
builder.Services.AddScoped<ServicioContactos>();
builder.Services.AddScoped<ServicioProspectos>();
// F1-10: inmuebles, unidades y documentos de propiedad. La carpeta de documentos es configuración
// y no una constante (Almacen__Documentos): en desarrollo, pruebas y producción son tres sitios.
builder.Services.AddSingleton(OpcionesAlmacen.Desde(builder.Configuration));
builder.Services.AddSingleton<AlmacenDocumentos>();
builder.Services.AddScoped<ServicioInmuebles>();
builder.Services.AddScoped<ServicioDocumentosInmueble>();
// F2-01: disponibilidad. No tiene endpoints propios: la llaman los servicios de reservas
// (F2-03) y de contratos (F2-05) desde su transacción, porque el bloqueo va escrito en la misma
// que la fila de negocio que lo origina.
builder.Services.AddScoped<ServicioDisponibilidad>();
// F2-02: cotizaciones. El PDF lo compone DocumentoCotizacionPdf con QuestPDF (docs/DESIGN.md §7).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddScoped<ServicioCotizaciones>();
// F2-03: reservas. Es scoped por lo mismo que la disponibilidad: abre su propia transacción y
// escribe auditoría e idempotencia.
builder.Services.AddScoped<ServicioReservas>();
// F2-04: calendario de disponibilidad. Scoped y solo lectura: no escribe nada ni abre transacción,
// porque es una consulta a ocupacion_rango y ocupacion_dia (F2-01).
builder.Services.AddScoped<ServicioCalendario>();
// F2-05: contratos. Scoped y con los servicios de reservas y disponibilidad dentro, porque al
// activar un contrato escribe el candado de la unidad y deja en conflicto las reservas que se
// solapan, todo en la misma transacción.
builder.Services.AddScoped<ServicioContratos>();
builder.Services.AddControllers(opciones => opciones.Filters.Add<FiltroAccionGlobal>());

// F1-12: copia de seguridad cifrada de la base de datos y de los documentos, con rotación. El
// volcado lo hace el cliente de PostgreSQL con el rol rent_backup (ConnectionStrings__Backup), que
// por su cuenta solo lee. El almacén remoto no se registra todavía: la subida a Google Drive
// depende de F0-04 (carpetas y cliente OAuth), y sin él la copia se queda en local.
// TODO(A#): registrar IAlmacenRemoto -> AlmacenRemotoDriveNoDisponible cuando exista el cliente
// OAuth de F0-04. La copia se queda en local hasta entonces.

// F1-12: disparo de los jobs de docs/DOC_TECNICA.md §8. Hangfire 1.8 (la edición gratuita) planifica
// en UTC y no entiende los crons con zona horaria, así que la cadencia la calcula ProgramacionJobs
// a partir de la hora de Santo Domingo en lugar de escribir un número fijo.
// El almacenamiento va en su propia base de datos (atlas_rent_jobs) porque un pg_dump con
// rent_backup hace LOCK TABLE sobre todo lo que hay en atlas_rent y el planificador le rompería el
// volcado. La credencial sí es la de migraciones (ConnectionStrings__Migraciones), no la de la
// aplicación: rent_app no debe poder escribir en las tablas del planificador. Su panel no se monta
// (docs/SEGURIDAD.md A.1).
builder.Services.AddScoped<TrabajoCopiaSeguridad>();
builder.Services.AddScoped<TrabajoIntegridadAuditoria>();

var opcionesJobs = OpcionesJobs.Desde(builder.Configuration);
builder.Services.AddSingleton(opcionesJobs);

if (opcionesJobs.ServidorActivo)
{
    var cadenaMigraciones = builder.Configuration.GetConnectionString("Migraciones")
        ?? throw new InvalidOperationException(
            "Falta la cadena de conexión 'Migraciones' para el servidor de jobs. Definir "
            + "ConnectionStrings__Migraciones (user-secrets en desarrollo): es el rol rent_migrador, "
            + "el único con permisos sobre las tablas de Hangfire.");

    var cadenaJobs = ProgramacionJobs.ConexionDeJobs(cadenaMigraciones);

    builder.Services.AddHangfire(configuracion => configuracion
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            almacenamiento => almacenamiento.UseNpgsqlConnection(cadenaJobs),
            new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = true }));
    builder.Services.AddHangfireServer(opciones => opciones.ServerName = "atlas-rent");
}
builder.Services.AddSingleton(OpcionesBackup.Desde(builder.Configuration));
builder.Services.AddSingleton<ServicioCifradoCopia>();
builder.Services.AddSingleton<IVolcadoBaseDatos>(VolcadoPgDump.EnProduccion());
builder.Services.AddScoped<ServicioBackup>();
// F1-12: el "fallo → alerta" del criterio de aceptación. Scoped porque usa el DbContext, que es
// scoped; el backup es scoped también y por eso lo puede recibir.
builder.Services.AddScoped<IRegistroAlertas, ServicioAlertas>();
// La vigilancia de la copia solo lee disco y escribe la alerta; no necesita la cadena de volcado,
// que es la que casi siempre falta cuando esto se ejecuta a mano.
builder.Services.AddScoped<TrabajoVigilanciaCopia>();

// F2-03: el aviso de la reserva que entra dentro de 7 días. Scoped porque lee la tabla reserva y
// escribe en alerta con el mismo servicio que los demás trabajos.
builder.Services.AddScoped<TrabajoAlertaReserva>();

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

// Los trabajos recurrentes se registran con el almacenamiento ya montado. AddOrUpdate escribe en
// él, así que solo tiene sentido si el servidor está encendido: apagado (tests de integración) no
// se registra nada y no se toca la base de datos de los jobs.
if (opcionesJobs.ServidorActivo)
{
    ProgramacionJobs.Registrar(app.Services.GetRequiredService<IRecurringJobManager>());
}

app.UseSerilogRequestLogging();

// Orden del pipeline de docs/DOC_TECNICA.md §7 (la parte de F1-05): IpBan → autenticación → límite
// de peticiones → sesión en BD → CSRF.
// El limitador va después de la autenticación, no antes como decía el borrador de §7: el límite
// global es por usuario cuando la petición lleva sesión, y para saberlo hace falta que el
// AuthenticationMiddleware ya haya fijado HttpContext.User. El bloqueo de IP, que es lo que
// protege de fuerza bruta, sigue estando antes de todo lo demás.
app.UseMiddleware<IpBanMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<ValidacionSesionMiddleware>();
app.UseMiddleware<CsrfMiddleware>();

// /health/live no toca dependencias: sirve para saber que el proceso está vivo.
// /health incluye la comprobación de PostgreSQL.
app.MapHealthChecks("/health/live", new() { Predicate = c => c.Tags.Contains("vivo") });
app.MapHealthChecks("/health");

app.MapControllers();

// F1-13: el backend sirve el frontend compilado. En desarrollo Vite va en su propio servidor y este
// wwwroot no existe: por eso los archivos estáticos solo se registran si hay contenido, y en ese
// caso index.html debe existir. Si no, la API arranca igual (los tests de integración, por ejemplo).
if (WebRootConIndice())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    // Rutas de la SPA: cualquier ruta que no sea /api ni /health devuelve index.js y deja que el
    // enrutador del cliente resuelva.
    app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>
/// Si hay un frontend compilado en <c>wwwroot</c>. El build del frontend se publica ahí antes de
/// generar el paquete de despliegue; en desarrollo y en pruebas no está, y la API no debe fallar
/// por eso.
/// </summary>
static bool WebRootConIndice()
{
    var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    return Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html"));
}

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