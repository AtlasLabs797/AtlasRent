# Documentación técnica — Atlas Rent

> Estado inicial: diseño previsto. Cada sección se actualiza al implementar (al cerrar cada tarea, `docs/PROCEDIMIENTOS.md` §2). Lo funcional está en `ESPECIFICACION.md`; aquí solo el cómo. Atlas Rent es un proyecto independiente: todo se construye y se prueba en este repositorio.

## 1. Arquitectura
Navegador → React SPA → API REST ASP.NET Core → EF Core → PostgreSQL.

```
Navegador ──HTTPS 443──▶ AtlasRent.Api (servicio Windows, Kestrel; Caddy opcional delante)
                          ├─ /api/v1/*            API REST
                          ├─ /webhooks/whatsapp   (cuando haya proveedor)
                          ├─ /*                   frontend estático (wwwroot)
                          └─ Hangfire server      jobs programados y colas
                                  │
                          PostgreSQL ≥16 (localhost; RLS; BD "atlas_rent")
                                  │
                          AtlasRent.Watchdog (servicio Windows .NET 10)
                          └─ operaciones críticas: restauración, actualización con copia previa y recuperación
Salidas: SMTP (Google) · ExchangeRate-API · Google Drive (backups cifrados) · GitHub Releases (actualizaciones) · proveedor WhatsApp
```

## 2. Versiones (consultadas en npm y NuGet el 14/10/2026)
| Componente | Versión |
|---|---|
| .NET SDK | 10.0.302 (`global.json`, `rollForward: latestFeature`) |
| ASP.NET Core / EF Core | 10.0 / 10.0.13 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| EFCore.NamingConventions | 10.0.1 |
| Hangfire.AspNetCore / Hangfire.PostgreSql | 1.8.25 / 1.21.1 |
| Newtonsoft.Json | 13.0.4 — añadido en F1-12: Hangfire.Core 1.8.25 arrastra 11.0.1, con CVE-2024-21907 (alta). Se fija a la versión parcheada para que el `dotnet list package --vulnerable` de la CI siga limpio |
| Serilog.AspNetCore | 10.0.0 |
| MailKit / MimeKit | 4.18.1 |
| ClosedXML | 0.105.1 |
| QuestPDF | 2026.9.1 — **verificada en F2-02**: `LicenseType.Community` es gratuita solo si la facturación bruta anual consolidada de la organización es **< 1 M USD**. Organismos públicos y empresas cotizadas **no** pueden usarla sea cual sea su facturación; si se deja de cumplir hay 90 días para comprar licencia o dejar de usar la librería, sin retroactividad. Es *source-available*, **no** una licencia aprobada por OSI, así que `SEGURIDAD.md` §A.4 no puede asumir MIT. Pendiente de confirmar los datos de la empresa antes de producción |
| BCrypt.Net-Next | 4.2.1 (12 rondas) — subido desde 4.2.0 en F1-04: es la última estable (5.0.0 es prerelease) |
| Otp.NET | 1.4.1 (TOTP; el QR se dibuja en el frontend) |
| PostgreSQL | ≥16 (desarrollo: imagen 16; producción: instancia existente ≥16 o 18 si se instala nueva) |
| Node.js / npm | 24.15.0 / 11.12.1 |
| React / React DOM | 19.3.1 |
| TypeScript | 6.0.3 (typescript-eslint aún no admite TypeScript 7) |
| Vite / @vitejs/plugin-react | 8.3.4 / 6.1.2 |
| React Router | 8.4.0 |
| Zustand | 5.0.15 |
| TanStack Query / Virtual | 5.104.2 / 3.14.19 |
| Axios | 1.20.0 |
| React Hook Form | 7.90.0 |
| Recharts | 3.10.1 |
| Lucide React | 1.57.0 |
| qrcode | 1.5.4 |
| @fontsource/geist-sans / @fontsource/geist-mono | 5.3.0 / 5.3.0 |
| ESLint / typescript-eslint | 10.12.0 / 8.71.2 |
| xunit.v3 / AwesomeAssertions / Testcontainers.PostgreSql | 4.0.2 / 9.6.0 / 4.16.0 |
| Playwright | 1.64.1 |

AwesomeAssertions sustituye a FluentAssertions porque esta pasó a licencia comercial desde la 8 (AwesomeAssertions es su continuación con licencia Apache 2.0 y la misma API).

En F1-01 estas versiones se fijan en `global.json`, los `.csproj` y `package-lock.json`. Cualquier cambio posterior se propone y se anota aquí.

## 3. Estructura
Raíz del repositorio (la CI ejecuta `dotnet restore/build/test` y `setup-dotnet` desde aquí, así que la solución y `global.json` van en la raíz):
```
global.json                 SDK 10.0.302 + `test.runner = Microsoft.Testing.Platform`
AtlasRent.slnx              solución: Api, Watchdog, Tests
Directory.Build.props       net10.0, nullable, warnings como errores, packages.lock.json
Directory.Packages.props    gestión centralizada de versiones (DOC_TECNICA §2)
```
```
backend/AtlasRent.Api/
  Program.cs        pipeline (orden en §7)
  Controllers/      finos; delegan en Services
  Services/         <Dominio>Service: reglas de negocio, transacciones
  DTOs/             contratos de entrada/salida
  Data/             AtlasRentDbContext, configuraciones EF, consultas SQL específicas
  Migrations/       EF + migrationBuilder.Sql para exclusiones, triggers, RLS, permisos
  Middleware/       seguridad, bloqueo de IP, CSRF, auditoría, validación
  Jobs/             Hangfire
  Authorization/    roles, permisos por usuario, políticas ([Accion("factura.emitir")])
backend/AtlasRent.Watchdog/
backend/AtlasRent.Tests/
frontend/src/       pages (lazy), components, design-system (tokens y componentes del DS), stores (Zustand), api (Axios + TanStack Query)
scripts/            PowerShell: install, update, start, uninstall
db/scripts/         SQL de roles y auditoría
```

**Pruebas en .NET 10:** `xunit.v3` corre sobre Microsoft.Testing.Platform. El opt-in va en la sección `test` de `global.json`; con el target `VSTest` el SDK 10 da error. Consecuencias: no se usan `Microsoft.NET.Test.Sdk` ni `xunit.runner.visualstudio`, y los tests pasan `TestContext.Current.CancellationToken` (el analizador `xUnit1051` es error por `TreatWarningsAsErrors`).

**Npgsql 10:** ya no expone la extensión `AddNpgsqlDataSource`; el `NpgsqlDataSource` se registra a mano con `NpgsqlDataSourceBuilder`.

**Health checks:** `/health/live` solo comprueba que el proceso vive (tag `vivo`); `/health` añade la comprobación de PostgreSQL con `SELECT 1`. Ambos responden `text/plain`, que es el escritor por defecto de `MapHealthChecks`.
Dominios: `Auth`, `Usuarios`, `Configuracion`, `Clientes`, `Inmuebles`, `Cotizaciones`, `Reservas`, `Contratos`, `Facturacion`, `Comprobantes`, `Cobros`, `Fianzas`, `TipoCambio`, `Documentos`, `Mensajeria`, `Informes`, `Alertas`, `Aprobaciones`, `Importacion`, `Backups`.

### 3.1 Frontend: design system y temas
- Los tokens y componentes vienen de `Atlas Rent Design System/` y se integran en `frontend/src/design-system/` como indica `docs/DESIGN.md` §1.
- Tema: `<html data-theme="light|dark">`. Un script en línea en `index.html` lee la preferencia guardada (`light` / `dark` / `system`, por defecto `system`) y resuelve `system` con `matchMedia('(prefers-color-scheme: dark)')` antes del primer pintado, para que no haya parpadeo. Un store de Zustand expone la preferencia y escucha los cambios del sistema.
- Implementado en F1-13: la clave de la preferencia en `localStorage` es `atlas-rent.tema` con valores `claro` / `oscuro` / `sistema`, y el script en línea y el store deben mantener la misma clave (lo comprueba `frontend/tests/tema.test.ts`). El DS solo define `[data-theme="dark"]`: los tokens claros viven en `:root`, así que un contenedor anidado con `data-theme="light"` **no** restaura el tema claro con la app en oscuro.
- La cookie de CSRF que lee el cliente HTTP es `atlas-rent-csrf`; el backend debe fijarla con ese nombre en F1-04.

### 3.2 Navegación y permisos en la interfaz
- `GET /api/v1/auth/yo` devuelve `permisosEfectivos`: el techo del rol recortado por los permisos por usuario, resuelto por `IPolicyService`. En ese endpoint se leen de la política ya cargada por el filtro de autorización, así que **no añade consultas**; en `MfaController` y `EmergenciaController` sí hay una, porque al ir `[AllowAnonymous]` el filtro no corre.
- `frontend/src/permisos.ts` copia literal `Acciones.cs`. Es una copia a propósito: si el backend renombra una acción, el menú deja de mostrar el módulo en vez de dejar de protegerlo.
- El menú se filtra por esos permisos, y los módulos sin pantalla quedan declarados **sin ruta enlazada** (`TODO(A#)`): un enlace que da 404 es peor que una entrada ausente.
- El backend sigue siendo el que autoriza (ESPECIFICACION §2: "Todo se valida en backend. Ocultar un botón no es protección"). Filtrar la interfaz es para no ofrecer acciones que siempre van a recibir un 403.
- `vite.config.ts` tiene un proxy de desarrollo `/api` → `127.0.0.1:5100`, necesario para ver los estados de sesión con Vite.
- **Cómo llega el build a `wwwroot`:** `npm run build` escribe en `frontend/dist`, y el target `AtlasRentPublicarFrontend` de `AtlasRent.Api.csproj` lo copia a `wwwroot/` en `dotnet publish`. Va en `publish` y no en `build` a propósito: compilar en desarrollo y ejecutar los tests de integración no debe depender de que el frontend esté construido. Si se publica sin haberlo construido, el target avisa por consola y el paquete sale **sin interfaz**, no con una interfaz a medias.

## 4. Base de datos
### 4.1 Roles (credenciales separadas por función)
| Rol | Uso | Permisos |
|---|---|---|
| `rent_owner` | propietario de esquemas y tablas | NOLOGIN |
| `rent_migrador` | migraciones / instalador | DDL, miembro de `rent_owner` |
| `rent_app` | Api y Watchdog | DML en tablas de negocio; solo INSERT/SELECT en `auditoria`; sujeto a RLS |
| `rent_backup` | backup | solo lectura |

Si la instancia PostgreSQL la usan otras aplicaciones: base de datos y roles propios; ningún rol de Atlas Rent accede a otras bases ni al revés.

### 4.2 Convenciones
- PK `uuid`; número visible aparte. Nombres `snake_case` (EFCore.NamingConventions).
- Dinero `numeric(18,2)`, cambio `numeric(18,6)`, fechas de negocio `date`, marcas `timestamptz`.
- Comunes: `origen`, `created_at/by`, `updated_at/by`; concurrencia optimista con la columna de sistema `xmin` (`UseXminAsConcurrencyToken` / `IsRowVersion` en Npgsql); conflicto → 409 con el estado actual.
- Económicos: estado anulado, nunca borrado. Resto: `deleted_at`, `deleted_by`.

### 4.3 Piezas críticas en SQL
- **Exclusión de rangos:** `EXCLUDE USING gist (unidad_id WITH =, daterange(fecha_inicio, fecha_fin, '[)') WITH &&) WHERE (bloquea)` sobre `ocupacion_rango` (`btree_gist`).
- **Medio día:** `ocupacion_dia (unidad_id, fecha, tipo int)` con `tipo` 1 = `medio_dia`, 2 = `dia_completo`; trigger que serializa por unidad con `SELECT ... FROM unidad WHERE id = NEW.unidad_id FOR UPDATE`, rechaza si hay un rango bloqueando esa fecha y aplica la capacidad de 2 medios días o 1 día completo.
- **Capas de bloqueo (decidido en F2-01):** la disponibilidad no se guarda en `contrato` ni en `reserva`, sino en dos tablas que los escriben esos servicios en su propia transacción. Motivo: una restricción de exclusión no puede abarcar dos tablas, así que con una por tabla un contrato y una reserva mensual del mismo rango se solaparían sin que nada lo detecte, y la especificación (§3.4) exige comprobar la disponibilidad en la base sin carreras. `ocupacion_rango` es el bloqueo por rango (contrato, reserva mensual) y `ocupacion_dia` el bloqueo por día suelto (reserva corta, mantenimiento); un trigger en cada tabla comprueba el otro lado, para que un rango no tape días reservados ni al revés. Liberar es `bloquea = false`, nunca borrar.
- **Contador de serie:** `UPDATE serie SET siguiente = siguiente + 1 WHERE id = @id RETURNING siguiente - 1`; `UNIQUE (serie_id, numero)`.
- **Comprobante:** `UPDATE secuencia_comprobante SET siguiente = siguiente + 1 WHERE serie_id = @id AND siguiente <= rango_hasta AND vence >= current_date RETURNING siguiente - 1`; sin fila → excepción "Comprobantes agotados o vencidos" y rollback completo.
- **Periódica:** índice único parcial `(contrato_id, periodo, concepto) WHERE estado <> 'anulada'`.
- **Auditoría:** append-only (`db/scripts/03_auditoria_append_only.sql`) + **firma de registros** (hash encadenado SHA-256 por fila) y job diario de verificación de integridad.
- **RLS:** activado en `documento_inmueble` (solo gerente/admin) y `auditoria` (lectura solo admin/gerente), con variable de sesión (`set_config('app.usuario_id', …, true)` y `app.rol`) fijada por la API al abrir cada transacción mediante un interceptor de EF Core.
- **Número de cliente:** `SEQUENCE` iniciada en `max(Factusol)+1`.
- **Índice ciego de documento:** `documento_hmac` = HMAC-SHA256(documento normalizado); índice único.

**Firmas de auditoría (decidido en F1-03).** La posición en la cadena es la secuencia `auditoria_secuencia_seq`, que asigna la **aplicación** con `nextval` bajo `pg_advisory_xact_lock`, y no una identidad: EF siempre añade `RETURNING` a las columnas store-generated y PostgreSQL filtra ese `RETURNING` con la política SELECT, de modo que con una identidad un empleado no podría registrar su propia auditoría. Por el mismo motivo `auditoria` queda fuera del token `xmin` (es append-only; `xmin` sí se aplica al resto de entidades). Firma: `SHA-256(firma_anterior || campos unidos con 0x1F)`, con `created_at` a 6 decimales y el `jsonb` ya normalizado por PostgreSQL, para que lo firmado sea lo almacenado. La firma anterior se lee de `auditoria_ultima_firma()` (`SECURITY DEFINER`), porque RLS no deja leer auditoría a un empleado y la cadena no puede romperse por el rol de quien muta. El interceptor que fija `app.usuario_id` y `app.rol` es un `DbTransactionInterceptor`, y sus parámetros se nombran con `@` (con `$1` EF rompe el enlace y Npgsql devuelve 08P01).

**Avisos heredados de F1-03:**
- `02_basedatos.sql` declara permisos por defecto `FOR ROLE rent_owner`, pero las tablas las crea `rent_migrador`: **cada migración nueva debe conceder explícitamente sus permisos a `rent_app`**. Resuelto en F1-12, que además añadió los permisos por defecto correctos (`FOR ROLE rent_migrador`) para que la siguiente migración no tenga que acordarse.
- El job diario de verificación de integridad (DOC_TECNICA §8) debe correr con `app.rol = admin` o `gerente` (o como `rent_migrador`); con otro rol, RLS le ocultará filas y la verificación daría un falso OK.
- **`rent_backup` necesita `BYPASSRLS`** (`db/scripts/01_roles.sql`, solo un superusuario puede concederlo). `pg_dump` extrae con `COPY … TO STDOUT` y PostgreSQL rechaza esa sentencia sobre una tabla con RLS activada si el rol no la ignora (SQLSTATE 55000). Como `auditoria` y `documento_inmueble` la tienen activada, **sin esa línea el volcado falla**. Ninguna política RLS lo resuelve: el bloqueo es de la sentencia `COPY`.
  - El fallo peligroso es el otro: si alguien degrada ese error para que el backup "pase", el volcado sale **vacío y sin error**. Por eso el servicio de backup comprueba `TieneFilas` y **verifica descifrando el archivo del disco** antes de rotar.
  - Riesgo asumido: un `rent_backup` comprometido lee todo, incluidas las tablas con RLS. Es inherente a poder respaldar. Se compensa con que el backup va cifrado y con que ese rol no sirve para operar la aplicación. **Al restaurar, usa un superusuario, nunca `rent_backup`.**
- `UseXminAsConcurrencyToken()` ya no existe en Npgsql EF 10: se usa una propiedad `uint` con `IsRowVersion()`, y hay que quitar a mano de la migración la columna `xmin` que EF sí genera.

## 5. Lógica de dominio
- **Tipos base** (`backend/AtlasRent.Api/Dominio/`): `Dinero` (decimal + `Moneda`), `Redondeo`, `Prorrateo`, `TiempoNegocio`. `Dinero` redondea al construirse y **no** convierte entre monedas: mezclar USD y DOP lanza `MonedaDistintaException` (D7). El redondeo del sistema es half-up explícito (`MidpointRounding.AwayFromZero`) porque el de `decimal` en .NET es half-even y `0,005` se quedaría en `0,00`.
- **Saldo de factura** = total − Σ retenciones − Σ asignaciones vigentes (moneda de la factura) − Σ ajustes de diferencia de cambio. Pagada / Parcial / Pendiente derivados. Vencida = saldo > 0 y hoy (Santo Domingo) > vencimiento.
- **Prorrateo:** `Math.Round(renta / díasDelMes × díasFacturables, 2, MidpointRounding.AwayFromZero)`, días inclusivos.
- **Emisión (una transacción):** validar → número de serie → comprobante (si fiscal) → congelar `cambio_informe` → Emitida → auditoría → outbox.
- **Cobro:** `cobro (moneda_recibida, importe_recibido, cambio_aplicado)`; `cobro_asignacion (destino, importe_moneda_destino, importe_moneda_recibida, estado)`. Propuesta FIFO por vencimiento dentro del contrato. Excedente → `saldo_favor`.
- **Pago en otra moneda:** equivalente = importe recibido convertido con el cambio aplicado, redondeado a 2; tolerancia configurable por moneda; `ajuste_diferencia_cambio` auditado.
- **Anular factura (una transacción):** factura y comprobante anulados; sus `cobro_asignacion` anuladas; cobros sin asignaciones vigentes → anulados; auditoría con la lista de afectados.
- **Anticipo:** campos de nota en `reserva`; ninguna consulta económica los lee.

## 6. API
- REST `/api/v1`, JSON, errores `ProblemDetails` con código de referencia.
- Listas paginadas: `{ data, total, page, pageSize, totalPages }`; ordenación `?sortBy=campo&sortDir=asc|desc`.
- Mutaciones sensibles aceptan `Idempotency-Key`.
- Todo endpoint con `[Accion("...")]`; política global de denegar sin atributo.
- Frontend: TanStack Query para lecturas/caché/invalidación; Axios con `withCredentials`, cabecera `X-CSRF-Token` e interceptor que renueva la sesión una vez ante 401 y, si falla, lleva al login.
- Tabla de endpoints por módulo: se añade al implementarlos.

## 7. Seguridad técnica (detalle y estado en SEGURIDAD.md)
- **Base:** JWT de acceso corto en cookie HttpOnly + token de refresco rotatorio, CSRF double-submit, BCrypt (12 rondas), MFA/TOTP, rate limiting (`Microsoft.AspNetCore.RateLimiting`), cabeceras de seguridad y CSP, HSTS, auditoría firmada, registro de eventos de seguridad, control de sesiones y tokens.
- **Añadido para Atlas Rent (expuesta a internet):**
  - Validación de sesión contra BD en cada petición (revocación inmediata), caché ≤ 30 s.
  - Bloqueo de cuenta 5 → 30 min.
  - `IpBanMiddleware`: tabla `bloqueo_ip`, escalado 1 h → 24 h → indefinido, lista blanca, desbloqueo admin.
  - Aviso al admin de entrada desde IP nueva.
  - Cifrado AES-256-GCM del documento de identidad con clave fuera de la BD.
  - Comprobación de contraseñas contra HaveIBeenPwned (k-anonymity, llamada HTTP sin paquete adicional).
- **Orden del pipeline:** ForwardedHeaders (solo proxies de confianza, p. ej. Caddy) → HSTS/HTTPS → cabeceras → IpBan → RateLimiter → autenticación → sesión en BD → CSRF → autorización → auditoría.
- **Implementado en F1-04** (DOC_TECNICA §7 y `docs/SEGURIDAD.md` SEC-001, SEC-003):
  - Cookies: `atlas-rent-at` (acceso, HttpOnly, Secure, SameSite=Strict, ruta `/`, 15 min), `atlas-rent-rt` (refresco, HttpOnly, Secure, Strict, ruta `/api/v1/auth`, 12 h) y `atlas-rent-csrf` (double-submit, **sin** HttpOnly, rotada en cada refresco). El frontend ya usa la cabecera `X-CSRF-Token`.
  - En BD solo se guarda el **SHA-256 del token de refresco**, nunca el token. La rotación marca la fila anterior como revocada con motivo `rotacion`, y esa marca es la que permite detectar la reutilización y revocar la familia completa.
  - **Rotar el token no reinicia el reloj de inactividad ni el máximo absoluto**: refrescar no es actividad del usuario. Sin esto, un cliente que refresca en segundo plano mantendría la sesión viva para siempre y las reglas de 30 min y 12 h no se cumplirían nunca.
  - El CSRF es obligatorio en todo POST/PUT/PATCH/DELETE de controlador; solo el login se salta (`[SaltarCsrf]`), porque en el primer acceso todavía no hay cookie. El refresco **sí** lo exige.
  - `ContextoSesion` es **scoped**, no singleton: con singleton, el rol de una petición pisaría al de otra y RLS leería el valor equivocado.
  - El login no distingue entre usuario inexistente y contraseña incorrecta (mismo mensaje y mismo código de referencia) y ejecuta un BCrypt de comparación en el caso "usuario inexistente" para que el tiempo de respuesta no revele qué nombres existen.
  - La autenticación y la validación de sesión se resuelven en un mismo middleware. Los `ProblemDetails` se construyen a mano en `Problemas.cs`; `AddProblemDetails()` no se usa.
  - Clave en `Seguridad__JwtClave`; las rondas de BCrypt en `Seguridad__BcryptRondas` (por defecto 12).
  - **Pendiente de decidir (TODO A#):** si HaveIBeenPwned no responde, hoy la operación continúa y solo se registra un aviso. Hay que decidir si debe bloquear. La comprobación no se invoca desde ningún endpoint todavía, porque "cambiar contraseña" y "activar cuenta" son de F1-07.
- **Implementado en F1-05** (límites y bloqueos):
  - `Security:Limites:*`: global **300/min** (por usuario si la petición está autenticada, por IP si es anónima), login **20/min por IP**, bloqueo de IP con **10 fallos en 15 min → 1 h → 24 h → indefinido**, y bloqueo de cuenta **5 fallos → 30 min**. Todos configurables; un `0` se trata como `1` para que la API nunca quede sin responder.
  - **Orden real del pipeline, que se aparta del ideal de la lista de arriba:** `IpBanMiddleware` → `UseAuthentication` → `UseRateLimiter` → validación de sesión → CSRF. El limitador va **después** de la autenticación porque su clave global es el usuario autenticado; lo que protege de la fuerza bruta, el bloqueo de IP, sigue siendo lo primero. Los contadores de peticiones viven **en memoria de la instancia**: con una sola instancia (Windows Server, un servicio) es lo correcto, pero con dos o más habría que mover el rate limiting a almacenamiento compartido. El bloqueo de IP está en la base de datos y sí sigue siendo global.
  - El límite de login es **solo por IP**: `docs/SEGURIDAD.md` A.2 pide "por IP y por usuario", pero la clave sale del cuerpo de la petición y `PartitionedRateLimiter.Create` solo admite particiones síncronas, y leer el cuerpo de forma síncrona está prohibido en Kestrel. El control por nombre de acceso ya está cubierto, y mejor, por el bloqueo de cuenta.
  - `IpBanMiddleware` es **fail-open** si la base de datos no responde, para que `/health/live` sigaindicando que el proceso vive. Toda ruta real necesita la base de datos.
  - Los intentos fallidos se registran en `intento_login` con su IP; la auditoría firmada solo recoge los hechos relevantes: `auth.bloqueo_cuenta`, `seg.bloqueo_ip`, `seg.desbloqueo_ip` y `auth.ip_nueva`.
  - **Sin endpoint de desbloqueo de IP todavía**: la operación de negocio (`ServicioBloqueos.DesbloquearIpAsync`) existe y está probada, pero exponerla sin `IPPolicyService` (F1-07) dejaría un endpoint de administración abierto.
- **Implementado en F1-07** (autorización):
  - `backend/AtlasRent.Api/Authorization/`: `Acciones`, `MatrizPermisos`, `AccionAttribute`, `IPolicyService`, `PolicyService`, `PermisoDenegadoException`, `FiltroAccionGlobal`.
  - La matriz de `docs/ESPECIFICACION.md` §2 vive **sembrada como datos** en `permiso_rol`, no como un `switch` en el código. Se prueba contra la base de datos real (19 acciones × 3 roles) comprobando si cada permiso está **dentro del techo** del rol.
  - `usuario_permiso` solo guarda lo que difiere del techo: activado dentro del techo, o desactivado aunque el rol lo traiga. Nunca por encima (D17).
  - **Denegar por omisión**: un endpoint de controlador sin `[Accion]` se rechaza. Es un filtro MVC, así que los endpoints mínimos de `/health` quedan fuera.
  - Denegación → **403** con código de referencia y auditoría firmada con resultado `denegado` (usuario, acción, recurso e IP).
  - Identificadores de acción: `configuracion.editar`, `cliente.ver`, `cliente.editar`, `inmueble.documento`, `contrato.borrador`, `contrato.estado`, `factura.ver`, `factura.borrador`, `factura.emitir`, `factura.anular`, `cobro.registrar`, `fianza.mover`, `documento.enviar`, `cotizacion.enviar`, `mensajeria.masivo`, `plantilla.editar`, `informe.ver`, `auditoria.ver`, `ip.desbloquear`, `cuenta.desbloquear`. Técnicas: `sesion.ver`, `sesion.cerrar`, `mfa.obligatorio`, `seguridad.acceso_emergencia`.
  - `usuario.es_admin` (provisional de F1-06) desaparece: la migración lo vuelca a `usuario_rol`. `ServicioMfa.NecesitaMfa` y `ServicioEmergencia.EsAdmin` usan ya `IPolicyService`.
  - `app.rol` de RLS lleva el rol real del usuario, no un valor por defecto.
- **Implementado en F1-09** (cifrado y clientes):
  - `Services/Cifrado.cs`: `ServicioCifrado` (AES-256-GCM), `DocumentoIdentidad.Normalizar`, `IndiceCiegoDocumento` (HMAC-SHA256 con clave propia).
  - Formato almacenado: `v{version}:{nonceBase64}:{textoCifrado||etiquetaBase64}`. **Nonce de 12 bytes aleatorio por cifrado** (reutilizarlo con la misma clave rompe el cifrado de forma catastrófica), etiqueta de 16 bytes al final, y la versión de clave viaja dentro para poder rotarla.
  - **Normalización del documento**: mayúsculas invariantes, eliminados espacios, guiones (incluidos U+2010/2013/2014) y puntos; se conservan letras y dígitos. Motivo: un documento es una identidad, no un texto, y el índice único solo sirve si "001-1234567-8", "001 12-34567.8" y "00112345678" son el mismo cliente.
  - Clientes, contactos y prospectos con `[Accion]`, paginación de §6, `Idempotency-Key` y auditoría en la misma transacción. Número de cliente por `SEQUENCE` iniciada en `max+1`.
  - **`idempotencia` guarda el cuerpo completo de la respuesta sin cifrar** (SEC-008). Hoy no se devuelve el documento de identidad en las respuestas de alta y edición para evitarlo, pero es un rodeo.
  - `Cliente` va con la etiqueta `IEntidadTrazada` (la base de `IEntidadConfiguracion` pasa a heredarla), que es lo que permite que `GuardadoAuditado` sirva tanto a configuración como a clientes.
  - En .NET 10, `AesGcm.Decrypt` ordena los parámetros distinto que `Encrypt`: usar **argumentos con nombre** o se cruzan nonce y etiqueta en silencio.
  - **Rotación de clave no implementada:** subir `Seguridad__CifradoVersion` sin recifrar deja los datos antiguos ilegibles. `TODO(A#)`, y no subir la versión a ciegas.
  - `SEGURIDAD.md` SEC-007 cerrado: `usuario.mfa_secreto` cifrado en reposo.
- **Implementado en F1-10** (inmuebles y documentos):
  - Los documentos de propiedad se guardan **fuera del webroot** (`Almacen__Documentos`) y se sirven **por la API**. Un PDF de propiedad no es un recurso público.
  - El nombre en disco es un **GUID**, no viene de la petición; además `RutaDe()` normaliza con `Path.GetFullPath` y exige que la ruta empiece por la carpeta raíz. El nombre enviado queda solo como metadato.
  - La subida valida extensión **y** firma real `%PDF-` (5 primeros bytes). Al leer la firma para validarla hay que **volver a escribirla** antes de copiar el resto: si no, el PDF se guarda sin cabecera y queda corrupto.
  - Máximo **25 MB** (`TODO(A#)`: supuesto, SEC-004 A.4 pide un límite pero no lo fija).
  - **RLS sobre `documento_inmueble`**: solo `admin` y `gerente`. Es la **segunda** barrera, detrás del permiso de la API, y hay un test que la comprueba saltándose la API. Por eso las lecturas van en **transacción explícita** con `app.rol`: sin transacción la política vería el rol vacío y no saldría ninguna fila.
  - La unidad solo ofrece las modalidades que tiene permitidas y su tarifa se elige por **modalidad y moneda**. El estado manual (`disponible` / `mantenimiento` / `fuera de servicio`) **no** es ocupación: ocupada y reservada se calculan por fechas en fases posteriores.
  - `TODO(A#)`: listas de `TipoInmueble` y `TipoUnidad`, que la especificación no enumera.
- **Logs:** Serilog con redacción de propiedades sensibles.
- **Implementado en F1-08** (configuración):
  - CRUD bajo `/api/v1/config` con `[Accion(Acciones.ConfiguracionEditar)]` (solo admin), auditoría en la misma transacción, `Idempotency-Key` en las mutaciones y el formato de lista paginada de §6.
  - **D14 se respeta con un guard mecánico, pero su alcance es limitado**: un test (`CifrasFiscalesEnCodigoTests`) recorre el fuente de la API, descarta comentarios y cadenas y falla si aparece una cifra **en la misma línea** que un concepto fiscal. Verificado que falla ante `ItbisGeneral = 0.18m` o `RetencionPais = 0.10m`. **No** es un análisis del código: una constante con nombre innocuo (`TasaGeneral`, `General`, `ImpIndirecto`) pasa en verde. En `CalculoImpuestos.cs` sí es estricto: no puede aparecer ninguna cifra salvo `100` (divisor). "IVA" no está en la lista porque colisiona con "activar"/"desactivar". Comprobado además por búsqueda que **no hay ninguna cifra fiscal** en el código, en las migraciones ni en el frontend.
  - `CalculoImpuestos` no tiene ningún porcentaje propio: solo reparte los que le llegan de la configuración. El `100` es divisor de porcentaje, no una cifra fiscal.
  - Contador de series: `UPDATE serie SET siguiente = siguiente + 1 … RETURNING` con `UNIQUE (serie_id, numero)`. Un número emitido no se reutiliza y una factura anulada no lo libera.
  - **Las tolerancias de diferencia de cambio nacen en `null`**, no en los valores `[Supuesto]` de la especificación (1,00 USD / 50,00 DOP): una cifra fiscal no debe ser un valor por defecto en el código.
  - Los tipos de ITBIS, las retenciones, las formas de pago, las cuentas y las subempresas **no vienen sembrados**: los crea el administrador. Las series sí se siembran (1, 2, 3, 4 y 9) porque §3.7 las numera.
  - `subempresa` va sin RNC a propósito: el cifrado AES-256-GCM llega con F1-09.
  - **Prefijos de serie pendientes**: la especificación da los números pero no los prefijos, así que los pone el admin. `TODO(A#)`.
- **Implementado en F1-06** (MFA):
  - Activación en dos pasos: se devuelve la URI `otpauth://` y el MFA **solo** queda activo tras confirmar un código válido. El QR lo dibuja el frontend; el backend no genera imágenes.
  - El login correcto con MFA activo devuelve un **desafío**, no una sesión: token provisional opaco de **5 min**, de un solo uso, en cookie `atlas-rent-mfa` (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`). El código incorrecto cuenta para el bloqueo de cuenta igual que una contraseña incorrecta.
  - **10 códigos de recuperación** de un solo uso, alfabeto sin I/O/0/1 (evita confusiones al dictarlos), formato `XXXXX-XXXXX`, SHA-256 hasheados y mostrados una sola vez.
  - Acceso de emergencia: **dos administradores distintos** tienen que aprobarlo, el código vive 10 min y la sesión y la auditoría quedan marcadas como emergencia.
  - `Otp.NET` 1.4.1. RFC 6238 §5.2 (no reutilizar el mismo paso TOTP) no se aplica; queda mitigado por el token de un solo uso. `TODO(A#)`.
  - **Cerrado en F1-09:** `usuario.mfa_secreto` ya **no** está en claro; se cifra con AES-256-GCM en reposo (ver SEC-007). Antes de F1-09 era un bloqueante de despliegue: no subir MFA a producción sin el cifrado.
  - «Admin» es hoy la columna booleana `usuario.es_admin`. Los ganchos `ServicioMfa.NecesitaMfa` y `ServicioEmergencia.EsAdmin` están aislados para que F1-07 los sustituya por `IPolicyService` sin reescribir controladores ni servicios.

## 8. Jobs
Trabajos **existentes** (verificados en `Jobs/ProgramacionJobs.Todos`) y trabajos **previstos** en fases
posteriores, que todavía no están escritos:

| Job | Estado | Dónde | Frecuencia | Idempotencia |
|---|---|---|---|---|
| Backup cifrado BD + documentos → local (4) + Google Drive | F1-12 | Hangfire | diario 02:00 AST | por fecha |
| Verificación de integridad de auditoría | F1-12 | Hangfire | diario 03:00 AST | solo lectura |
| Vigilancia de que la copia del día exista | F1-12 | Hangfire | diario 04:00 AST | por fecha |
| Aviso de reserva a 7 días | F2-03 | Hangfire | diario 05:00 AST | dedupe por reserva |
| Renovaciones e incrementos de renta | F2-06 | Hangfire | diario 06:00 AST | por (contrato, bloque) **y** fecha de vencimiento |
| Fin de contrato por fecha efectiva | F2-09 | Hangfire | diario 07:00 AST | condición de fecha **y** 409 por estado |
| Avisos de operación (§3.16) | F2-10 | Hangfire | diario 08:00 AST | dedupe por (tipo, entidad) |
| Tipo de cambio | *previsto* | Hangfire | diario 08:00 AST ⚠️ | `UNIQUE (fecha, par)` |
| Recordatorios | *previsto* | Hangfire | diario 09:00 AST | por (cliente, unidad, fecha) |
| Alertas de vencimiento de factura | *previsto* (F3) | Hangfire | cada hora | dedupe por (tipo, entidad) |
| Envíos | *previsto* (F4) | Hangfire (cola) | continuo | por `envio.id` |
| Outbox | *previsto* (F3) | Hangfire | cada minuto | por evento |
| Limpieza de sesiones, intentos, temporales | *previsto* | Hangfire | diario | — |
| Comprobación de actualizaciones | *previsto* | Hangfire | diario | — |
| Restauración y actualización | F1-12 | Watchdog | bajo demanda | copia previa obligatoria |

⚠️ **Colisión de hora pendiente:** el tipo de cambio está previsto a las **08:00** y F2-10 puso ahí los
avisos de operación. Hoy no choca porque el trabajo de tipo de cambio todavía no existe (es de F3), pero
**al escribirlo hay que moverlo** o los dos se pisarán. Lo mismo con los trabajos previstos de F3 y F4:
las horas de `ProgramacionJobs.Todos` son las que manda, no las de esta tabla.

### 8.0 Reglas que siguen los trabajos de F2
- **Una transacción por contrato** en la renovación y en el fin: un contrato que falle no deja a medias los
  demás (`ServicioRenovaciones`, `ServicioFines`).
- **La colisión se trata, no se propaga**: `23505` del índice único significa "otra ejecución se adelantó",
  se cuenta como omitida y el trabajo sigue.
- **Auditoría sin usuario de sesión**: `UsuarioId = null` y `Origen = "job"`, escrita dentro de la
  transacción antes del commit. `auditoria` tiene `INSERT ... WITH CHECK (true)` con RLS, así que la fila
  con autor nulo entra.

### 8.1 Estado (F1-12)
Implementados y programados, con la tabla de arriba como fuente de verdad: **backup cifrado** (02:00), **verificación de integridad de auditoría** (03:00) y **vigilancia de la copia** (04:00), de F1-12; **aviso de reserva a 7 días** (05:00), de F2-03; **renovaciones e incrementos** (06:00), de F2-06; **fin de contrato por fecha efectiva** (07:00), de F2-09; y **avisos de operación** (08:00), de F2-10. Sigue esperando el dominio que todavía no existe: tipos de cambio (⚠️ choca con los avisos de operación a las 08:00), alertas de vencimiento de factura (F3), recordatorios, envíos, outbox, limpieza y comprobación de actualizaciones.

- **Dónde:** `AtlasRent.Api/Jobs/`. El servidor de Hangfire arranca con la API; el panel **no** se monta (`SEGURIDAD.md` A.1).
- **Cadencia:** `ProgramacionJobs` convierte la hora de `TZ_NEGOCIO` al UTC que le corresponde, porque Hangfire 1.8 (edición gratuita) planifica en UTC y no entiende el prefijo `CRON_TZ=` de los crons con zona. Las 02:00 AST son las 06:00 UTC. Si algún día la zona pasa a tener horario de verano, `CronDiarioEnUtc` falla al arrancar en vez de desplazar la copia una hora seis meses al año.
- **Almacenamiento:** base de datos propia `atlas_rent_jobs`, esquema `hangfire` dentro de ella (Hangfire.PostgreSql 1.21 usa ese nombre por defecto y crea esquema y tablas solo, con `PrepareSchemaIfNecessary`). No es un capricho de aislamiento: `pg_dump` del backup diario recorre `atlas_rent` entera y hace `LOCK TABLE` sobre todo lo que encuentra, así que con las tablas de Hangfire dentro el volcado falla con `permission denied for schema hangfire` y **el backup diario deja de hacerse**. En su propia base, ningún backup la recorre. La credencial sí es la de migraciones (`ConnectionStrings__Migraciones`, rol `rent_migrador`) con la base cambiada en código; `rent_app` no conecta ahí ni tiene migración que le conceda nada.
- **Apagado:** `Jobs__ServidorActivo=false` apaga el planificador entero. Los tests de integración lo usan para que no haya ningún servidor de jobs real.
- **Integridad de auditoría:** corre con `app.rol = admin` dentro de una transacción, porque la política `auditoria_lectura` esconde las filas a cualquier otro rol y una lectura vacía daría un falso «todo correcto» sobre una cadena manipulada. `TrabajoIntegridadAuditoria` lo documenta en el sitio donde alguien lo vaya a «simplificar».
- **A mano:** `AtlasRent.Watchdog copiar` (mismo `ServicioBackup` y misma cadena `ConnectionStrings__Backup`, idempotente por fecha).
- **Fallo → alerta (F1-12):** `ServicioBackup` no se limita a escribir en el log. Cada fallo del día deja una fila en `alerta` con la clave `backup:{fecha}`, mediante `ServicioAlertas`, que hace `INSERT … ON CONFLICT DO UPDATE` sobre el índice único de `alerta.clave`: el mismo fallo repetido suma a `repeticiones` en vez de llenar la tabla de filas idénticas. El motivo que se copia a la alerta solo se traslada si la excepción es del propio servicio; una excepción inesperada puede llevar dentro la cadena de conexión, y la alerta es texto que se lee fuera, así que ahí solo va el tipo de excepción. La bandeja de la interfaz es de F2-10; la tabla es el registro, no la pantalla.
- **La fecha de la copia va dentro del contenedor cifrado, no solo en el nombre del archivo.** El contenedor es `ARBK2`: marca, fecha y entradas. Con la fecha solo en el nombre, quien tuviera escritura en `Backup__CarpetaLocal` podía renombrar una copia vieja con la fecha de hoy; esa copia verificaría —es auténtica y descifrable—, la generación del día se saltaría y la rotación acabaría borrando las copias buenas creyendo que son viejas. Al estar la fecha en el texto cifrado, la etiqueta GCM la protege y `VerificarAsync` rechaza todo archivo cuyo nombre no case con su contenido.
- **El salto de formato no es retrocompatible.** `ARBK2` cambia la marca del contenedor, así que **las copias escritas por un binario anterior dejan de abrirse** (`Desempaquetar` lanza y `VerificarAsync` devuelve `null`). No hay copias de producción todavía, pero en cuanto las haya: al desplegar un binario nuevo hay que regenerar las copias del día, y el Watchdog no podrá restaurar hacia atrás en el formato antiguo.
- **Límite conocido de la alerta:** la alerta vive en la misma base de datos de la que depende el respaldo. Si el fallo es precisamente que **PostgreSQL está caído**, no hay dónde escribirla y el fallo sale solo por el log. Cubre los fallos del proceso —permisos del rol de volcado, volcado vacío, descifrado, escritura en disco—, no la caída de la instancia. Lo cubre cuando exista el aviso fuera de la máquina, con el sistema de alertas.
- **Vigilancia de la copia (`TrabajoVigilanciaCopia`, 04:00 AST):** cubre el único fallo del respaldo que no produce ningún error visible — que la copia **no se ejecute**. Si el proceso estaba apagado a las 02:00, o el planificador no disparó el trabajo, o alguien desactivó el servidor de jobs, no hay excepción, ni log de fallo, ni nada: simplemente no hay copia. Por eso el trabajo no pregunta si el de las 02:00 terminó (eso sería preguntarse a sí mismo), sino que abre el archivo que debería haber en disco, lo descifra y lo comprueba con el mismo `VerificarAsync` del resto del sistema. Distingue «no está» de «está pero no verifica» porque el operador actúa distinto, y registra la alerta con clave `backup-ausente:{fecha}`, distinta de `backup:{fecha}` del fallo: una dice que el respaldo falló y por qué, la otra que a las 04:00 no había copia. Son dos hechos, no el mismo contado dos veces. Va dos horas después de la copia para no vigilar una que todavía no ha tenido tiempo de hacerse.
- **`AtlasRent.Watchdog copiar` no registra alertas, a propósito.** Es un comando manual: quien lo ejecuta ve el error en la consola y en el visor de eventos. Meterlo necesitaría una conexión a la base de la aplicación en el Watchdog, que hoy solo usa `ConnectionStrings__Backup`, y esa dependencia no compensa. La copia diaria, que sí es desatendida, es la que alerta.

**Paso de superusuario, una vez por servidor** (la base la crea un superusuario porque `rent_migrador` no tiene permiso `CREATE` en ella; dentro, el esquema `hangfire` y sus tablas los crea el propio Hangfire con `PrepareSchemaIfNecessary` al arrancar la API la primera vez):

```sql
CREATE DATABASE atlas_rent_jobs OWNER rent_owner ENCODING 'UTF8' TEMPLATE template0;
\connect atlas_rent_jobs
REVOKE ALL ON DATABASE atlas_rent_jobs FROM PUBLIC;
GRANT CONNECT ON DATABASE atlas_rent_jobs TO rent_migrador;
```

## 9. Despliegue (Windows Server x64)
1. PostgreSQL: usar la instancia existente (≥16) o instalar la última estable; `listen_addresses='localhost'`, `pg_hba` solo local con `scram-sha-256`.
2. `db/scripts/01_roles.sql` y `02_basedatos.sql` como superusuario; contraseñas generadas en el momento y guardadas en el gestor de contraseñas de la empresa.
3. Paquete ZIP x64 (backend self-contained + frontend compilado + Watchdog + scripts).
4. `scripts/install.ps1`: instala en `C:\AtlasRent`, crea servicios `AtlasRent.Api` y `AtlasRent.Watchdog` con cuentas sin privilegios, variables de entorno, reglas de firewall (solo 443), migraciones con `rent_migrador`.
5. Certificado: HTTPS directo en Kestrel (win-acme / Let's Encrypt o certificado de la empresa) o Caddy delante.
6. Router: reenviar solo 443.
7. Comprobar checklist A.1 de SEGURIDAD.md.

**Actualizaciones:** paquetes publicados en GitHub Releases (, validación de integridad y firma, copia previa, recuperación ante error) mediante `scripts/update.ps1` y Watchdog.

**Si comparte servidor con otras aplicaciones:** puertos distintos o el mismo 443 con Caddy enrutando por subdominio; carpetas, servicios, roles de BD y claves separados.

## 10. Pruebas
- Backend: xUnit v3 + AwesomeAssertions; integración con Testcontainers PostgreSQL (obligatorio para exclusiones, triggers, RLS, numeración, auditoría); EF InMemory solo para lógica sin SQL específico.
- Frontend: ESLint 10, `tsc --noEmit`, Node.js Test Runner, Playwright E2E (reserva → contrato → factura → cobro en otra moneda).
- Casos obligatorios: ver ESPECIFICACION §8.
- CI (GitHub Actions): build, tests, auditoría npm y NuGet, verificación de versiones, escaneo de secretos, releases y firma de paquetes.

## 11. Operación
- Restaurar backup: runbook (FP-03).
- Admin bloqueado / sin MFA: procedimiento break-glass (F1-06).
- Rotación de la clave de cifrado de documentos (F1-09).
