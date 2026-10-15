# CHANGELOG — Trazabilidad

Una entrada por tarea, decisión, bug o incidencia de seguridad. Lo más reciente arriba. No se reescriben entradas pasadas; si algo estaba mal, se añade una entrada nueva que lo corrige.

Formato:
```
## [dd/mm/aaaa hh:mm] TIPO · ID-tarea · Título · (persona)
- Qué: cambios concretos (archivos/módulos).
- Por qué: motivo o referencia (spec §, D#, BUG-, SEC-, A#).
- Verificado: comandos/tests ejecutados y resultado. Qué NO se verificó.
- Migraciones: sí/no (nombre).
- Riesgo / pendiente:
- Commit/PR:
```
TIPO: FEAT · FIX · SEC · DOCS · MIGR · CONFIG · DECISION

---

## [15/10/2026 05:20] FEAT · F1-04 · Autenticación: JWT en cookie, refresco rotatorio, CSRF y sesiones revocables
- Qué:
  - Migración `20261015014910_AutenticacionYUsuarios` con `usuario` y `sesion`, permisos explícitos para `rent_app` y el aviso heredado de F1-03 aplicado (quitar a mano la columna `xmin` que EF genera).
  - Endpoints `/api/v1/auth`: `login`, `refresh`, `logout`, `yo`.
  - Cookies `atlas-rent-at` (acceso, HttpOnly, 15 min), `atlas-rent-rt` (refresco, HttpOnly, ruta `/api/v1/auth`, 12 h) y `atlas-rent-csrf` (double-submit, sin HttpOnly, rotada en cada refresco), validada con la cabecera `X-CSRF-Token`.
  - En BD solo el SHA-256 del token de refresco. La rotación marca la fila anterior como revocada con motivo `rotacion`, lo que permite detectar reutilización y revocar la familia completa.
  - `ServicioContrasenas` (BCrypt, 12 rondas por defecto) y `ServicioComplicidad` (HaveIBeenPwned por k-anonymity, solo prefijo de 5 caracteres del SHA-1, comparación de sufijos en tiempo constante, sin paquete adicional).
  - `ValidacionSesionMiddleware` valida la sesión en BD en cada petición con caché de 30 s; inactividad 30 min y máximo absoluto 12 h.
  - 18 tests nuevos en `backend/AtlasRent.Tests/Auth/`.
- Por qué: F1-04 del plan. La app está expuesta a internet, así que hace falta revocación inmediata y sesiones con caducidad real.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **83/83 en verde**; `dotnet list package --vulnerable --include-transitive` → sin vulnerabilidades. Revisado en el código que en BD se persiste solo el hash del token de refresco y que la rotación hereda familia, inactividad y caducidad absoluta.
- NO verificado: la integración con un despliegue real (`dotnet ef database update` sobre la BD de desarrollo), porque no se leen credenciales de `.env.dev`; la cobertura equivalente la da el contenedor de tests. La comprobación de HaveIBeenPwned no se invoca desde ningún endpoint: "cambiar contraseña" y "activar cuenta" son de F1-07.
- Migraciones: sí. `Migrations/20261015014910_AutenticacionYUsuarios`.
- Riesgo / pendiente:
  - **Decisión tuya pendiente:** si HaveIBeenPwned no responde, hoy la operación continúa y solo se registra un aviso (`TODO A#` en `ServicioComplicidad`). Hay que decidir si debe bloquear el alta o el cambio de contraseña.
  - BCrypt.Net-Next subido de 4.2.0 a **4.2.1** (última estable; 5.0.0 es prerelease). DOC_TECNICA §2 actualizado.
  - `ContextoSesion` pasó de singleton a **scoped**: con singleton, el rol de una petición pisaría al de otra y RLS leería el valor equivocado.
  - El snapshot de EF sigue incluyendo `xmin` en `usuario` y `sesion`; la siguiente migración generada tendrá que quitar a mano esa columna otra vez.
  - `SecurityStamp` se añadió como token de versión para invalidar sesiones al cambiar la contraseña: si algún flujo de F1-07 lo necesita, debe respetarlo.
  - Rate limiting y bloqueo de IP siguen sin implementar (F1-05), así que el login aún no está protegido contra fuerza bruta.
- Commit/PR: `feat(auth): JWT en cookie, refresco rotatorio, CSRF y validacion de sesion en BD`.

## [15/10/2026 04:50] FEAT · F1-03 · Base de datos: migración inicial, auditoría append-only firmada y RLS
- Qué:
  - `AtlasRentDbContext` (`Data/`) con `UseSnakeCaseNamingConvention`, convenciones globales (dinero `numeric(18,2)`, tipo de cambio `numeric(18,6)`, fechas de negocio `date`, marcas de tiempo en UTC `timestamptz`, PK `uuid`) y token de concurrencia optimista sobre la columna de sistema `xmin`.
  - Migración `20261015013004_MigracionInicial`: extensiones `btree_gist` y `pgcrypto`, tabla `auditoria`, secuencia `auditoria_secuencia_seq`, el SQL de `db/scripts/03_auditoria_append_only.sql` (REVOKE/GRANT para `rent_app` y triggers `auditoria_inmutable` / `trg_auditoria_no_truncate`), RLS con política de lectura por rol y la función `auditoria_ultima_firma()` (`SECURITY DEFINER`).
  - `AuditoriaService` escribe el registro firmado dentro de la transacción de la mutación: `pg_advisory_xact_lock` serializa la cadena, `created_at` a 6 decimales y `jsonb` normalizado por PostgreSQL antes de firmar, de modo que lo firmado es exactamente lo almacenado.
  - `IntegridadAuditoria` recorre la cadena y devuelve el primer registro manipulado.
  - `ContextoTransaccionInterceptor` fija `app.usuario_id` y `app.rol` por transacción.
  - Tests de integración con Testcontainers: 10 casos sobre roles, permisos, append-only e integridad.
- Por qué: F1-03 del plan. Toda mutación debe dejar auditoría con IP en la misma transacción (AGENTS.md §6), y esa auditoría no puede alterarse ni leerse por quien no tiene permiso.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **65/65 en verde** (10 de auditoría + 51 previos + 4 de arranque), con la migración aplicándose de verdad desde cero en PostgreSQL 16; `dotnet list package --vulnerable --include-transitive` → sin vulnerabilidades. Criterios de aceptación cubiertos uno a uno: `rent_app` **puede** insertar, **no** puede UPDATE, DELETE ni TRUNCATE; el trigger rechaza modificar incluso con permisos; la verificación detecta una fila alterada por debajo; y la lectura depende del rol (teoría con 3 casos).
- NO verificado: el job diario de verificación de integridad (DOC_TECNICA §8) no existe todavía; y `pg_dump` con `rent_backup` no se ha probado (ver riesgo siguiente).
- Migraciones: sí. `Migrations/20261015013004_MigracionInicial`, aplicada por el proceso en los tests de integración. Requiere ejecutar `dotnet ef database update` con `rent_migrador` en el entorno de desarrollo.
- Riesgo / pendiente:
  - **Cada migración futura debe conceder sus permisos a `rent_app`**: `02_basedatos.sql` declara permisos por defecto `FOR ROLE rent_owner`, pero las tablas las crea `rent_migrador`, así que los permisos por defecto no aplican. En esta migración selauwt el de la secuencia.
  - El job de integridad debe ejecutarse con `app.rol = admin` o `gerente` (o como `rent_migrador`): con otro rol, RLS le ocultará filas y dará una verificación falsa.
  - `rent_backup` no tiene política de SELECT, así que con RLS activa un `pg_dump` con ese rol saldría vacío. Resolver en F1-12.
  - Decisiones que la especificación no fijaba: la posición en la cadena es una secuencia asignada por la aplicación, no una identidad, porque EF siempre añade `RETURNING` a las columnas store-generated y PostgreSQL filtra `RETURNING` con la política SELECT (un empleado no podría registrar su auditoría). Por el mismo motivo `auditoria` queda fuera del token `xmin`. `UseXminAsConcurrencyToken()` ya no existe en Npgsql EF 10. El separador de la firma es 0x1F escrito como conversión `(char)0x1F` y nunca como carácter literal en el fuente.
  - **Pendiente de tu decisión**: F1-12 depende de F0-04 (carpetas de Google Drive y cliente OAuth), que sigue en curso y bloquea la parte de subida de backups.
- Commit/PR: `feat(db): migracion inicial, auditoria append-only firmada y RLS`.

## [15/10/2026 04:35] FEAT · F1-13 · Esqueleto frontend con design system y tema claro/oscuro
- Qué: `frontend/` con Vite + React 19 + TypeScript 6, y los scripts `lint`, `typecheck`, `test` y `build` que la CI invoca.
  - Design system copiado de `Atlas Rent Design System/` a `frontend/src/design-system/` (solo `styles.css`, `tokens/` y `components/`; **no** `_ds*`, `uploads/` ni `ui_kits/`), con **22** componentes portados de `.jsx` a `.tsx` usando cada `.d.ts` como contrato.
  - `Icon` reescrito con `lucide-react` manteniendo `<Icon name="…" />`, sin CDN ni recursos de red en ejecución.
  - Fuentes Geist autoalojadas con `@fontsource/geist-sans` y `@fontsource/geist-mono`; `tokens/fonts.css` deja de importar Google Fonts.
  - Tema claro/oscuro: script en línea en `index.html` que fija `data-theme` antes del primer pintado (sin parpadeo) y store de Zustand con preferencia Claro/Oscuro/Sistema y escucha de `matchMedia`. Un test comprueba que el script en línea y el store comparten clave y valores.
  - Base: React Router 8 con páginas `React.lazy` + `Suspense`, cliente Axios con `withCredentials` y `X-CSRF-Token`, textos de interfaz centralizados en español.
- Por qué: F1-13 del plan (se separó de F1-01 al pasar el proyecto a independiente). Segunda columna de la página de muestra forzada a `data-theme="dark"`: el DS solo define `[data-theme="dark"]` y un contenedor anidado con `data-theme="light"` no puede restaurar el tema claro con la app en oscuro, sin inventar tokens.
- Verificado (por el orquestador, no por el subagente): `npm ci` correcto; `npm run lint` 0; `npm run typecheck` 0; `npm test` 8/8; `npm run build` correcto y genera `frontend/dist`; `npm audit --audit-level=high` → 0 vulnerabilidades. Versiones instaladas exactamente las de DOC_TECNICA §2, sin desviaciones.
- NO verificado: **revisión visual en claro y oscuro a ojo**: no se abrió la app en un navegador, así que contraste y maquetación no están comprobados aunque lint/typecheck/build estén en verde. Tampoco probados contra la API real, ni los nombres de icono de Lucide inexistentes (el fallback a `Circle` es silencioso). El chunk principal pesa 931,70 kB (261,24 kB gzip) y avisa de superar 500 kB.
- Migraciones: no.
- Riesgo / pendiente: quedan ~28 componentes del DS por portar conforme los usen las pantallas; refresco de sesión ante 401 (DOC_TECNICA §6) pendiente de F1-04; el nombre de la cookie de CSRF `atlas-rent-csrf` es un supuesto que debe casar con el backend; `backend/**/wwwroot/` sin conectar (`MapFallbackToFile` sigue pendiente).
- Commit/PR: `feat(frontend): esqueleto Vite+React+TS, design system portado y tema claro/oscuro`.

## [15/10/2026 03:40] FEAT · F1-02 · Tipos de dominio: Dinero, redondeo, fechas de negocio y zona horaria
- Qué: `backend/AtlasRent.Api/Dominio/` con `Moneda` (USD/DOP), `Dinero`, `Redondeo`, `Prorrateo`, `TiempoNegocio` y `MonedaDistintaException`. Tests en `backend/AtlasRent.Tests/Dominio/`.
  - `Redondeo.Importe` half-up a 2 decimales, `Redondeo.TipoCambio` a 6 (`numeric(18,6)`) y `Redondeo.Porcentaje` a 4 (ITBIS y retenciones, F1-08).
  - `Dinero` redondea al construirse, de modo que ningún valor con centimas ocultas llega a la base de datos ni a una suma posterior; sumar, restar o comparar monedas distintas lanza `MonedaDistintaException` en vez de convertir (D7).
  - `Prorrateo.Mensual` aplica `renta / díasDelMes × díasFacturables` con días inclusivos y rechaza rangos imposibles.
  - `TiempoNegocio` con `DateOnly` para fechas de negocio y `America/Santo_Domingo` para "hoy", con resolución alternativa del identificador de Windows.
- Por qué: F1-02 del plan. Todo el dinero, las fechas y los permisos se escriben con test primero (AGENTS.md §6).
- Verificado: `dotnet test -c Release` → 55/55 en verde (51 nuevos de dominio + los 4 de arranque de F1-01); `dotnet build -c Release -warnaserror` sin advertencias (vía `TreatWarningsAsErrors`). Cubierto: half-up en positivo y negativo (0,005 → 0,01 y −0,005 → −0,01), el caso trampa 2,675 → 2,68, meses de 28/29/30/31 días completos y parciales, cambio de día entre UTC y Santo Domingo, vencimiento el mismo día.
- NO verificado: el consumo de estos tipos desde EF Core (conversores `numeric(18,2)`, `date` y `timestamptz`) llega en F1-03.
- Migraciones: no.
- Riesgo / pendiente: `Redondeo.Porcentaje` queda preparado para F1-08; `TiempoNegocio.Ahora()` no tiene test determinista por diseño (las funciones puras sí lo tienen) y se exercita en F1-04.
- Commit/PR: `feat(dominio): tipos de dinero, redondeo, prorrateo, fechas de negocio y zona horaria`.

## [15/10/2026 03:05] FEAT · F1-01 · Esqueleto backend (solución, Serilog, health checks, PostgreSQL)
- Qué:
  - `global.json` en la raíz (SDK 10.0.302, `rollForward: latestFeature`) **y sección `test: { runner: Microsoft.Testing.Platform }`**: el SDK 10 ya no soporta el target `VSTest`, así que `dotnet test` sin ese opt-in falla.
  - `AtlasRent.slnx` en la raíz con `backend/AtlasRent.Api`, `backend/AtlasRent.Watchdog` y `backend/AtlasRent.Tests`. La CI ejecuta `dotnet restore/build/test` en la raíz, por eso la solución y `global.json` viven ahí y no en `backend/`.
  - `Directory.Build.props` (`net10.0`, nullable, warnings como errores, `RestorePackagesWithLockFile`) y `Directory.Packages.props` (gestión centralizada de versiones de DOC_TECNICA §2). `packages.lock.json` versionados en los tres proyectos: la CI restaura con `--locked-mode` y sin ellos falla.
  - `backend/AtlasRent.Api/Program.cs`: Serilog (`ReadFrom.Configuration`, override de `Microsoft.AspNetCore`), `NpgsqlDataSource` registrado a mano, `AddControllers`, `UseSerilogRequestLogging`, `MapControllers` y `MapHealthChecks` en `/health/live` (solo vida del proceso) y `/health` (vida + PostgreSQL).
  - `backend/AtlasRent.Watchdog/Program.cs`: proceso en pie y registrándose; las operaciones críticas llegan en F1-12.
  - `backend/AtlasRent.Tests/Arranque/ArranqueApiTests.cs`: 4 tests con `WebApplicationFactory` sobre la API real.
  - `.github/workflows/ci.yml`: Node de 24.14.1 a **24.15.0** para igualarlo a DOC_TECNICA §2.
- Por qué: F1-01 del plan. Base de la que dependen F1-02 a F1-12.
- Verificado: `dotnet build -c Release -warnaserror` en verde, 0 advertencias; `dotnet test -c Release` 4/4 en verde; `dotnet list package --vulnerable --include-transitive` sin vulnerabilidades en los tres proyectos; `dotnet restore --locked-mode` correcto; `dotnet run` arranca y `/health/live` responde 200 `Healthy`; `/health` responde 200 `Healthy` con la BD de desarrollo conectada y 503 cuando no puede conectar.
- NO verificado: la CI en GitHub no se ha ejecutado (falta `push`, que `AGENTS.md` §5 prohíbe sin permiso). La conexión validada usa el superusuario del contenedor de desarrollo, cuya contraseña está versionada en `docker-compose.dev.yml`; **no** se pudo validar con `rent_app` porque su contraseña vive en `.env.dev`, que no se lee. Los endpoints usan `text/plain` (escritor por defecto de `MapHealthChecks`).
- Migraciones: no.
- Riesgo / pendiente: alinear la contraseña de `rent_app` en `.env.dev` y en los user-secrets de `AtlasRent.Api` con la del entorno; Npgsql 10 ya no expone `AddNpgsqlDataSource`, así que el `NpgsqlDataSource` se construye a mano; el frontend estático (`MapFallbackToFile`) se añade en F1-13.
- Commit/PR: `feat(backend): esqueleto de solución, Serilog, health checks y conexión a PostgreSQL`.

## [14/10/2026 23:59] DECISION · F0 · Proyecto independiente, instrucciones neutras y modo oscuro (spec v3.2)
- Qué:
  - **Sin Atlas Balance:** eliminada toda reutilización y dependencia de Atlas Balance en `docs/ESPECIFICACION.md` (v3.2; D1, D2, D20, D30, párrafo de reutilización, 3.18, §4, §5, §5.1, §7), `docs/DOC_TECNICA.md`, `docs/PLAN.md`, `docs/SEGURIDAD.md`, CI, `config/variables-entorno.example` y `docker-compose.dev.yml`. Donde había "mecanismo de Balance" queda la decisión concreta: concurrencia con `xmin`, auditoría con hash encadenado, RLS con variable de sesión fijada por interceptor de EF Core, formato de paginación, refresco de sesión en Axios, validación con `JsonUnmappedMemberHandling.Disallow`.
  - **Versiones propias** en DOC_TECNICA §2, consultadas en npm y NuGet hoy (React 19.3.1, TypeScript 6.0.3, Vite 8.3.4, EF Core 10.0.13, SDK 10.0.302…). FluentAssertions → AwesomeAssertions (FA ≥ 8 es comercial). QuestPDF: criterio de licencia explícito (Community solo con facturación < 1 M USD).
  - **Plan:** F1-01 pasa a ser el esqueleto backend sin depender de F0-05; nueva F1-13 (esqueleto frontend + design system + temas); F1-11 incluye el selector de tema; el riesgo 4 pasa a ser "seguridad construida desde cero".
  - **Instrucciones neutras:** `AGENTS.md` reescrito sin referencias a modelos ni herramientas concretas (subagentes descritos por capacidad, no por nombre). Los flujos de los comandos `/siguiente`, `/cerrar-tarea`, `/registrar-bug`, `/registrar-seguridad` y `/revision-seguridad` pasan a `docs/PROCEDIMIENTOS.md`; `.claude/commands/` quedan como atajos de una línea. `CLAUDE.md` solo importa `AGENTS.md`. Menciones a "Claude" en PLAN, BUGS, SEGURIDAD y DOC_TECNICA → "agente".
  - **Modo oscuro (D31):** `Atlas Rent Design System/tokens/themes.css` (nuevo) con paridad completa, basado en el tema oscuro de la familia Atlas Labs; tokens `--danger-solid`, `--ai-solid` y `--ai-soft-hover` para fondos con texto blanco (aplicados en `components.css`, `components-app.css`, `ApprovalCard.jsx`, `ChatPanel.jsx`); `styles.css` importa `themes.css`. Mecanismo `data-theme` en DESIGN §3.1 y DOC_TECNICA §3.1.
  - **Diseño:** `docs/DESIGN.md` con integración en Vite (fuentes con `@fontsource`, `--font-sans` admite "Geist Sans"), temas, diseño base de PDF [Supuesto A2] y pendientes actualizados; nota en el `DESIGN.md` del DS remitiendo a `docs/DESIGN.md`; `DatePicker.prompt.md` creado.
  - **Repo:** `AGENTS.md` (secciones de diseño en `docs/DESIGN.md` y skills en `C:\Users\usuario\Documents\Skills`), `README.md` en español, `.gitignore` con rutas reales (`frontend/dist/`, `backend/**/wwwroot/`, `*.tsbuildinfo`, `logs/`, `*.crt`, `*.cer`) y `.claude/settings.local.json` fuera del repo.
- Por qué: decisión del responsable (14/10/2026): Atlas Rent es un proyecto propio, varios agentes de distintos proveedores trabajarán en él y se quiere modo oscuro.
- Verificado: sin menciones a Atlas Balance fuera de entradas antiguas del CHANGELOG; copias de `ESPECIFICACION.md` idénticas; contraste WCAG de los tokens oscuros calculado; página de prueba con el CSS del DS revisada a ojo en claro y oscuro (botones, badges, tabla, KPI, alerta, toast, campo). `.env.dev` sigue ignorado. NO verificado: los componentes React del DS en oscuro (se comprueba al portarlos en F1-13); `_ds_bundle.js` y las fichas `.card.html` son artefactos generados y no se han regenerado.
- Migraciones: no.
- Riesgo / pendiente: licencia de QuestPDF según facturación de la empresa; logo real; ajustar PDF con los ejemplos de F0-06.
- Commit/PR: sin commit.

## [14/10/2026 22:45] CONFIG · F0-05 · Entorno de desarrollo inicial
- Qué: carpeta enlazada con `AtlasLabs797/AtlasRent` (rama main); `AGENTS.md` redactado (estaba vacío); PostgreSQL de desarrollo en Docker con roles `rent_*` y base `atlas_rent`; `dotnet-ef` actualizado a 10.0.13.
- Por qué: el puerto 5432 lo ocupa el PostgreSQL nativo de Atlas Balance, así que `docker-compose.dev.yml` publica en **5433** (en desarrollo usar Port=5433 en las cadenas de conexión).
- Verificado: login como `rent_app` y extensiones `btree_gist`/`pgcrypto` presentes. `03_auditoria_append_only.sql` no aplicado (la tabla llega en F1-03).
- Pendiente: el SDK instalado es 10.0.302 y la spec indica 10.0.112; en F1-01 `global.json` debe usar rollForward o igualarse a Balance.

## [14/10/2026 14:50] DECISION · F0 · Stack alineado con Atlas Balance V-03.01 (spec v3.1)
- Qué: stack, versiones, estructura de carpetas, pruebas, CI, despliegue y diseño igualados a Atlas Balance V-03.01 para copiar su código: React 19.2.8, TS 5.6.3, Vite 8.0.16, React Router 8.3.0, Zustand 5.0.14, TanStack Query 5, Axios 1.16, Recharts 3, Lucide React; ASP.NET Core 10 (SDK 10.0.112), EF Core 10.0.12, Npgsql 10.0.3, EFCore.NamingConventions, Hangfire 1.8, Serilog 10, MailKit 4.17, ClosedXML 0.105; xUnit v3, Testcontainers, Playwright, ESLint 10; PostgreSQL ≥16 con RLS; scripts PowerShell, ZIP x64, actualizaciones por GitHub Releases; backups cifrados a Google Drive.
- Eliminado: TanStack Table, Radix UI, zod, react-i18next, FluentValidation, Otp.NET/QRCoder como paquetes propios (se usa lo de Balance), Material Symbols, CSS Modules, tabla de cola del Watchdog, carpeta externa de backup (A7 resuelta con Google Drive).
- Añadido: nombre Atlas Rent (D29, supuesto), instancia PostgreSQL compartida con Balance si existe (D30), reglas de reutilización en CLAUDE.md §2, tabla de piezas reutilizadas en DOC_TECNICA §3.1. Única dependencia nueva: QuestPDF.
- Archivos: CLAUDE.md, docs/ESPECIFICACION.md, PLAN.md, DESIGN.md, DOC_TECNICA.md, SEGURIDAD.md, db/scripts/*.sql (roles rent_*), config/variables-entorno.example, docker-compose.dev.yml (postgres:16-alpine), CI, .claude/.
- Por qué: petición del responsable; stack de Atlas Balance aportado el 14/10/2026.
- Verificado: coherencia cruzada entre documentos. Sin código aún.

## [14/10/2026 14:30] DECISION · F0 · Especificación v3.0 y paquete regenerado
- Qué: especificación reescrita (v3.0) integrando las decisiones D1-D28; regenerados CLAUDE.md, AGENTS.md, PLAN.md, DESIGN.md, DOC_TECNICA.md, MANUAL_USUARIO.md, SEGURIDAD.md, BUGS.md, configuración de Claude Code, CI, scripts SQL (`db/scripts`), `config/variables-entorno.example`.
- Por qué: respuestas del responsable del 14/10/2026:
  - Stack .NET 10 + PostgreSQL 18 + React, igual que Gestión de Caja y Atlas Connect; despliegue nativo en Windows Server sin Docker (D1, D2).
  - Acceso por web sin VPN con bloqueo de cuenta e IP; MFA solo admin (D3, D4; riesgos SEC-R01, SEC-R02 aceptados).
  - Documento en una sola moneda; pago en otra moneda mediante cobro con cambio aplicado, sustituyendo la duplicación de facturas (D7, D8).
  - Anular factura anula solo la parte de cobro asignada a esa factura (D6).
  - Anticipo como nota; alquiler corto por medio día / día completo cobrado antes; recordatorios automáticos o manuales; cotizaciones sin vencida; permiso de envío de cotizaciones por empleado; documentos de identidad sin enmascarar pero cifrados; backups: 4 diarios (D10-D20).
  - WhatsApp pendiente de proveedor (D23); sin fechas fijas (D24).
- Eliminado respecto a v2.x: Docker/Linux en servidor, NestJS, pg-boss, Caddy, VPN, MCP público, alquiler por horas, anticipo como cobro, vigencia de cotizaciones, enmascarado de documentos, Playwright para PDF.
- Verificado: coherencia cruzada entre documentos revisada. Sin código aún.

## [14/10/2026 07:45] DECISION · F0 · Comprobante normal, sin e-factura (spec v2.2)
- Qué: se revierte la propuesta e-CF de la v2.1. Vuelve el numerador de comprobante fiscal propio por serie (rango, vencimiento, alertas, bloqueo). E-factura fuera de la app: campo de texto manual opcional `referencia_fiscal_externa`. Eliminados P18, estados "Pendiente e-CF"/"e-CF rechazado", tabla `ecf_referencia` y módulo `ecf`.
- Archivos: CLAUDE.md, docs/ESPECIFICACION.md (0.0 #16, §1, §2, §3, 4.7, 4.16, 5.4, 6, 9.6, 10, 12, 13), PLAN.md (F0-01, F3-03, riesgos), DESIGN.md, MANUAL_USUARIO.md, DOC_TECNICA.md, SEGURIDAD.md.
- Por qué: decisión del responsable.
- Riesgo: validez del comprobante normal tras el 15/11/2026 (Ley 32-23) la confirma el contador.

## [13/10/2026 23:30] DOCS · F0 · Paquete inicial para Claude Code
- Qué: CLAUDE.md, docs/PLAN.md, docs/DESIGN.md, docs/BUGS.md, docs/SEGURIDAD.md, docs/MANUAL_USUARIO.md, docs/DOC_TECNICA.md, configuración de Claude Code (.claude/), CI base, .env.example, .gitignore, docker-compose.dev.yml.
- Por qué: arranque del proyecto.
- Verificado: coherencia cruzada entre documentos revisada a mano. Sin código aún.

## [13/10/2026 23:00] DECISION · F0 · Especificación v2.1
- Qué: docs/ESPECIFICACION.md pasa a v2.1 con 15 correcciones (sección 0.0, #16-#30).
- Por qué: e-CF obligatorio (Ley 32-23, plazo final 15/11/2026) antes del corte; fórmula de pendiente incoherente; esfuerzo mal estimado; contenedores; numeración de importadas; JWT vs revocación; permisos de borrador y exportación; MCP público; PDF sin aislamiento; auditoría borrable.
- Pendiente: P18 (sistema e-CF y validez de series 1/2) bloquea la Fase 3.
