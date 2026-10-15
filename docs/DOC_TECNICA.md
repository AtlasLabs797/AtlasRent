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
| Serilog.AspNetCore | 10.0.0 |
| MailKit / MimeKit | 4.18.1 |
| ClosedXML | 0.105.1 |
| QuestPDF | 2026.9.1 — **[Verificar licencia]**: Community gratuita solo con facturación bruta anual < 1 M USD |
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
- **Exclusión de rangos:** `EXCLUDE USING gist (recurso_id WITH =, periodo WITH &&) WHERE (bloquea)` en contratos y reservas mensuales (`btree_gist`).
- **Medio día:** `reserva_dia (recurso_id, fecha, mitades smallint check (mitades in (1,2)))`; trigger con `pg_advisory_xact_lock` por unidad que suma mitades del día (≤ 2) y comprueba que no haya contrato/reserva mensual en la fecha.
- **Contador de serie:** `UPDATE serie SET siguiente = siguiente + 1 WHERE id = @id RETURNING siguiente - 1`; `UNIQUE (serie_id, numero)`.
- **Comprobante:** `UPDATE secuencia_comprobante SET siguiente = siguiente + 1 WHERE serie_id = @id AND siguiente <= rango_hasta AND vence >= current_date RETURNING siguiente - 1`; sin fila → excepción "Comprobantes agotados o vencidos" y rollback completo.
- **Periódica:** índice único parcial `(contrato_id, periodo, concepto) WHERE estado <> 'anulada'`.
- **Auditoría:** append-only (`db/scripts/03_auditoria_append_only.sql`) + **firma de registros** (hash encadenado SHA-256 por fila) y job diario de verificación de integridad.
- **RLS:** activado en `documento_inmueble` (solo gerente/admin) y `auditoria` (lectura solo admin/gerente), con variable de sesión (`set_config('app.usuario_id', …, true)` y `app.rol`) fijada por la API al abrir cada transacción mediante un interceptor de EF Core.
- **Número de cliente:** `SEQUENCE` iniciada en `max(Factusol)+1`.
- **Índice ciego de documento:** `documento_hmac` = HMAC-SHA256(documento normalizado); índice único.

**Firmas de auditoría (decidido en F1-03).** La posición en la cadena es la secuencia `auditoria_secuencia_seq`, que asigna la **aplicación** con `nextval` bajo `pg_advisory_xact_lock`, y no una identidad: EF siempre añade `RETURNING` a las columnas store-generated y PostgreSQL filtra ese `RETURNING` con la política SELECT, de modo que con una identidad un empleado no podría registrar su propia auditoría. Por el mismo motivo `auditoria` queda fuera del token `xmin` (es append-only; `xmin` sí se aplica al resto de entidades). Firma: `SHA-256(firma_anterior || campos unidos con 0x1F)`, con `created_at` a 6 decimales y el `jsonb` ya normalizado por PostgreSQL, para que lo firmado sea lo almacenado. La firma anterior se lee de `auditoria_ultima_firma()` (`SECURITY DEFINER`), porque RLS no deja leer auditoría a un empleado y la cadena no puede romperse por el rol de quien muta. El interceptor que fija `app.usuario_id` y `app.rol` es un `DbTransactionInterceptor`, y sus parámetros se nombran con `@` (con `$1` EF rompe el enlace y Npgsql devuelve 08P01).

**Avisos heredados de F1-03:**
- `02_basedatos.sql` declara permisos por defecto `FOR ROLE rent_owner`, pero las tablas las crea `rent_migrador`: **cada migración nueva debe conceder explícitamente sus permisos a `rent_app`**.
- El job diario de verificación de integridad (DOC_TECNICA §8) debe correr con `app.rol = admin` o `gerente` (o como `rent_migrador`); con otro rol, RLS le ocultará filas y la verificación daría un falso OK.
- `rent_backup` no tiene política de SELECT, así que con RLS activa un `pg_dump` con ese rol saldría vacío. Resolver en F1-12.
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
  - **`idempotencia` guarda el cuerpo completo de la respuesta sin cifrar** (SEC-006). Hoy no se devuelve el documento de identidad en las respuestas de alta y edición para evitarlo, pero es un rodeo.
  - `Cliente` va con la etiqueta `IEntidadTrazada` (la base de `IEntidadConfiguracion` pasa a heredarla), que es lo que permite que `GuardadoAuditado` sirva tanto a configuración como a clientes.
  - En .NET 10, `AesGcm.Decrypt` ordena los parámetros distinto que `Encrypt`: usar **argumentos con nombre** o se cruzan nonce y etiqueta en silencio.
  - **Rotación de clave no implementada:** subir `Seguridad__CifradoVersion` sin recifrar deja los datos antiguos ilegibles. `TODO(A#)`, y no subir la versión a ciegas.
  - `SEGURIDAD.md` SEC-005 cerrado: `usuario.mfa_secreto` cifrado en reposo.
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
  - **D14 se respeta mecánicamente**: un test (`CifrasFiscalesEnCodigoTests`) recorre el fuente de la API, descarta comentarios y cadenas y falla si aparece una cifra junto a un concepto fiscal. Está verificado que **falla** al plantar una constante. "IVA" no está en la lista de conceptos porque colisiona con "activar"/"desactivar".
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
  - **Pendiente crítico:** `usuario.mfa_secreto` está **en claro**. Ver SEC-005 en `docs/SEGURIDAD.md`: no desplegar MFA en producción antes de que F1-09 cierre el cifrado.
  - «Admin» es hoy la columna booleana `usuario.es_admin`. Los ganchos `ServicioMfa.NecesitaMfa` y `ServicioEmergencia.EsAdmin` están aislados para que F1-07 los sustituya por `IPolicyService` sin reescribir controladores ni servicios.

## 8. Jobs
| Job | Dónde | Frecuencia | Idempotencia |
|---|---|---|---|
| Backup cifrado BD + documentos → local (4) + Google Drive | Hangfire | diario 02:00 AST | por fecha |
| Tipo de cambio | Hangfire | diario 08:00 AST | `UNIQUE (fecha, par)` |
| Renovaciones e incrementos | Hangfire | diario 00:15 AST | por (contrato, bloque) / (contrato, incremento) |
| Alertas | Hangfire | cada hora | dedupe por (tipo, entidad) |
| Recordatorios | Hangfire | diario 09:00 AST | por (cliente, unidad, fecha); modo manual solo prepara la lista |
| Envíos | Hangfire (cola) | continuo | por `envio.id` |
| Outbox | Hangfire | cada minuto | por evento |
| Verificación de integridad de auditoría | Hangfire | diario | solo lectura |
| Limpieza de sesiones, intentos, temporales | Hangfire | diario | — |
| Comprobación de actualizaciones | Hangfire | diario | — |
| Restauración y actualización | Watchdog | bajo demanda | copia previa obligatoria |

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
