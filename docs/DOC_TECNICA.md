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
| BCrypt.Net-Next | 4.2.0 (12 rondas) |
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
- **Logs:** Serilog con redacción de propiedades sensibles.

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
