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
- **Facturas (F3-02):** una sola tabla `factura` para facturas y proformas — lo que las distingue es `serie.tipo`, no el sitio donde viven; con dos tablas habría que duplicar líneas, totales y cobros sin ganar nada. `factura_linea` cuelga de ella con cascada al borrado, que solo se da en borrador. Dos `CHECK`: `(estado = 1) = (numero IS NULL)` (un borrador no tiene número y una emitida lo tiene) y `tipo_cambio_fecha IS NULL OR fecha_emision IS NOT NULL` (el cambio congelado solo existe si el documento salió). El ITBIS se congela **en la línea** (`porcentaje_itbis`, `tipo_itbis_id`), no solo en la cabecera: la factura dice "este importe lleva este impuesto" y cambiar el tipo de ITBIS mañana no puede cambiar lo que una factura ya emitida dice.
- **Cobros (F3-06):** `cobro` (dinero que entró, con su cambio congelado y su estado), `cobro_asignacion` (a qué factura se aplica) y `saldo_a_favor` (**una fila por contrato y moneda**, no una por cobro: el crédito es un saldo del contrato y se actualiza al cobrar y al consumir). **Ninguna de las tres lleva `DELETE` para `rent_app`**: un cobro se anula con motivo y sus asignaciones se quedan con `vigente = false`, que es la diferencia entre anular y borrar (§3.10.1, AGENTS.md §6). `CHECK` en `cobro`: `importe_recibido > 0`, `saldo_favor_usado >= 0 AND excedente >= 0` y `(estado = 2) = (anulado_at IS NOT NULL AND motivo_anulacion IS NOT NULL)` —anular con motivo y con fecha, o no está anulado. La cascada de `cobro_asignacion` sobre `cobro` no cambia eso: sin `DELETE` concedido, solo un `DROP` de tabla o un script de migración la usaría.
- **Pago en otra moneda (F3-07):** `cobro_asignacion` gana `importe_recibido numeric(18,2)` y `tipo_cambio_valor numeric(18,6)`, **las dos nulas o las dos puestas** (`CHECK (importe_recibido IS NULL) = (tipo_cambio_valor IS NULL)`). Nulas cuando el cobro y la factura comparten moneda: ahí no se aplicó conversión y un cambio de 1 afirmaría que la hubo (es el mismo motivo por el que el dólar no tiene fila en `tipo_cambio`, F3-01). El `cobro` **no cambia**: su `tipo_cambio_valor` sigue siendo el del día aunque la asignación guarde el que el gerente fijó, porque son dos datos distintos —el del día justifica los informes (D9) y el aplicado explica el cobro—. La tabla nueva `ajuste_diferencia_cambio` (`factura_id`, `importe`, `moneda`, `tipo_cambio_valor`, `motivo`, `ip`, `anulado_at/por`, `motivo_anulacion`, `created_at/by`) **tampoco lleva `DELETE`**: corrige un saldo de factura, se anula con motivo. Sus `CHECK`: `importe > 0`, `tipo_cambio_valor > 0`, `length(trim(motivo)) > 0` y `(anulado_at IS NULL) = (anulado_por IS NULL AND motivo_anulacion IS NULL)`.
- **Tolerancia de la diferencia de cambio: vive en `ajuste_general`, no en la configuración de despliegue.** Las columnas `tolerancia_cambio_usd` y `tolerancia_cambio_dop` son de `20261015042341_Configuracion` (F1-04) y **nacen a `NULL`**: la fila semilla se inserta sin ellas a propósito, porque §3.10.2 marca esos importes como [Supuesto] y nadie los ha aprobado. **Quién la escribe: el administrador**, desde *Configuración → Ajustes generales* (`PUT /api/v1/config/ajustes`, permiso `configuracion.editar`); el servicio de cobros la lee con `ServicioConfiguracion.LeerAjustesAsync`, el mismo camino que la pantalla. Una moneda sin valor, o con valor cero, significa **ajuste desactivado** y el intento sale en 409 diciendo dónde configurarlo. No hay segunda fuente (ni variable de entorno ni constante): dos sitios donde escribir una cifra que no sabes cuál manda es una trampa silenciosa (BUG-007).

### 4.3 Piezas críticas en SQL
- **Exclusión de rangos:** `EXCLUDE USING gist (unidad_id WITH =, daterange(fecha_inicio, fecha_fin, '[)') WITH &&) WHERE (bloquea)` sobre `ocupacion_rango` (`btree_gist`).
- **Medio día:** `ocupacion_dia (unidad_id, fecha, tipo int)` con `tipo` 1 = `medio_dia`, 2 = `dia_completo`; trigger que serializa por unidad con `SELECT ... FROM unidad WHERE id = NEW.unidad_id FOR UPDATE`, rechaza si hay un rango bloqueando esa fecha y aplica la capacidad de 2 medios días o 1 día completo.
- **Capas de bloqueo (decidido en F2-01):** la disponibilidad no se guarda en `contrato` ni en `reserva`, sino en dos tablas que los escriben esos servicios en su propia transacción. Motivo: una restricción de exclusión no puede abarcar dos tablas, así que con una por tabla un contrato y una reserva mensual del mismo rango se solaparían sin que nada lo detecte, y la especificación (§3.4) exige comprobar la disponibilidad en la base sin carreras. `ocupacion_rango` es el bloqueo por rango (contrato, reserva mensual) y `ocupacion_dia` el bloqueo por día suelto (reserva corta, mantenimiento); un trigger en cada tabla comprueba el otro lado, para que un rango no tape días reservados ni al revés. Liberar es `bloquea = false`, nunca borrar.
- **Contador de serie:** `UPDATE serie SET siguiente = siguiente + 1 WHERE id = @id RETURNING siguiente - 1`; `UNIQUE (serie_id, numero)`.
- **Contador de serie en la factura (F3-02):** la emisión llama a `ServicioSeries.ReservarNumeroAsync` **dentro de la transacción de la factura**, y esa llamada reutiliza la transacción abierta si la hay. Por eso un fallo posterior (un choque de índice, un cliente que ya no está) deshace el número junto con la factura: no hay número consumido sin factura detrás (D28). La red que lo hace inevitable si dos emisiones se colaran es doble: `UNIQUE (serie_id, numero)` en `factura` (índice parcial `WHERE numero IS NOT NULL`, porque un borrador no tiene número) y `UNIQUE (serie_id, numero)` en `numero_serie`.
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
- **ITBIS de la factura (F3-02):** es **por línea** (§3.8), no sobre el total. `CalculoImpuestosLinea.Calcular(cantidad, precio, porcentaje)` redondea una vez el importe (`cantidad × precio`) y una vez su impuesto (`importe × porcentaje / 100`), y los totales de la cabecera son `Subtotal = Σ Importe`, `Itbis = Σ ItbisImporte` y `Total = Subtotal + Itbis`. No es lo mismo que redondear el subtotal entero: dos líneas al 18 % de 100,01 y 0,30 dan 18,00 + 0,05 = **18,05**, mientras que 100,31 × 18 % redondeado da **18,06**. El céntimo se pierde si el impuesto se calcula sobre el total. `CalculoImpuestos.Calcular` (de factura completa, con retenciones) sigue existiendo para lo que venga después; hoy no lo usa nadie. El 100 es un divisor de porcentaje y no una cifra fiscal: el porcentaje sale de `tipo_itbis` (D14).
- **Signo de las líneas (F3-02):** el tipo lo decide. Una línea `Descuento` con importe positivo se rechaza con 409, y cualquier otro tipo con importe negativo también: restar es tener una línea de descuento, no poner un signo raro. El subtotal es por tanto la suma algebraica de las líneas.
- **Borrador reescribible (F3-02):** una factura en borrador no tiene número, ni comprobante, ni deuda, así que se le cambian la cabecera y las líneas, y sus líneas se borran. Emitida, ninguna línea se toca: lo que hay es anular y emitir otra (F3-08).
- **Saldo de factura (F3-07):** `Dominio/CalculoCobro.Calcular(total, retencion, cobrado, ajustado, vencimiento, hoy)` → record con `Cobrado`, `Ajustado`, `Pendiente`, `Estado`, `Vencida` y `DiasVencida`. **Pendiente = total − retenciones − cobrado − ajustado, acotado a cero**: un pendiente negativo significaría que se ha cobrado de más, y eso es saldo a favor (§3.10.1), que vive en otra tabla con otras reglas; devolverlo negativo haría que cualquier suma de pendientes de §3.13 descuadrara contra el dinero real. Estado = `Pagada` / `Parcial` / `Pendiente` (§3.8) y `Vencida` es **una bandera aparte** (pendiente > 0 y hoy > vencimiento), porque vencida no sustituye al estado: la vista de cuentas necesita las dos cosas. `cobrado` y `ajustado` salen de **dos consultas agregadas** sobre la página, no de una por factura, y **no se suman entre sí en la respuesta**: `cobrado` es dinero que entró en la caja y `ajustado` no lo es (§3.10.2); mezclarlos haría que un informe de caja dijera que entraron más dólares de los que entraron. Los dos juntos son los que cierran la factura. La retención entra por parámetro y hoy vale cero porque la columna es de F3-04; la prueba que heredará F3-04 está en `CobroCalculoTests`.
  - **Nota para F3-10 (vista de cuentas y panel de cobro): para saber si una factura está cerrada hay que sumar `cobrado + retenciones + ajustado`.** `cobrado` a secas **no dice si la factura está saldada**, y usar `cobrado == total` como criterio daría por pagada una factura cerrada con retención o con un ajuste, y por no pagada una factura cuyo total baja por retención. Y al revés: un informe de caja **solo** puede sumar `cobrado`, porque `ajustado` no es dinero que entró. Son dos sumas distintas con dos propósitos distintos, y confundirlas es como se infla un informe de caja.
- **Cobro (F3-06):** `cobro (moneda_recibida, importe_recibido, tipo_cambio_valor/tipo_cambio_fecha congelados)`; `cobro_asignacion (factura_id, importe en la moneda de la factura, vigente)`; `saldo_a_favor (contrato_id, moneda, importe)`. **FIFO dentro del contrato y de la moneda recibida**, por `FechaVencimiento`, luego `FechaEmision` y luego `Id` (desempate estable: sin él dos facturas del mismo día darían un orden que cambia entre dos llamadas). Un cobro parcial deja la siguiente a medias y no la salta. **Editable**: si quien cobra manda asignaciones, el servicio no propone nada; si no, propone la FIFO. `POST /simular` devuelve esa propuesta **sin escribir nada**. Excedente = `importe_recibido + saldo_favor_usado − Σ asignaciones`, y sin contrato se queda en la columna `excedente` del cobro sin generar crédito. **El crédito se gasta solo cuando quien cobra lo pide** (`usarSaldoAFavor`): §3.10.1 no dice a qué factura se descuenta, y aplicarlo por las reglas al emitir sería inventar la regla.
- **Anular un cobro (F3-06):** una transacción — el cobro pasa a `Anulado` con motivo y fecha, sus asignaciones quedan con `vigente = false` **sin borrarse**, y el saldo a favor vuelve a como estaba antes (sale el excedente, vuelve el crédito consumido). Las facturas recupuran su pendiente porque sus asignaciones ya no cuentan.
- **Pago en otra moneda (F3-07, §3.10.2, D8):** la FIFO **ya no filtra por moneda**: propone las facturas del contrato en cualquier moneda, con su equivalente en la moneda recibida. El reparto va **en la moneda recibida** —el dinero está en pesos y hay que decidir a qué facturas llega—: se convierte el pendiente a la moneda del cobro (`pendiente × cambio`), se toma `min(disponible, eso)` y se vuelve a la moneda de la factura con `ImporteRecibido / TipoCambioValor`, redondeado half-up a dos decimales (`Redondeo.Importe`). El importe **nunca pasa del pendiente**: sobrar es saldo a favor (§3.10.1), no sobrepago. **La factura conserva su moneda, su importe, su número y su comprobante**; pagar en pesos una factura de dólares no emite otra factura ni gasta un número de serie.
  - **Qué cambio se aplica:** por defecto el del día de la moneda recibida (`ServicioTiposCambio`), y el gerente puede mandar `tipoCambioAplicado` con el que se usó de verdad. El que va a cada asignación es el aplicado; **`cobro.tipo_cambio_valor` conserva el del día** aunque no coincidan (D9). No hay conversión al revés: convertir una factura en pesos con un cobro en dólares necesitaría el cambio del peso, y el cobro solo trae el de la moneda en que entra el dinero. En la FIFO esa factura se **salta** y sale en `deudaEnOtraMoneda`; en una asignación explícita es un 409 que lo explica.
  - **`deudaEnOtraMoneda` cambió de sentido:** ya no es «la deuda que el cobro ignora» sino **la que queda viva después de aplicarlo**. El nombre y el formato se conservan para no romper al front de F3-10.
- **Ajuste por diferencia de cambio (F3-07, §3.10.2):** cierra el pendiente que dejó un cambio que no dividía exacto. **El importe lo calcula el servicio** (es el pendiente real) y solo lo acepta si cabe en la tolerancia; **no se pide** porque, si quien firma eligiera el importe, el mismo botón serviría para tapar un saldo de cualquier tamaño. **Sin tolerancia configurada el ajuste está desactivado** y sale en 409 diciendo que la fije el administrador en *Configuración → Ajustes generales* (§4.2): §3.10.2 marca esos importes como [Supuesto], y un ajuste que se permite sin que nadie haya escrito una cifra es una pérdida de dinero silenciosa. Con `pendiente > tolerancia` el 409 dice el importe, la tolerancia y las dos salidas de §3.10.2: que el cliente pague la diferencia en un cobro nuevo, o que se corrija el cambio del cobro anterior. Va con `cobro.registrar` —mueve el mismo saldo que el cobro— y **no** con un permiso nuevo.
- **Anular factura (una transacción):** factura y comprobante anulados; sus `cobro_asignacion` anuladas; cobros sin asignaciones vigentes → anulados; auditoría con la lista de afectados.
- **Anticipo:** campos de nota en `reserva`; ninguna consulta económica los lee.

## 6. API
- REST `/api/v1`, JSON, errores `ProblemDetails` con código de referencia.
- Listas paginadas: `{ data, total, page, pageSize, totalPages }`; ordenación `?sortBy=campo&sortDir=asc|desc`.
- Mutaciones sensibles aceptan `Idempotency-Key`.
- Todo endpoint con `[Accion("...")]`; política global de denegar sin atributo.
- Frontend: TanStack Query para lecturas/caché/invalidación; Axios con `withCredentials`, cabecera `X-CSRF-Token` e interceptor que renueva la sesión una vez ante 401 y, si falla, lleva al login.
- Tabla de endpoints por módulo: se añade al implementarlos.

### 6.1 Tipos de cambio (F3-01)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/tipos-cambio` | `factura.ver` | Histórico paginado; filtros `moneda`, `desde`, `hasta` y orden `?sortBy=fecha\|moneda\|valor\|createdAt`. |
| `GET /api/v1/tipos-cambio/actual?moneda=&fecha=` | `factura.ver` | Valor de un día. Sin `fecha`, hoy en Santo Domingo. Es lo que leerán la factura (F3-02) y el cobro para congelar el cambio. |
| `GET /api/v1/tipos-cambio/{id}` | `factura.ver` | Fila concreta del histórico. |
| `POST /api/v1/tipos-cambio` | `configuracion.editar` | Carga manual. Sin `fecha`, la de hoy. Acepta `Idempotency-Key`. |

- **Ninguna acción nueva.** Ver es `factura.ver` (la fila 7 de §2, "Ver facturas individuales", que
  también necesitarán las pantallas de factura y cobro). Cargar a mano es `configuracion.editar`,
  que de §2 solo tiene el administrador: es la operación que sustituye a la fuente cuando esta falla
  (§3.12), así que es configuración de la casa y no una tarea del día a día.
- **Un valor por día y por moneda** (`UNIQUE (fecha, moneda)`): el cambio se congela al emitir y al
  cobrar (D9), así que a lo largo del día el valor es uno y dos documentos no pueden congelar
  números distintos.
- **USD no se guarda.** Se resuelve a 1 sin consultar la base, y un `CHECK (moneda <> 1)` impide que
  se cuele una fila que solo podría contradecirlo.
- **La carga manual no la pisa la fuente.** Es el camino de recuperación que fija §3.12: si el
  trabajo de las 05:00 lo sobrescribiera, la carga manual no habría arreglado nada. Una fila que vino
  de la fuente sí se actualiza con el valor del día.
- **Origen de la escritura.** `exchange_rate_api` para el trabajo (auditoría con `usuario_id` nulo y
  `origen = "job"`) y `manual` para la API (origen `api`, con la IP de la petición). La fila guarda
  el origen, y con él el `created_by` y el `created_at` de quien la escribió.

### 6.2 Facturas (F3-02)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/facturas` | `factura.ver` | Listado paginado; filtros `estado`, `serieId`, `clienteId`, `unidadId`, `moneda`, `desde`/`hasta` (fechas de emisión) y orden `?sortBy=createdAt\|fechaEmision\|vencimiento\|total\|numero`. Cada fila trae también `cobrado`, `pendiente`, `estadoCobro` y `vencida`, resueltos con **una consulta agregada** sobre la página (§3.13). **Sin totales agregados**: son de §3.15 y solo gerente/admin los ven (F3-10). |
| `GET /api/v1/facturas/{id}` | `factura.ver` | Factura con sus líneas y con `cobrado`, `pendiente`, `estadoCobro` y `vencida` derivados de los cobros (§3.8, F3-06). No expone retención (F3-04). |
| `POST /api/v1/facturas` | `factura.borrador` | Alta en borrador. Sin número y sin cambio: los dos los pone la emisión. Acepta `Idempotency-Key`. |
| `PUT /api/v1/facturas/{id}` | `factura.borrador` | Edita la cabecera. 409 si ya no está en borrador. Acepta `Idempotency-Key`. |
| `POST /api/v1/facturas/{id}/lineas` | `factura.borrador` | Alta de línea; recalcula los totales. `cantidad` × `precioUnitario` y el ITBIS los calcula la aplicación. Acepta `Idempotency-Key`. |
| `PUT /api/v1/facturas/{id}/lineas/{lineaId}` | `factura.borrador` | Edición de línea. 409 si la factura ya salió. Acepta `Idempotency-Key`. |
| `DELETE /api/v1/facturas/{id}/lineas/{lineaId}` | `factura.borrador` | Baja de línea, **solo en borrador**. Acepta `Idempotency-Key`. |
| `POST /api/v1/facturas/{id}/emitir` | `factura.emitir` | Emisión: número, fecha y cambio congelado, todo en una transacción. Acepta `Idempotency-Key`. |

- **El servicio también comprueba el permiso**, con `IPolicyService.PuedeAsync`, y no solo el atributo
  del endpoint: F3-05 emite facturas desde un trabajo de Hangfire, sin sesión HTTP ni filtro, y con la
  comprobación solo en el controlador ese camino emitiría sin mirar quién lo hace. Es el mismo patrón
  que `ExigirEstadoAsync` de los contratos (F2-05).
- **El 409 de la emisión** es el conflicto de negocio: la factura no está en borrador, la serie está
  desactivada, la serie tiene moneda fija y no es la de la factura, no hay líneas, o **no hay tipo de
  cambio del día** (D9: emitir sin cambio congelado deja un documento que los informes no pueden
  consolidar; la salida es la carga manual de §3.12 y el mensaje la dice).
- **Líneas: van por su propio endpoint** y no dentro de la cabecera, porque un descuento mal puesto se
  corrige borrando una línea y replace-all obligaría a reenviar todas las demás.
- **Series 9 y 3**: la histórica (9) queda desactivada por la migración de F3-02 —no emite, la usan las
  importaciones de Factusol conservando su número— y la fiscal (3) es la que genera comprobante (F3-03).

### 6.3 Cobros (F3-06, F3-07)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/cobros` | `factura.ver` | Listado paginado; filtros `clienteId`, `contratoId`, `moneda`, `estado` (`Registrado`/`Anulado`), `desde`/`hasta` (fechas efectivas) y orden `?sortBy=fechaEfectiva\|createdAt\|importe\|estado`. **Sin totales agregados**: son de §3.15 (F3-10). |
| `GET /api/v1/cobros/{id}` | `factura.ver` | Cobro con sus asignaciones. Las de un cobro anulado **salen igual**, con `vigente = false`: lo que deja de contar no se esconde. |
| `POST /api/v1/cobros` | `cobro.registrar` | Registro. Cuerpo = el de `simular`. Acepta `Idempotency-Key`: un reintento devuelve el cobro ya registrado y no mete dos veces el mismo dinero. |
| `POST /api/v1/cobros/simular` | `cobro.registrar` | **Misma entrada, no muta nada.** Devuelve la propuesta FIFO (factura, moneda, importe aplicado, importe recibido, tipo de cambio aplicado y pendiente previo), `totalAsignado`, `excedente`, el crédito disponible y `deudaEnOtraMoneda`. |
| `POST /api/v1/cobros/{id}/anular` | `cobro.registrar` | Anulación **con motivo obligatorio** (400 sin él). No borra: anula el cobro y desactiva sus asignaciones. Acepta `Idempotency-Key`. |
| `POST /api/v1/cobros/ajustes-diferencia-cambio` | `cobro.registrar` | **F3-07.** Cierra por ajuste el pendiente de una diferencia de cambio. Cuerpo `{ facturaId, tipoCambioValor, motivo }`; **el importe no se pide**. Acepta `Idempotency-Key`. |
| `POST /api/v1/cobros/ajustes-diferencia-cambio/{id}/anular` | `cobro.registrar` | **F3-07.** Anulación **con motivo obligatorio**. No borra: la fila queda anulada y la factura recupera su pendiente. Acepta `Idempotency-Key`. |

- **Ninguna acción nueva.** Ver un cobro es `factura.ver` (la fila 7 de §2) y mover dinero es
  `cobro.registrar`, que D15 deja desactivado al empleado. Registrar, simular, anular y **ajustar** llevan
  la misma acción: el ajuste mueve el mismo saldo que la factura tiene pendiente, y ponerlo detrás de
  otro permiso habría sido tocar la matriz de permisos por dos endpoints.
- **Cuerpo del registro:** `clienteId`, `contratoId` (nulo = cobro suelto), `fechaEfectiva`,
  `monedaRecibida`, `importeRecibido`, `formaPagoId`, `cuentaReceptoraId`, `referencia`,
  `usarSaldoAFavor`, `asignaciones` (opcional; `{ facturaId, importe }`, importe **en la moneda de la
  factura**, también cuando la factura está en otra moneda) y `tipoCambioAplicado` (opcional, F3-07).
  Si `asignaciones` no viene, se propone la FIFO.
- **`tipoCambioAplicado` (F3-07):** el cambio que el gerente dice que se usó de verdad. Ausente, se usa el
  del día de la moneda recibida. Si viene, **la asignación guarda ese y el cobro conserva el del día** (§5).
- **`deudaEnOtraMoneda` (F3-07):** desde F3-07 es la deuda en otra moneda que **queda viva después de
  aplicar este cobro**, no la que el cobro ignora. El nombre y el formato no cambian para no romper al
  front de F3-10; lo que cambia es lo que dice.
- **El 409 es el conflicto de negocio**: asignar más del pendiente, asignar a una factura de otro
  contrato, a una cuya moneda no se puede convertir con este cobro (§5), a una que no genera deuda
  (borrador, anulada o serie sin `suma_a_saldos`), usar más crédito del que hay, cuenta receptora de otra
  moneda, cobro ya anulado, **ajustar una diferencia mayor que la tolerancia** (el mensaje lleva el
  importe, la tolerancia y las dos salidas de §3.10.2), o **falta el tipo de cambio del día** en una
  moneda que no sea el dólar (D9; la salida es la carga manual de §3.12 y el mensaje la dice).
- **`ajustado` en la factura (F3-07):** la factura expone `cobrado` y `ajustado` **por separado** y no
  los suma en ningún campo. `ajustado` no es dinero que entró en la caja; junto con `cobrado` es lo que
  cierra la factura, y eso ya lo dicen `pendiente` y `estadoCobro`.
- **El servicio también comprueba el permiso**, con `IPolicyService.PuedeAsync`, y no solo el atributo
  del endpoint: los trabajos de Hangfire entran por el servicio, sin sesión HTTP ni filtro. El intento
  denegado se audita antes de lanzar.
- **Una transacción, un commit:** el cobro, sus asignaciones y el movimiento del saldo a favor entran
  juntos, y la auditoría (`cobro.registrado` y, si hubo crédito, `cobro.saldo_a_favor_usado`) va dentro.
  El ajuste es su propia transacción (`ajuste_diferencia_cambio.registrado`) con la IP en la fila **y** en
  la auditoría.
- **TODO(F4-01): el recibo en PDF no está aquí.** Es de la fase 4, con QuestPDF y las plantillas por
  serie. **No hay endpoint que devuelva un PDF provisional**: un recibo de dinero provisional es un
  documento que alguien puede imprimir y archivar.

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
| Tipo de cambio | F3-01 | Hangfire | diario 05:00 AST ⚠️ | `UNIQUE (fecha, moneda)` |
| Recordatorios | *previsto* | Hangfire | diario 09:00 AST | por (cliente, unidad, fecha) |
| Alertas de vencimiento de factura | *previsto* (F3) | Hangfire | cada hora | dedupe por (tipo, entidad) |
| Envíos | *previsto* (F4) | Hangfire (cola) | continuo | por `envio.id` |
| Outbox | *previsto* (F3) | Hangfire | cada minuto | por evento |
| Limpieza de sesiones, intentos, temporales | *previsto* | Hangfire | diario | — |
| Comprobación de actualizaciones | *previsto* | Hangfire | diario | — |
| Restauración y actualización | F1-12 | Watchdog | bajo demanda | copia previa obligatoria |

⚠️ **Colisión de hora pendiente:** el tipo de cambio se escribió en F3-01 a las **05:00**, que es la
hora del aviso de reserva a 7 días de F2-03, y no a las 08:00 que tenía aquí previsto. Va antes a
propósito: los trabajos de F2 escriben en el estado del negocio y por eso quieren la copia de las
02:00 por delante, mientras que el tipo de cambio es un valor de referencia que la factura y el
cobro congelan al emitirse, así que importa que esté antes de que empiece el día. Los dos trabajos
que comparten hora son cortos, ninguno espera al otro y ninguno escribe en la misma fila. Si algún
día hay que mover uno, la decisión no es de gusto sino de qué tiene que existir antes de las
primeras facturas. Y lo mismo con los trabajos previstos de F3 y F4: las horas de
`ProgramacionJobs.Todos` son las que manda, no las de esta tabla.

### 8.0 Reglas que siguen los trabajos de F2
- **Una transacción por contrato** en la renovación y en el fin: un contrato que falle no deja a medias los
  demás (`ServicioRenovaciones`, `ServicioFines`).
- **La colisión se trata, no se propaga**: `23505` del índice único significa "otra ejecución se adelantó",
  se cuenta como omitida y el trabajo sigue.
- **Auditoría sin usuario de sesión**: `UsuarioId = null` y `Origen = "job"`, escrita dentro de la
  transacción antes del commit. `auditoria` tiene `INSERT ... WITH CHECK (true)` con RLS, así que la fila
  con autor nulo entra.

### 8.0.1 Reglas del trabajo de tipo de cambio (F3-01)
- **No lanza si la fuente falla.** La fuente es una dependencia externa y el trabajo no puede ser la
  razón de que caiga: deja `alerta` con tipo `tipo_cambio` y clave `fallo:{fecha}` (el tipo ya estaba
  previsto en la tabla) y sigue. La carga manual de `POST /api/v1/tipos-cambio` es el camino de
  recuperación que fija §3.12.
- **La clave de la alerta lleva la fecha, al revés que las de F2-10**, porque el problema es «hoy no
  hay cambio», que mañana deja de ser cierto: sin fecha, la bandeja se llenaría de un aviso por día
  para siempre. Con fecha, fallar dos veces el mismo día actualiza el mismo aviso (`repeticiones`)
  y una pasada que sí trae el cambio lo cierra.
- **El motivo copiado a la alerta es solo el de la excepción propia** (`FalloTipoCambioException`), que
  está escrito para que alguien lo lea; de cualquier otra excepción va el tipo, porque su mensaje
  puede traer la URL con la clave de la API. Mismo criterio que el fallo de la copia en F1-12.
- **La carga manual no la pisa la fuente.** Solo se respeta el valor de la fuente si la fila no es
  manual. Al revés sí: una fila de la fuente se actualiza con el valor del día, así que correr el
  trabajo dos veces no crea una segunda fila y no lanza.

### 8.1 Estado (F1-12)
Implementados y programados, con la tabla de arriba como fuente de verdad: **backup cifrado** (02:00), **verificación de integridad de auditoría** (03:00) y **vigilancia de la copia** (04:00), de F1-12; **aviso de reserva a 7 días** (05:00), de F2-03; **renovaciones e incrementos** (06:00), de F2-06; **fin de contrato por fecha efectiva** (07:00), de F2-09; **avisos de operación** (08:00), de F2-10; y **tipo de cambio** (05:00), de F3-01. Sigue esperando el dominio que todavía no existe: alertas de vencimiento de factura (F3), recordatorios, envíos, outbox, limpieza y comprobación de actualizaciones.

- **Tipo de cambio (F3-01):** ExchangeRate-API, con `TipoCambio__Url` y `TipoCambio__ApiKey`
  opcionales; sin clave va al endpoint abierto y con clave al plan de pago, que devuelve la tabla en
  `conversion_rates` en vez de `rates` (el cliente entiende las dos). La columna `valor` es
  `numeric(18,6)` y se redondea con `Redondeo.TipoCambio`, que es la única razón de que el tipo de
  cambio no pase por `numeric(18,2)` de la convención global.

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

**Datos de configuración que la aplicación NO siembra y sin los que no se puede cobrar (F3-06).** Un
cobro exige `forma_pago_id` y `cuenta_receptora_id`, y **no se pueden registrar sin ellos**: el servicio
responde 404 y la pantalla de cobro no deja guardar. Se crean desde *Configuración → Formas de pago* y
*Configuración → Cuentas receptoras* (`POST /api/v1/config/formas-pago` y `POST /api/v1/config/cuentas`,
permiso `configuracion.editar`), y hay que comprobarlo en la instalación:

- **Al menos una forma de pago activa.** Es dato de configuración y lo da el operador, no la
  aplicación: sembrarla en el código pondría nombres y intermédiaire de cobros en el código, que es donde
  no se pueden cambiar sin desplegar.
- **Al menos una cuenta receptora activa por cada moneda en la que se cobre**, y **su moneda tiene que
  ser la del importe recibido** (D8): un cobro en dólares contra una cuenta en pesos se rechaza con 409.
- La **cuenta en USD** es imprescindible: el dólar vale 1 sin consultar `tipo_cambio` (F3-01) y es la
  base de informes, así que es la moneda con la que se prueba el sistema.
- Lo mismo con los **tipos de ITBIS** (F1-08): sin ninguno, las facturas se pueden emitir pero ninguna
  lleva impuesto.

**Ajuste por diferencia de cambio: hay que configurarlo antes de que sirva de algo (F3-07).** No es una
cifra de despliegue sino un dato de negocio, y sale de `ajuste_general` (`tolerancia_cambio_usd`,
`tolerancia_cambio_dop`), que escribe el **administrador** en *Configuración → Ajustes generales*
(`PUT /api/v1/config/ajustes`, permiso `configuracion.editar`). Las columnas **nacen a `NULL`**: la fila
sembrada por `20261015042341_Configuracion` se inserta sin ellas a propósito, porque §3.10.2 marca los
importes (USD 1,00 / DOP 50,00) como **[Supuesto]** y nadie los ha aprobado. **Mientras no se escriban, el
ajuste por diferencia de cambio está desactivado**: el intento sale en 409 diciendo que hay que configurar
la tolerancia. Es deliberado —un pendiente que se cierra sin que nadie haya dicho que se puede cerrar es
una pérdida de dinero silenciosa— y **no hay ninguna otra fuente** (ni variable de entorno ni constante
en el código): dos sitios donde escribir una cifra sin saber cuál manda es la trampa que BUG-007
documenta. Un cero también desactiva el ajuste: es una decisión activa de no cerrar diferencias, y no lo
mismo que no haber configurado nada. `TODO(A#)` sigue pendiente de confirmar con la gerencia.

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
