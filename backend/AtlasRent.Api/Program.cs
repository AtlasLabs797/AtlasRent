using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Middleware;
using AtlasRent.Api.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<ValidadorContrasena>();

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
                // F6-01: el token de agente viaja en Authorization: Bearer y no es un JWT. Si se
                // dejara, JwtBearer lo validaría como tal y devolvería 401 antes de que lo viera
                // AutenticacionAgenteMiddleware. Que la cabecera mande aquí no abre ninguna puerta:
                // las peticiones de agente pasan por el mismo techo de rol que las demás.
                if (contexto.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    contexto.Token = null;
                    return Task.CompletedTask;
                }

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
// F6-01: tokens de agente. Servicio y opciones sin estado; el contexto de sesión que fija el
// middleware es el mismo que el de la sesión de cookie, así que RLS no se entera de la diferencia.
// El segundo filtro solo actúa en peticiones con token de agente, para exigir que la acción esté en
// la lista del token además de estar en el techo del propietario (F6-01).
builder.Services.AddSingleton(OpcionesAgentes.Desde(builder.Configuration));
builder.Services.AddScoped<ServicioAgentes>();

// F6-02: el asistente. Dos implementaciones de IProveedorIA, no cuatro: la API de mensajes de
// Anthropic y el dialecto chat/completions que OpenAI, OpenRouter y MiniMax ya comparten. Los
// clientes son de IHttpClientFactory y con nombre, para que una prueba sustituya el transporte con
// un HttpMessageHandler falso y capture la petición sin llamar a ninguna IA de verdad.
builder.Services.AddSingleton(OpcionesIa.Desde(builder.Configuration));

// Holgado a propósito: un proveedor lento tiene que acabar en 503, no cortar la petición a los 30 s
// de TimeProvider, que además dejaría la llamada colgada sin auditar.
var tiempoEsperaProveedor = TimeSpan.FromSeconds(45);

builder.Services
    .AddHttpClient(ProveedorAnthropic.NombreCliente, http => http.Timeout = tiempoEsperaProveedor);
builder.Services
    .AddHttpClient(ProveedorChatCompletions.NombreCliente, http => http.Timeout = tiempoEsperaProveedor);
builder.Services.AddScoped<IProveedorIA, ProveedorAnthropic>();
builder.Services.AddScoped<IProveedorIA, ProveedorChatCompletions>();
builder.Services.AddScoped<ServicioConsultaIa>();
// F6-03: el camino de escritura. Depende del servicio de aprobaciones (que lo reparte por tipo) y
// del de recordatorios (que es lo que se ejecuta al aprobar), así que se registra después de ambos.
builder.Services.AddScoped<ServicioPropuestasAgente>();
builder.Services.AddControllers(opciones =>
{
    opciones.Filters.Add<FiltroAccionGlobal>();
    opciones.Filters.Add<FiltroAccionAgente>();
})
.AddJsonOptions(json =>
{
    // SEC-030, docs/SEGURIDAD.md A.5: un campo que el DTO no conoce es un 400, en todos los DTO y
    // no solo en los que llevan el atributo (que ya no hace falta, pero no estorba).
    json.JsonSerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    json.JsonSerializerOptions.Converters.Add(new AtlasRent.Api.DTOs.ImporteJsonConverter());
});

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
// volcado. La credencial es la del rol rent_jobs (ConnectionStrings__Jobs): dueño solo de esa base,
// sin acceso a atlas_rent. Antes era rent_migrador, que pertenece a rent_owner y con él la API podía
// desactivar el trigger append-only de auditoria (SEC-020). Su panel no se monta (docs/SEGURIDAD.md A.1).
builder.Services.AddScoped<TrabajoCopiaSeguridad>();
builder.Services.AddScoped<TrabajoIntegridadAuditoria>();

var opcionesJobs = OpcionesJobs.Desde(builder.Configuration);
builder.Services.AddSingleton(opcionesJobs);

if (opcionesJobs.ServidorActivo)
{
    var cadenaBaseJobs = builder.Configuration.GetConnectionString("Jobs")
        ?? throw new InvalidOperationException(
            "Falta la cadena de conexión 'Jobs' para el servidor de jobs. Definir "
            + "ConnectionStrings__Jobs (user-secrets en desarrollo): es el rol rent_jobs, dueño de "
            + "la base atlas_rent_jobs y sin acceso a atlas_rent.");

    var cadenaJobs = ProgramacionJobs.ConexionDeJobs(cadenaBaseJobs);

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
// F2-10: lectura y cierre de la bandeja de alertas, y lectura de la auditoría.
builder.Services.AddScoped<ServicioBandejaAlertas>();
builder.Services.AddScoped<ServicioLecturaAuditoria>();
// La vigilancia de la copia solo lee disco y escribe la alerta; no necesita la cadena de volcado,
// que es la que casi siempre falta cuando esto se ejecuta a mano.
builder.Services.AddScoped<TrabajoVigilanciaCopia>();

// F2-03: el aviso de la reserva que entra dentro de 7 días. Scoped porque lee la tabla reserva y
// escribe en alerta con el mismo servicio que los demás trabajos.
builder.Services.AddScoped<TrabajoAlertaReserva>();

// F2-06: la renovación automática por bloques y los incrementos de renta (§3.5). El servicio lleva
// la lógica y el trabajo solo la dispara, para que las pruebas puedan ejercitar la renovación sin
// pasar por el planificador.
builder.Services.AddScoped<ServicioRenovaciones>();
builder.Services.AddScoped<TrabajoRenovacionContratos>();
builder.Services.AddScoped<ServicioIncrementos>();

// F2-07: catálogo de plantillas y anexos, la congelación de la plantilla al aprobar y el PDF del
// contrato. Scoped, y con el cifrado dentro porque el PDF descifra el documento del cliente (D16).
builder.Services.AddScoped<ServicioPlantillas>();

// F2-08: inventario base de la unidad, la copia congelada que se lleva el contrato al activarse y la
// revisión de salida. Scoped, y sin cifrado porque no hay fotos (§3.1 lo dice del inventario base).
builder.Services.AddScoped<ServicioInventario>();

// F2-09: el fin por fecha efectiva y la alerta persistente hasta liquidar (§3.5). Scoped, y el
// trabajo solo dispara el servicio, igual que la renovación, para que las pruebas puedan ejercitar el
// fin sin pasar por el planificador.
builder.Services.AddScoped<ServicioFines>();
builder.Services.AddScoped<TrabajoFinContratos>();

// F2-10: la bandeja de aprobaciones (§3.16) y los avisos de §3.16 que no dependen de las tablas de
// F3. Los dos son de solo lectura sobre lo que ya existe, más la escritura en `alerta` con el mismo
// servicio que los demás trabajos, así que ninguno necesita migración: `alerta` y
// `solicitud_aprobacion` ya están creadas.
builder.Services.AddScoped<ServicioAprobaciones>();
builder.Services.AddScoped<ServicioAvisosOperacion>();
builder.Services.AddScoped<TrabajoAvisosOperacion>();

// F3-01: el tipo de cambio diario (§3.12, D22). La fuente es un HttpClient con nombre detrás de una
// interfaz, para que las pruebas puedan sustituirla por un doble: el criterio de aceptación de la
// tarea es qué pasa cuando la API falla, y eso no se puede probar esperando a la API de verdad. El
// tiempo de espera es holgado porque un fallo aquí solo deja una alerta y la carga manual detrás.
builder.Services.AddSingleton(OpcionesTipoCambio.Desde(builder.Configuration));
builder.Services.AddHttpClient<IFuenteTipoCambio, ClienteTipoCambio>(
    http => http.Timeout = ClienteTipoCambio.TiempoEspera);
builder.Services.AddScoped<ServicioTiposCambio>();
builder.Services.AddScoped<TrabajoTipoCambio>();

// F3-02: facturas y proformas. Depende de ServicioSeries (el número sale del contador transaccional)
// y de ServicioTiposCambio (el cambio del día se congela al emitir, D9), y comprueba permisos por
// su cuenta porque F3-05 emite desde un trabajo sin sesión HTTP.
builder.Services.AddScoped<ServicioFacturas>();

// F3-03: numerador de comprobante fiscal. Lleva su propio contador, independiente del de la serie, y
// lo usa ServicioFacturas dentro de la transacción de emisión. Sin él, una serie fiscal no emite:
// una factura de serie fiscal sin comprobante no se puede declarar (§3.7).
builder.Services.AddScoped<ServicioComprobantes>();
builder.Services.AddScoped<TrabajoComprobantes>();

// F3-06: cobros, asignaciones FIFO y saldo a favor. Va después de ServicioFacturas porque el
// registro de un cobro comprueba las facturas del contrato, y el servicio de facturas lo usa para
// resolver cobrado, pendiente y estado de cobro sin una consulta por fila (§3.13).
// F3-07: además del pago en otra moneda (§3.10.2), el ajuste por diferencia de cambio. Su
// tolerancia NO es configuración de despliegue: sale de ajuste_general, que escribe el
// administrador en Configuración → Ajustes generales (F1-04), y sin ella no hay ajuste.
builder.Services.AddScoped<ServicioCobros>();

// F3-04: comprobantes de retención (§3.9, D14). Lo usa ServicioFacturas, que crea la fila del
// comprobante dentro de la transacción de emisión, y el trabajo diario, que avisa de las que siguen
// pendientes. Los porcentajes y la base salen de tipo_retencion, no de aquí: este servicio solo
// copia lo que la factura ya decidió.
builder.Services.AddScoped<ServicioRetenciones>();

// Catálogos de solo lectura para elegir al facturar y cobrar (api/v1/catalogos).
builder.Services.AddScoped<ServicioCatalogos>();
builder.Services.AddScoped<TrabajoRetenciones>();

// F3-05: la facturación periódica del mes natural y la factura de la reserva corta. Va después de
// ServicioFacturas porque no tiene contador propio: crea el borrador, añade la línea y emite por el
// mismo camino de F3-02, que es lo que garantiza que una factura periódica numere, congele el
// cambio y lleve comprobante igual que una tecleada a mano. El trabajo diario solo avisa de lo que
// no está facturado; facturar es de gerente o administrador (D15) y pasa por el permiso, no por él.
builder.Services.AddScoped<ServicioFacturacionPeriodica>();
builder.Services.AddScoped<TrabajoPeriodosSinFacturar>();

// F3-09: fianzas (§3.11). No tiene permiso propio que registrar en la matriz: leer una fianza es ver
// la cuenta del contrato (factura.ver) y moverla es fianza.mover, que §2 ya deja en gerente y
// administrador. El trabajo diario solo levanta el aviso de §3.16; la devolución y la liquidación lo
// resuelven ellas mismas, en su propio commit.
builder.Services.AddScoped<ServicioFianzas>();
builder.Services.AddScoped<TrabajoFianzas>();

// La vista única de cuentas y cobros (F3-10, §3.13): facturas, proformas y fianzas en una sola lista
// con sus filtros y los totales por moneda. Solo lectura; el cobro se sigue registrando por
// POST /api/v1/cobros.
builder.Services.AddScoped<ServicioCuentas>();

// F5-01: informes y dashboard (§3.15). Se calculan sobre las filas de la vista única y no con una
// segunda fórmula de dinero, que es lo que hace que el informe cuadre con la tabla. Solo lectura.
builder.Services.AddScoped<ServicioInformes>();

// F5-02: exportación del informe a Excel y CSV (§3.15). Sale de las mismas filas que el resumen, así
// que es de solo lectura y no trae permiso propio: el del endpoint es informe.ver.
builder.Services.AddScoped<ServicioExportacionInformes>();

// F4-02: el envío de documentos por correo (§3.14). Las opciones son singleton porque son la
// configuración del despliegue y no cambian mientras corre el proceso; la clave sale de ahí, de
// variables de entorno o de user-secrets, y no hay ninguna columna donde escribirla.
// IServicioCorreo va detrás de una interfaz para que las pruebas la sustituyan por un doble: el
// criterio de aceptación de la tarea es qué pasa cuando el servidor de correo falla, y eso no se
// puede probar esperando al servidor de verdad.
builder.Services.AddSingleton(OpcionesCorreo.Desde(builder.Configuration));
builder.Services.AddSingleton<IServicioCorreo, ServicioCorreoMailKit>();
builder.Services.AddScoped<ServicioPlantillasMensaje>();
// Va después de ServicioFacturas, ServicioCobros, ServicioCotizaciones, ServicioCuentas y
// ServicioPlantillas porque el envío compone el PDF del documento con los cinco generadores de
// F4-01 en lugar de inventar un camino nuevo.
builder.Services.AddScoped<ServicioEnvios>();

// F4-03: el envío masivo (§3.14). La cola va detrás de una interfaz por el mismo motivo que
// IServicioCorreo y con el mismo motivo todavía más fuerte: en las pruebas no hay servidor de jobs,
// y «qué se encoló» es justo lo que hay que comprobar. En producción la encola Hangfire con un
// trabajo por envío; no es un job recurrente y por eso no va en ProgramacionJobs.
//
// Se resuelve por fábrica y no con `AddScoped<ColaEnviosHangfire>()` porque `AddHangfire` solo se
// registra con el servidor activo: sin esa comprobación, validar el contenedor en desarrollo
// reventaría al arrancar cualquier instancia sin planificador.
builder.Services.AddScoped<IColaEnvios>(servicios => servicios.GetService<IBackgroundJobClient>() is { } cliente
    ? new ColaEnviosHangfire(cliente)
    : new ColaEnviosSinServidor());
builder.Services.AddScoped<ServicioEnviosMasivos>();
builder.Services.AddScoped<TrabajoEnvio>();
builder.Services.AddScoped<TrabajoBarridoEnvios>();

// SEC-023: aviso de IP nueva al administrador (alerta + correo por la cola). Misma fábrica que
// IColaEnvios, pero sin servidor de jobs no hay cola y no se falla: el login no puede depender de ella.
builder.Services.AddScoped<IColaAvisosAcceso>(servicios => servicios.GetService<IBackgroundJobClient>() is { } cliente
    ? new ColaAvisosAccesoHangfire(cliente)
    : new ColaAvisosAccesoSinServidor());
builder.Services.AddScoped<ServicioAvisosAcceso>();
builder.Services.AddScoped<TrabajoAvisoIpNueva>();

// F4-05: los avisos de pago (§3.14, D12). El trabajo va detrás del servicio por el mismo motivo que
// los demás: para que las pruebas puedan ejercitar el barrido sin pasar por el planificador.
builder.Services.AddScoped<ServicioRecordatorios>();
builder.Services.AddScoped<TrabajoRecordatorios>();

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

// SEC-014. Primero de todo, para que cada cabecera de seguridad salga también en los errores y en
// las redirecciones: se fijan al empezar a escribir la respuesta.
// CSP: sin 'unsafe-inline' en script-src (el script del tema es public/tema-inicial.js). Los estilos
// en línea de React son atributos style="..." y no elementos <style>, por eso solo style-src-attr los
// permite. data: en img-src y font-src porque Vite incrusta como data: los recursos pequeños.
const string PoliticaContenido =
    "default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; "
    + "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; "
    + "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
app.Use((http, siguiente) =>
{
    http.Response.OnStarting(() =>
    {
        var cabeceras = http.Response.Headers;
        cabeceras["X-Content-Type-Options"] = "nosniff";
        cabeceras["Referrer-Policy"] = "no-referrer";
        cabeceras["X-Frame-Options"] = "DENY";
        cabeceras["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        cabeceras["Content-Security-Policy"] = PoliticaContenido;
        return Task.CompletedTask;
    });

    return siguiente(http);
});

// SEC-014: la IP y el esquema reales del cliente solo se leen de X-Forwarded-* si la conexión viene
// de un proxy de Seguridad__ProxiesConfianza. Sin ninguno configurado no se registra el middleware y
// esas cabeceras se ignoran, porque las puede mandar cualquiera. Va antes de todo lo que usa la IP
// (bloqueos, limitador, auditoría): ResolutorIpPeticion lee ya la RemoteIpAddress resuelta.
// ForwardLimit = 1: se toma la última entrada, la que añadió el proxy de confianza; lo que el cliente
// escribió antes en la cadena no cuenta.
if (opcionesSeguridad.ProxiesConfianza.Count > 0 || opcionesSeguridad.RedesProxiesConfianza.Count > 0)
{
    var reenvio = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    // Por defecto confía en loopback; hay que vaciarlo para que mande solo lo configurado.
    reenvio.KnownProxies.Clear();
    reenvio.KnownIPNetworks.Clear();
    foreach (var proxy in opcionesSeguridad.ProxiesConfianza)
    {
        reenvio.KnownProxies.Add(proxy);
    }

    foreach (var red in opcionesSeguridad.RedesProxiesConfianza)
    {
        reenvio.KnownIPNetworks.Add(red);
    }

    app.UseForwardedHeaders(reenvio);
}

// SEC-014: manejador global de excepciones, en todos los entornos. Va dentro de la página de
// excepciones de desarrollo que añade WebApplication, así que esa nunca llega a ver la excepción y
// la respuesta es la misma sin traza en cualquier entorno. El detalle queda en el log.
app.UseExceptionHandler(manejador => manejador.Run(async http =>
{
    var excepcion = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (excepcion is not null)
    {
        Log.Error(excepcion, "Excepción no controlada en {Metodo} {Ruta}", http.Request.Method, http.Request.Path);
    }

    http.Response.Clear();
    var problema = new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Error interno",
        Detail = "Se produjo un error inesperado. Si se repite, indica el código de referencia al soporte.",
    };
    problema.Extensions["codigoReferencia"] = Problemas.ErrorInterno;
    http.Response.StatusCode = StatusCodes.Status500InternalServerError;
    http.Response.ContentType = "application/problem+json";
    await http.Response.WriteAsJsonAsync(problema, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
}));

// SEC-014: HSTS y redirección a HTTPS fuera de desarrollo. En desarrollo y en los tests de
// integración (HTTP) no se aplican. Detrás de un proxy la redirección depende de X-Forwarded-Proto,
// que solo se lee de los proxies de confianza (arriba).
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

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
// F6-01: el token de agente se resuelve antes de validar la sesión de cookie, porque fija el mismo
// contexto de sesión. Si no viene cabecera, no hace nada.
app.UseMiddleware<AutenticacionAgenteMiddleware>();
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