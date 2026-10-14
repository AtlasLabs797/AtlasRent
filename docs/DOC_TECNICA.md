# Documentación técnica — Atlas Rent

> Estado inicial: diseño previsto. Cada sección se actualiza al implementar (Claude, en `/cerrar-tarea`). Lo funcional está en `ESPECIFICACION.md`; aquí solo el cómo. El stack y la organización son los de **Atlas Balance V-03.01** para reutilizar su código.

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

## 2. Versiones (las de Atlas Balance V-03.01, 14/10/2026)
| Componente | Versión |
|---|---|
| .NET SDK | 10.0.112 (`global.json`) |
| ASP.NET Core / EF Core | 10 / 10.0.12 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| EFCore.NamingConventions | 10 |
| Hangfire / Hangfire.PostgreSql | 1.8 / la de Atlas Balance |
| Serilog | 10 |
| MailKit / MimeKit | 4.17 |
| ClosedXML | 0.105 |
| QuestPDF | última estable — **nueva; [Verificar] licencia Community según facturación** |
| PostgreSQL | ≥16 (instancia existente) o 18 si se instala nueva |
| Node.js / npm | 24.14.1 |
| React | 19.2.8 |
| TypeScript | 5.6.3 |
| Vite | 8.0.16 |
| React Router | 8.3.0 |
| Zustand | 5.0.14 |
| TanStack Query / Virtual | 5 / 3 |
| Axios | 1.16 |
| React Hook Form | 7 |
| Recharts | 3 |
| Lucide React, QRCode | las de Atlas Balance |
| ESLint | 10 |
| xUnit / FluentAssertions / Testcontainers | v3 / la de Atlas Balance **[Verificar: FluentAssertions ≥ 8 requiere licencia comercial]** / la de Atlas Balance |
| Playwright | la de Atlas Balance |

Las versiones exactas se copian de los `.csproj`, `global.json` y `package-lock.json` de Atlas Balance en F1-01. Cualquier cambio de versión se propone y se anota aquí.

## 3. Estructura
```
backend/AtlasRent.Api/
  Program.cs        pipeline (copiado de Atlas Balance y adaptado)
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
frontend/src/       pages (lazy), components, stores (Zustand), api (Axios + TanStack Query), styles (tokens)
scripts/            PowerShell: install, update, start, uninstall (de Atlas Balance)
db/scripts/         SQL de roles y auditoría
```
Dominios: `Auth`, `Usuarios`, `Configuracion`, `Clientes`, `Inmuebles`, `Cotizaciones`, `Reservas`, `Contratos`, `Facturacion`, `Comprobantes`, `Cobros`, `Fianzas`, `TipoCambio`, `Documentos`, `Mensajeria`, `Informes`, `Alertas`, `Aprobaciones`, `Importacion`, `Backups`.

### 3.1 Piezas reutilizadas de Atlas Balance (se completa en F1-01/F1-02)
| Pieza | Origen en Atlas Balance | Destino | Adaptaciones |
|---|---|---|---|
| _(rellenar al copiar)_ | | | |

## 4. Base de datos
### 4.1 Roles (alineados con la separación de credenciales de Atlas Balance)
| Rol | Uso | Permisos |
|---|---|---|
| `rent_owner` | propietario de esquemas y tablas | NOLOGIN |
| `rent_migrador` | migraciones / instalador | DDL, miembro de `rent_owner` |
| `rent_app` | Api y Watchdog | DML en tablas de negocio; solo INSERT/SELECT en `auditoria`; sujeto a RLS |
| `rent_backup` | backup | solo lectura |

Si se comparte la instancia con Atlas Balance: base de datos y roles propios; ningún rol de una app tiene acceso a la base de la otra.

### 4.2 Convenciones
- PK `uuid`; número visible aparte. Nombres `snake_case` (EFCore.NamingConventions).
- Dinero `numeric(18,2)`, cambio `numeric(18,6)`, fechas de negocio `date`, marcas `timestamptz`.
- Comunes: `origen`, `created_at/by`, `updated_at/by`; concurrencia optimista (mecanismo de Atlas Balance).
- Económicos: estado anulado, nunca borrado. Resto: `deleted_at`, `deleted_by`.

### 4.3 Piezas críticas en SQL
- **Exclusión de rangos:** `EXCLUDE USING gist (recurso_id WITH =, periodo WITH &&) WHERE (bloquea)` en contratos y reservas mensuales (`btree_gist`).
- **Medio día:** `reserva_dia (recurso_id, fecha, mitades smallint check (mitades in (1,2)))`; trigger con `pg_advisory_xact_lock` por unidad que suma mitades del día (≤ 2) y comprueba que no haya contrato/reserva mensual en la fecha.
- **Contador de serie:** `UPDATE serie SET siguiente = siguiente + 1 WHERE id = @id RETURNING siguiente - 1`; `UNIQUE (serie_id, numero)`.
- **Comprobante:** `UPDATE secuencia_comprobante SET siguiente = siguiente + 1 WHERE serie_id = @id AND siguiente <= rango_hasta AND vence >= current_date RETURNING siguiente - 1`; sin fila → excepción "Comprobantes agotados o vencidos" y rollback completo.
- **Periódica:** índice único parcial `(contrato_id, periodo, concepto) WHERE estado <> 'anulada'`.
- **Auditoría:** append-only (`db/scripts/03_auditoria_append_only.sql`) + **firma de registros** y job de verificación de integridad, reutilizados de Atlas Balance.
- **RLS:** activado en `documento_inmueble` (solo gerente/admin) y `auditoria` (lectura solo admin/gerente), con el patrón de Atlas Balance (variable de sesión con usuario y rol fijada por la API en cada conexión).
- **Número de cliente:** `SEQUENCE` iniciada en `max(Factusol)+1`.
- **Índice ciego de documento:** `documento_hmac` = HMAC-SHA256(documento normalizado); índice único.

## 5. Lógica de dominio
- **Saldo de factura** = total − Σ retenciones − Σ asignaciones vigentes (moneda de la factura) − Σ ajustes de diferencia de cambio. Pagada / Parcial / Pendiente derivados. Vencida = saldo > 0 y hoy (Santo Domingo) > vencimiento.
- **Prorrateo:** `Math.Round(renta / díasDelMes × díasFacturables, 2, MidpointRounding.AwayFromZero)`, días inclusivos.
- **Emisión (una transacción):** validar → número de serie → comprobante (si fiscal) → congelar `cambio_informe` → Emitida → auditoría → outbox.
- **Cobro:** `cobro (moneda_recibida, importe_recibido, cambio_aplicado)`; `cobro_asignacion (destino, importe_moneda_destino, importe_moneda_recibida, estado)`. Propuesta FIFO por vencimiento dentro del contrato. Excedente → `saldo_favor`.
- **Pago en otra moneda:** equivalente = importe recibido convertido con el cambio aplicado, redondeado a 2; tolerancia configurable por moneda; `ajuste_diferencia_cambio` auditado.
- **Anular factura (una transacción):** factura y comprobante anulados; sus `cobro_asignacion` anuladas; cobros sin asignaciones vigentes → anulados; auditoría con la lista de afectados.
- **Anticipo:** campos de nota en `reserva`; ninguna consulta económica los lee.

## 6. API
- REST `/api/v1`, JSON, errores `ProblemDetails` con código de referencia.
- Listas paginadas con el formato de Atlas Balance; ordenación `?sortBy=&sortDir=`.
- Mutaciones sensibles aceptan `Idempotency-Key`.
- Todo endpoint con `[Accion("...")]`; política global de denegar sin atributo.
- Frontend: TanStack Query para lecturas/caché/invalidación; Axios con cookies y cabecera CSRF (cliente de Atlas Balance).
- Tabla de endpoints por módulo: se añade al implementarlos.

## 7. Seguridad técnica (detalle y estado en SEGURIDAD.md)
- **Base:** la de Atlas Balance (JWT en cookies HttpOnly, CSRF, BCrypt, MFA/TOTP, rate limiting, cabeceras, HSTS, auditoría firmada, registro de eventos de seguridad, control de sesiones y tokens).
- **Añadido para Atlas Rent (expuesta a internet):**
  - Validación de sesión contra BD en cada petición (revocación inmediata), caché ≤ 30 s.
  - Bloqueo de cuenta 5 → 30 min (si Atlas Balance ya lo tiene, se reutiliza).
  - `IpBanMiddleware`: tabla `bloqueo_ip`, escalado 1 h → 24 h → indefinido, lista blanca, desbloqueo admin.
  - Aviso al admin de entrada desde IP nueva.
  - Cifrado AES-256-GCM del documento de identidad con clave fuera de la BD.
  - Comprobación de contraseñas contra HaveIBeenPwned (k-anonymity, llamada HTTP sin paquete adicional).
- **Orden del pipeline:** ForwardedHeaders (solo proxies de confianza, p. ej. Caddy) → HSTS/HTTPS → cabeceras → IpBan → RateLimiter → autenticación → sesión en BD → CSRF → autorización → auditoría.
- **Logs:** Serilog con redacción de propiedades sensibles.

## 8. Jobs
| Job | Dónde | Frecuencia | Idempotencia |
|---|---|---|---|
| Backup cifrado BD + documentos → local (4) + Google Drive | Hangfire (como Atlas Balance) | diario 02:00 AST | por fecha |
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

## 9. Despliegue (Windows Server x64, como Atlas Balance)
1. PostgreSQL: usar la instancia existente (≥16) o instalar la última estable; `listen_addresses='localhost'`, `pg_hba` solo local con `scram-sha-256`.
2. `db/scripts/01_roles.sql` y `02_basedatos.sql` como superusuario; contraseñas generadas en el momento y guardadas en el gestor de contraseñas de la empresa.
3. Paquete ZIP x64 (backend self-contained + frontend compilado + Watchdog + scripts).
4. `scripts/install.ps1` (adaptado de Atlas Balance): instala en `C:\AtlasRent`, crea servicios `AtlasRent.Api` y `AtlasRent.Watchdog` con cuentas sin privilegios, variables de entorno, reglas de firewall (solo 443), migraciones con `rent_migrador`.
5. Certificado: HTTPS directo en Kestrel (win-acme / Let's Encrypt o certificado de la empresa) o Caddy delante, igual que Atlas Balance.
6. Router: reenviar solo 443.
7. Comprobar checklist A.1 de SEGURIDAD.md.

**Actualizaciones:** sistema de Atlas Balance (GitHub Releases, validación de integridad y firma, copia previa, recuperación ante error) mediante `scripts/update.ps1` y Watchdog.

**Si comparte servidor con Atlas Balance:** puertos distintos o el mismo 443 con Caddy enrutando por subdominio; carpetas, servicios, roles de BD y claves separados.

## 10. Pruebas (igual que Atlas Balance)
- Backend: xUnit v3 + FluentAssertions; integración con Testcontainers PostgreSQL (obligatorio para exclusiones, triggers, RLS, numeración, auditoría); EF InMemory solo para lógica sin SQL específico.
- Frontend: ESLint 10, `tsc --noEmit`, Node.js Test Runner, Playwright E2E (reserva → contrato → factura → cobro en otra moneda).
- Casos obligatorios: ver ESPECIFICACION §8.
- CI (GitHub Actions): build, tests, auditoría npm y NuGet, verificación de versiones, escaneo de secretos, releases y firma de paquetes.

## 11. Operación
- Restaurar backup: runbook (FP-03).
- Admin bloqueado / sin MFA: procedimiento break-glass (F1-06; reutilizar el de Atlas Balance si existe).
- Rotación de la clave de cifrado de documentos (F1-09).
