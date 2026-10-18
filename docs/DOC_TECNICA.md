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
| MailKit / MimeKit | 4.18.1 — **añadido en F4-02** (envío de correo por SMTP, §3.14). Resolvió a MailKit 4.18.1 + MimeKit 4.18.1 + BouncyCastle.Cryptography 2.7.0 |
| System.Security.Cryptography.Pkcs | 10.0.13 — **añadido en F4-02**: MailKit 4.18.1 arrastra la 10.0.0, con dos avisos de vulnerabilidad conocidos (GHSA-2fmv-rv2c-59x7, moderada, y GHSA-846f-f764-76mx, alta). Se fija a la versión parcheada de la misma línea que el resto de paquetes de Microsoft del repo, con el mismo criterio que Newtonsoft.Json en F1-12, para que el `dotnet list package --vulnerable` de la CI siga limpio |
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
- **Alertas (`alerta`, F1-12 / F2-09):** lo que falló y que alguien tiene que mirar. La identidad de un fallo es **el par `(tipo, clave)`**, y lo único con unicidad estricta es la pareja **entre las alertas vivas**: `ix_alerta_tipo_clave` es `UNIQUE (tipo, clave) WHERE resuelta_at IS NULL`, más `ix_alerta_tipo_ocurrido_at` para los listados. **El filtro no es un adorno (BUG-008):** con un `UNIQUE (clave)` a secas, una alerta cerrada bloqueaba su clave para siempre —el `ON CONFLICT` de `ServicioAlertas` no insertaba nada ni la resucitaba— y un problema que aparecía, se cerraba y volvía a aparecer **no volvía a avisar**, al revés de §3.16 y de AGENTS.md §6. Cerrar es poner `resuelta_at` (nunca borrar) y, si el problema regresa, la fila **se reabre**: `RegistrarAsync` reabre la cerrada más reciente antes de insertar, y el `ON CONFLICT` sobre el índice parcial refresca la viva. `rent_app` tiene `SELECT, INSERT, UPDATE` y **no `DELETE`**.

- **Correo (`envio` y `plantilla_mensaje`, F4-02):** `envio` guarda **los bytes exactos del PDF que salió** (`pdf_copia` `bytea`), no una referencia, y `GET /api/v1/envios/{id}/pdf` devuelve esa columna. El documento se apunta con una **referencia lógica** —`tipo_documento` + `documento_id` + `documento_anio`— **sin clave foránea**, porque un envío apunta a facturas, estados de cuenta, recibos, cotizaciones y contratos, que están en cuatro tablas distintas y el estado de cuenta no tiene ni fila propia; lo que sí se guarda es lo que hace falta para repetir el envío y auditarlo. La contrapartida es que nada impide dejar un envío apuntando a un documento borrado, y da igual: **un envío no se borra** y un documento tampoco (D28). `rent_app` tiene `SELECT, INSERT, UPDATE` y **no `DELETE`**. Índices: `(tipo_documento, documento_id, documento_anio)` para el envío de un documento, `(estado)` para el filtro «envío fallido» y `(tipo_documento, documento_id, documento_anio, destinatario_email)` para el antidiplicado de §3.14 —los tres últimos **no son únicos a propósito**, porque forzar un reenvío tiene que poder escribir una segunda fila y lo que evita el duplicado es la comprobación previa del servicio, no una restricción que solo daría un error sin decir a quién se lo mandó ya. `plantilla_mensaje` es `UNIQUE (tipo, idioma)`: es lo que hace que guardar sea un `PUT` que crea o edita en vez de dos endpoints con dos formas de dejar la misma fila duplicada. **Nada se borra**: una plantilla se desactiva, porque un envío antiguo tiene que poder decir qué texto encontró al salir.
- **La clave del servidor de correo no está en la base de datos.** Vive en variables de entorno o en `dotnet user-secrets`, en la sección `Correo` (docs/SEGURIDAD.md §A.7). No hay columna donde escribirla y esa es la decisión: si se configurara en la base, acabaría en los backups y en `ajuste_general`, que es justo lo que no se quiere con una credencial.

### 4.3 Piezas críticas en SQL
- **Exclusión de rangos:** `EXCLUDE USING gist (unidad_id WITH =, daterange(fecha_inicio, fecha_fin, '[)') WITH &&) WHERE (bloquea)` sobre `ocupacion_rango` (`btree_gist`).
- **Medio día:** `ocupacion_dia (unidad_id, fecha, tipo int)` con `tipo` 1 = `medio_dia`, 2 = `dia_completo`; trigger que serializa por unidad con `SELECT ... FROM unidad WHERE id = NEW.unidad_id FOR UPDATE`, rechaza si hay un rango bloqueando esa fecha y aplica la capacidad de 2 medios días o 1 día completo.
- **Capas de bloqueo (decidido en F2-01):** la disponibilidad no se guarda en `contrato` ni en `reserva`, sino en dos tablas que los escriben esos servicios en su propia transacción. Motivo: una restricción de exclusión no puede abarcar dos tablas, así que con una por tabla un contrato y una reserva mensual del mismo rango se solaparían sin que nada lo detecte, y la especificación (§3.4) exige comprobar la disponibilidad en la base sin carreras. `ocupacion_rango` es el bloqueo por rango (contrato, reserva mensual) y `ocupacion_dia` el bloqueo por día suelto (reserva corta, mantenimiento); un trigger en cada tabla comprueba el otro lado, para que un rango no tape días reservados ni al revés. Liberar es `bloquea = false`, nunca borrar.
- **Contador de serie:** `UPDATE serie SET siguiente = siguiente + 1 WHERE id = @id RETURNING siguiente - 1`; `UNIQUE (serie_id, numero)`.
- **Contador de serie en la factura (F3-02):** la emisión llama a `ServicioSeries.ReservarNumeroAsync` **dentro de la transacción de la factura**, y esa llamada reutiliza la transacción abierta si la hay. Por eso un fallo posterior (un choque de índice, un cliente que ya no está) deshace el número junto con la factura: no hay número consumido sin factura detrás (D28). La red que lo hace inevitable si dos emisiones se colaran es doble: `UNIQUE (serie_id, numero)` en `factura` (índice parcial `WHERE numero IS NOT NULL`, porque un borrador no tiene número) y `UNIQUE (serie_id, numero)` en `numero_serie`.
- **Comprobante fiscal (F3-03):** `UPDATE numerador_fiscal SET siguiente_numero = siguiente_numero + 1 WHERE serie_id = @id AND siguiente_numero <= rango_hasta RETURNING siguiente_numero - 1`, **dentro de la transacción de la factura** y reutilizando la que ya está abierta; sin fila → 409 y rollback completo. Es un contador **distinto** del de la serie (`serie.siguiente` numera facturas; `numerador_fiscal.siguiente_numero` numera comprobantes) y vive en su propia fila, 1:1 con la serie, con `UNIQUE (serie_id)` en `numerador_fiscal` y `UNIQUE (serie_id, numero)` en `comprobante_fiscal`. El `WHERE … siguiente_numero <= rango_hasta` es lo que hace el bloqueo de agotado íntegro bajo carrera; el de vencimiento va antes, en el servicio, porque es una comparación de fechas. **Ningún rango, tipo ni umbral está sembrado ni en el código**: los configura el administrador en `PUT /api/v1/config/numeradores-fiscales/{serieId}`.
- **Periódica (F3-05):** columnas `periodo_inicio` / `periodo_fin` en `factura`, nulas en la factura tecleada a mano, e índice único parcial `NULLS NOT DISTINCT (contrato_id, periodo_inicio, concepto) WHERE periodo_inicio IS NOT NULL AND estado <> 3`. El filtro saca las facturas que no son periódicas y las anuladas (la clave se libera al anular, F3-08). `NULLS NOT DISTINCT` (PostgreSQL 15) es lo que protege también a la factura de la reserva corta, que llega con `contrato_id` nulo; **el modelo de EF no expresa esa variante**, así que si una migración reconstruye el índice hay que volver a escribirlo a mano.
- **Documentos PDF (F4-01):** `serie.plantilla_pdf` es el nombre de la plantilla del catálogo de `Services.RegistroPlantillasPdf` (hoy solo `estandar`, la de `docs/DESIGN.md §7`) y `serie.condiciones_pago` el texto del pie. Los dos son **anulables**: nulos significan «la plantilla de por defecto» y «sin condiciones escritas», y una serie dada de alta por importación es válida así. Un `plantilla_pdf` que no está en el catálogo **no cae en la de por defecto**: se rechaza al guardar la serie con 400 y la lista de las válidas, porque imprimir cada factura con un diseño que nadie pidió es un error que sale meses después. `subempresa` gana `rnc`, `direccion`, `telefono` y `email` (los cuatro anulables, mismo criterio) para la cabecera del documento. `subempresa.rnc` **va cifrado en reposo** con el mismo AES-256-GCM y el mismo formato `v1:nonce:cifrado` que `cliente.documento` (AGENTS.md §6) —por eso la columna es `text` y no `varchar(32)`: lo que se guarda es el texto cifrado—, se descifra al leer la configuración y **no lleva índice ciego** porque no se busca por RNC (la lista solo ordena por nombre). Se ve completo en la pantalla y en la cabecera del PDF, igual que el documento del cliente (D16), y a diferencia de este no va a la auditoría. La migración es puramente aditiva y **no necesita `GRANT`**: los permisos en PostgreSQL son por tabla para `INSERT`/`UPDATE`, no por columna.
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
- **Emisión (una transacción):** validar → **numerador del comprobante utilizable (si la serie es fiscal)** → número de serie → comprobante (si fiscal) → congelar `cambio_informe` → Emitida → auditoría → outbox. El numerador se comprueba **antes** del número de serie a propósito: si está agotado o vencido, la emisión se rechaza y **no se gasta número de factura** (criterio de aceptación de `docs/PLAN.md`).
- **ITBIS de la factura (F3-02):** es **por línea** (§3.8), no sobre el total. `CalculoImpuestosLinea.Calcular(cantidad, precio, porcentaje)` redondea una vez el importe (`cantidad × precio`) y una vez su impuesto (`importe × porcentaje / 100`), y los totales de la cabecera son `Subtotal = Σ Importe`, `Itbis = Σ ItbisImporte` y `Total = Subtotal + Itbis`. No es lo mismo que redondear el subtotal entero: dos líneas al 18 % de 100,01 y 0,30 dan 18,00 + 0,05 = **18,05**, mientras que 100,31 × 18 % redondeado da **18,06**. El céntimo se pierde si el impuesto se calcula sobre el total. `CalculoImpuestos.Calcular` (de factura completa, con retenciones) sigue existiendo para lo que venga después; hoy no lo usa nadie. El 100 es un divisor de porcentaje y no una cifra fiscal: el porcentaje sale de `tipo_itbis` (D14).
- **Signo de las líneas (F3-02):** el tipo lo decide. Una línea `Descuento` con importe positivo se rechaza con 409, y cualquier otro tipo con importe negativo también: restar es tener una línea de descuento, no poner un signo raro. El subtotal es por tanto la suma algebraica de las líneas.
- **Borrador reescribible (F3-02):** una factura en borrador no tiene número, ni comprobante, ni deuda, así que se le cambian la cabecera y las líneas, y sus líneas se borran. Emitida, ninguna línea se toca: lo que hay es anular y emitir otra (F3-08).
- **Saldo de factura (F3-07):** `Dominio/CalculoCobro.Calcular(total, retencion, cobrado, ajustado, vencimiento, hoy)` → record con `Cobrado`, `Ajustado`, `Pendiente`, `Estado`, `Vencida` y `DiasVencida`. **Pendiente = total − retenciones − cobrado − ajustado, acotado a cero**: un pendiente negativo significaría que se ha cobrado de más, y eso es saldo a favor (§3.10.1), que vive en otra tabla con otras reglas; devolverlo negativo haría que cualquier suma de pendientes de §3.13 descuadrara contra el dinero real. Estado = `Pagada` / `Parcial` / `Pendiente` (§3.8) y `Vencida` es **una bandera aparte** (pendiente > 0 y hoy > vencimiento), porque vencida no sustituye al estado: la vista de cuentas necesita las dos cosas. `cobrado` y `ajustado` salen de **dos consultas agregadas** sobre la página, no de una por factura, y **no se suman entre sí en la respuesta**: `cobrado` es dinero que entró en la caja y `ajustado` no lo es (§3.10.2); mezclarlos haría que un informe de caja dijera que entraron más dólares de los que entraron. Los dos juntos son los que cierran la factura. La retención entra por parámetro con el valor real de `factura.retencion` desde F3-04; la prueba que heredó está en `CobroCalculoTests` y los casos nuevos, en `Retenciones/RetencionCalculoTests`.
  - **Nota para F3-10 (vista de cuentas y panel de cobro): para saber si una factura está cerrada hay que sumar `cobrado + retenciones + ajustado`.** `cobrado` a secas **no dice si la factura está saldada**, y usar `cobrado == total` como criterio daría por pagada una factura cerrada con retención o con un ajuste, y por no pagada una factura cuyo total baja por retención. Y al revés: un informe de caja **solo** puede sumar `cobrado`, porque `ajustado` no es dinero que entró. Son dos sumas distintas con dos propósitos distintos, y confundirlas es como se infla un informe de caja.
- **Cobro (F3-06):** `cobro (moneda_recibida, importe_recibido, tipo_cambio_valor/tipo_cambio_fecha congelados)`; `cobro_asignacion (factura_id, importe en la moneda de la factura, vigente)`; `saldo_a_favor (contrato_id, moneda, importe)`. **FIFO dentro del contrato y de la moneda recibida**, por `FechaVencimiento`, luego `FechaEmision` y luego `Id` (desempate estable: sin él dos facturas del mismo día darían un orden que cambia entre dos llamadas). Un cobro parcial deja la siguiente a medias y no la salta. **Editable**: si quien cobra manda asignaciones, el servicio no propone nada; si no, propone la FIFO. `POST /simular` devuelve esa propuesta **sin escribir nada**. Excedente = `importe_recibido + saldo_favor_usado − Σ asignaciones`, y sin contrato se queda en la columna `excedente` del cobro sin generar crédito. **El crédito se gasta solo cuando quien cobra lo pide** (`usarSaldoAFavor`): §3.10.1 no dice a qué factura se descuenta, y aplicarlo por las reglas al emitir sería inventar la regla.
- **Anular un cobro (F3-06):** una transacción — el cobro pasa a `Anulado` con motivo y fecha, sus asignaciones quedan con `vigente = false` **sin borrarse**, y el saldo a favor vuelve a como estaba antes (sale el excedente, vuelve el crédito consumido). Las facturas recupuran su pendiente porque sus asignaciones ya no cuentan.
- **Pago en otra moneda (F3-07, §3.10.2, D8):** la FIFO **ya no filtra por moneda**: propone las facturas del contrato en cualquier moneda, con su equivalente en la moneda recibida. El reparto va **en la moneda recibida** —el dinero está en pesos y hay que decidir a qué facturas llega—: se convierte el pendiente a la moneda del cobro (`pendiente × cambio`), se toma `min(disponible, eso)` y se vuelve a la moneda de la factura con `ImporteRecibido / TipoCambioValor`, redondeado half-up a dos decimales (`Redondeo.Importe`). El importe **nunca pasa del pendiente**: sobrar es saldo a favor (§3.10.1), no sobrepago. **La factura conserva su moneda, su importe, su número y su comprobante**; pagar en pesos una factura de dólares no emite otra factura ni gasta un número de serie.
  - **Qué cambio se aplica:** por defecto el del día de la moneda recibida (`ServicioTiposCambio`), y el gerente puede mandar `tipoCambioAplicado` con el que se usó de verdad. El que va a cada asignación es el aplicado; **`cobro.tipo_cambio_valor` conserva el del día** aunque no coincidan (D9). No hay conversión al revés: convertir una factura en pesos con un cobro en dólares necesitaría el cambio del peso, y el cobro solo trae el de la moneda en que entra el dinero. En la FIFO esa factura se **salta** y sale en `deudaEnOtraMoneda`; en una asignación explícita es un 409 que lo explica.
  - **`deudaEnOtraMoneda` cambió de sentido:** ya no es «la deuda que el cobro ignora» sino **la que queda viva después de aplicarlo**. El nombre y el formato se conservan para no romper al front de F3-10.
- **Ajuste por diferencia de cambio (F3-07, §3.10.2):** cierra el pendiente que dejó un cambio que no dividía exacto. **El importe lo calcula el servicio** (es el pendiente real) y solo lo acepta si cabe en la tolerancia; **no se pide** porque, si quien firma eligiera el importe, el mismo botón serviría para tapar un saldo de cualquier tamaño. **Sin tolerancia configurada el ajuste está desactivado** y sale en 409 diciendo que la fije el administrador en *Configuración → Ajustes generales* (§4.2): §3.10.2 marca esos importes como [Supuesto], y un ajuste que se permite sin que nadie haya escrito una cifra es una pérdida de dinero silenciosa. Con `pendiente > tolerancia` el 409 dice el importe, la tolerancia y las dos salidas de §3.10.2: que el cliente pague la diferencia en un cobro nuevo, o que se corrija el cambio del cobro anterior. Va con `cobro.registrar` —mueve el mismo saldo que el cobro— y **no** con un permiso nuevo.
- **Retención (F3-04, §3.9, D14):** es **una línea de factura más**, de tipo `Retencion` y en negativo, pero **fuera del subtotal**: los totales son `Subtotal = Σ Importe` de las líneas que no son retención, `Itbis = Σ ItbisImporte`, `Total = Subtotal + Itbis`, `Retencion = −Σ Importe` de las líneas de retención (columna en positivo) y `NetoACobrar = Total − Retencion`. La razón de que no entre en el subtotal es que **retener no baja lo facturado**: lo que baja es el neto a cobrar. Si entrara, el cliente vería una factura más corta en vez de una factura con un netting en medio, y el total ya emitido no cuadraría con el papel que se le entregó.
  - **El importe lo recalcula `RecalcularTotales` en dos pasadas**: primero suma las líneas de servicio y calcula subtotal, ITBIS y total; después aplica a cada línea de retención su porcentaje sobre esa base y vuelve a sumar. En una sola pasada la retención no tendría base, porque la base depende de las líneas y las líneas incluyen la retención. Por eso el importe de una línea de retención **no se congela** —a diferencia del porcentaje y la base, que sí—: cambia cada vez que se toca cualquier otra línea, y si no se recalculara, añadir una línea de renta después de retener dejaría el neto mal sin que nada se enterara.
  - **La base la decide el tipo configurado** (`tipo_retencion.base`), no el servicio: `CalculoRetencion.Importe(base, subtotal, itbis, total, porcentaje)` no sabe cuál es la base correcta, solo la aplica. **El porcentaje y la base se congelan en la línea** (`porcentaje_retencion`, `base_retencion`), igual que el ITBIS de F3-02: la factura dice "esto lleva esta retención sobre esta base" aunque el tipo cambie mañana.
  - **Solo a clientes agente de retención** (`cliente.agente_retencion`): una línea de retención a un cliente que no lo es se rechaza con **409** y un mensaje que dice dónde marcarlo. No se ignora ni se quita, porque aceptarla en silencio dejaría una factura que dice retener a un cliente al que no se le puede retener.
  - **`cobrado` no incluye la retención.** La retención cierra el pendiente pero no es dinero que entró en caja, igual que el ajuste de F3-07. Ver la nota de F3-10 más arriba.
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
| `GET /api/v1/facturas/{id}` | `factura.ver` | Factura con sus líneas y con `cobrado`, `pendiente`, `estadoCobro` y `vencida` derivados de los cobros (§3.8, F3-06). Desde F3-04 también `retencion`, `netoACobrar` y `retencionSinComprobante` (§5 y §6.5). |
| `POST /api/v1/facturas` | `factura.borrador` | Alta en borrador. Sin número y sin cambio: los dos los pone la emisión. Acepta `Idempotency-Key`. |
| `PUT /api/v1/facturas/{id}` | `factura.borrador` | Edita la cabecera. 409 si ya no está en borrador. Acepta `Idempotency-Key`. |
| `POST /api/v1/facturas/{id}/lineas` | `factura.borrador` | Alta de línea; recalcula los totales. `cantidad` × `precioUnitario` y el ITBIS los calcula la aplicación. Con `tipo = Retencion` hace falta `tipoRetencionId` y **el precio se ignora**: el importe sale del porcentaje sobre la base (§3.9, F3-04), y solo la admite un cliente agente de retención. Acepta `Idempotency-Key`. |
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

### 6.2.1 Facturación periódica y factura de reserva (F3-05)

| Endpoint | Acción | Notas |
|---|---|---|
| `POST /api/v1/facturas/periodicas/simular` | `factura.emitir` | Lo que se factura de un mes natural, **sin escribir nada**: contrato, periodo, días facturables, importe y prorrateo aplicado, vencimiento, serie, comprobante y los motivos de omitirse. Cuerpo `{ periodo, serieId, contratosIds? }`; `contratosIds` ausente = todos los candidatos. |
| `POST /api/v1/facturas/periodicas/generar` | `factura.emitir` | Emite esas facturas por el mismo camino de F3-02 (número, cambio congelado, comprobante y retención si el cliente es agente). Lo saltado se devuelve con su motivo en vez de fallar. Acepta `Idempotency-Key`. |
| `POST /api/v1/reservas/{id}/factura` | `factura.emitir` | Factura de una reserva corta (§3.4 «se cobra antes»): importe, moneda y vencimiento son los de la reserva, y el cuerpo solo lleva la serie. Devuelve el aviso del anticipo; **no crea cobros** (D10). 409 si la reserva ya tiene factura. Acepta `Idempotency-Key`. |

- **La serie es obligatoria y única por ejecución**, y no hay serie «de la casa»: §3.7 deja la
  numeración en la serie. Un contrato cuya moneda no admite la serie aparece **omitido con su
  motivo** en la simulación y en la ejecución; no es un 409 a mitad del lote.
- **El prorrateo** es `renta / días del mes × días facturables`, con días **inclusivos**, y el mes
  entero paga la renta íntegra sin dividir (dividir y multiplicar por los mismos días no siempre
  vuelve a dar la renta exacta en `decimal`).
- **El vencimiento** es el día de pago del contrato dentro del periodo facturado, recortado al final
  del mes; si ese día ya pasó al facturar, vence en el ciclo siguiente (el mismo día del mes
  siguiente). Con más de un mes de retraso la factura nace vencida: eso no lo arregla ninguna regla
  escrita todavía.
- **Los dos endpoints piden `factura.emitir`**, y la simulación también: enseña los importes, los
  vencimientos y la serie y el comprobante de facturas que van a existir.
- **Ejecutar sin haber simulado es legal y visible**: la auditoría de la ejecución
  (`factura.periodicas.generadas`) registra si los contratos venían de una lista —lo que se vio— o
  se facturaron todos los candidatos sin mirar.

### 6.2.2 Ciclo de vida de la factura (F3-08)

| Endpoint | Acción | Notas |
|---|---|---|
| `PUT /api/v1/facturas/{id}/modificar` | `factura.anular` | Cambia **solo líneas** de una factura emitida: `{ lineas: [{ lineaId, tipo, concepto, cantidad, precioUnitario, tipoItbisId?, tipoRetencionId? }] }`. Recalcula subtotal, ITBIS, retención, total y neto con las mismas funciones de dominio de F3-02/F3-04. **409** si hay comprobante fiscal, si está anulada o convertida, o si la línea no es suya. Acepta `Idempotency-Key`. |
| `POST /api/v1/facturas/{id}/simular-anulacion` | `factura.anular` | Lo que la anulación haría, **sin escribir nada**: los cobros que se anularían enteros y los que solo dejarían de pagar esta factura, con sus importes. Es la pantalla de confirmación de §3.8. |
| `POST /api/v1/facturas/{id}/anular` | `factura.anular` | Cuerpo `{ motivo }` **obligatorio** (400 sin él). En una sola transacción: factura `Anulada`, número `Anulado` en `numero_serie` (el contador no retrocede, D28), comprobante fiscal `Anulado`, asignaciones de cobro sin vigor y —los que se quedan sin ninguna— cobro `Anulado` con el motivo derivado. **409** si ya está anulada, si es borrador o si es una proforma convertida. Acepta `Idempotency-Key`. |
| `POST /api/v1/facturas/{id}/duplicar` | `factura.anular` | Copia cliente, unidad, contrato, serie, moneda, líneas, ITBIS y retenciones **al borrador**; no copia número, comprobante, cobros, envíos, retención recibida, referencia de e-factura ni la proforma de origen. Devuelve la copia y `similares`: facturas del mismo contrato, periodo y concepto que ya existían. **No bloquea** por eso. **409** si el original es periódico (límite conocido, más abajo). |
| `POST /api/v1/facturas/{id}/convertir-proforma` | `factura.anular` | Cuerpo `{ serieId? }`. La factura sale **emitida** con la misma moneda y los mismos importes (D7) y la proforma queda `Convertida`, todo en un commit. La serie destino por defecto es la primera fiscal activa (la 3 si está sembrada) y se valida: tiene que ser fiscal y estar activa, y no puede ser la de la proforma. |
| `POST /api/v1/facturas/{id}/rehacer-moneda` | `factura.anular` | Cuerpo `{ moneda, serieId, total, motivo }`, con `motivo` y `total` obligatorios. **Anula la original** (que conserva su número y su moneda) y crea un borrador en la otra moneda con el importe **tecleado**: el tipo de cambio con el que se rehace lo elige quien lo teclea. Acepta `Idempotency-Key`. |

- **Una factura anulada no es una deuda.** `CalculoCobro` recibe el estado del documento y devuelve
  pendiente cero con `estadoCobro = "Anulada"`; sin eso, el aging sumaría dinero que el cliente no debe
  por un documento que ya no vale.
- **Los cobros se tocan uno a uno, no en bloque.** Un cobro con otras facturas vivas sigue vigente y solo
  pierde la asignación de la factura anulada; solo se anula si se queda sin ninguna asignación viva, y
  entonces su excedente sale y su crédito vuelve al `saldo_a_favor` del contrato (§3.10.1).
- **Las seis piden `factura.anular`, no `factura.emitir`.** Es el permiso que §2 da exactamente a este
  trabajo («modificar o anular una factura ya emitida») y estaba sembrado desde F1-09 sin que ninguna
  tarea lo usara: emitir es sacar número y comprobante, que es otra cosa y ya tiene su propio permiso.
  Se comprobó que el techo por rol ya estaba sembrado (admin y gerente), así que el cambio fue solo el
  atributo y el servicio: ninguna migración.
- **Límite conocido: duplicar una factura periódica da 409.** Su clave `(contrato, periodo, concepto)`
  es única mientras no esté anulada (`ix_factura_periodo_unico`), así que la copia chocaría con ella
  misma y la base no deja ni intentarlo. Las salidas son **anular la original** —que libera la clave—
  y emitir la correcta, o pedir la copia **con otro periodo**, que ya es otro documento a propósito.
  - `TODO(A#)`: §3.8 dice que duplicar avisa de lo que ya existe «sin impedir la duplicación», lo que es
    cierto para las facturas tecleadas a mano y **no puede ser cierto para las periódicas**. Hay que
    decidir si duplicar una periódica debe **ofrecer cambiar el periodo de la copia** en lugar de
    rechazar, y si eso lo propone la aplicación o lo teclea quien duplica. No se resuelve aquí porque es
    una regla de negocio, no un fallo de implementación.

### 6.2.3 Documento PDF de factura y proforma (F4-01)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/facturas/{id}/pdf` | `factura.ver` | PDF en memoria (`application/pdf`, `factura-{id}.pdf`) con el diseño de `docs/DESIGN.md §7`. 404 si no existe. No lleva `Idempotency-Key` ni `Mutar`: no muta nada. |

- **El compositor va partido en dos: `Modelo` y `Generar`.** El texto de un PDF de QuestPDF no se
  puede leer del archivo —incrusta las fuentes con subconjunto y escribe los caracteres como índices de
  glifo—, así que `DocumentoFacturaPdf.Modelo` decide y formatea todo lo que el documento dice en
  strings, sin tocar la librería, y `DocumentoFacturaPdf.Generar` solo lo pinta. Las pruebas afirman
  sobre el modelo; del archivo solo comprueban que es un PDF, que se sirve como tal y que dos
  generaciones dan los mismos bytes.
- **Los totales no se recalculan en el documento.** Salen de los datos ya calculados del dominio
  (`Factura.Subtotal/Itbis/Total/Retencion/NetoACobrar`) y, si hay cobros, de `Dominio/CalculoCobro`:
  lo que dice el papel y lo que dice la pantalla son el mismo número. Sin cobros no hay filas de
  pagado ni pendiente.
- **Una factura anulada se descarga**, con la marca de agua «ANULADA». El papel marcado es lo que
  necesita el expediente del cliente; lo que no se puede es *emitir* una anulada, y eso lo sigue
  llevando `POST /emitir`.
- **La empresa de la cabecera es la del contrato** y, sin contrato, la de la unidad
  (`unidad.facturadora_por_defecto`). Sin ninguna de las dos, el bloque sale entero del documento: es
  preferible a imprimir la de otra factura. `TODO(A#)` en `ServicioFacturas.FacturadoraAsync`, por si
  el responsable decide otro orden.
- **El texto de usuario no se interpreta nunca.** Conceptos, nombre del cliente y notas salen con los
  elementos de texto de la librería, que no ejecutan marcado ni HTML ni plantillas
  (`docs/SEGURIDAD.md`, SEC-001). La regla la vigila `InterpretacionEnPdfEnCodigoTests`, que falla si
  algún compositor de `Services/` llama a `Html(`, `Svg(`, `.ParseHtml` o `MinifyHtml`.
- `TODO(A#)`: las fuentes del design system (Geist y Geist Mono) siguen sin estar en el repositorio
  (`docs/DESIGN.md §8`), así que el documento sale con la tipografía que trae la librería y el número
  no va en mono. Es el mismo pendiente que tienen los PDF de cotización y de contrato.

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
- **El recibo en PDF es `GET /api/v1/cobros/{id}/pdf`** (F4-01, §3.10.1). Es opcional y lo pide quien
  registra el cobro; no se genera solo. Sin `Idempotency-Key` ni `Mutar`: es lectura. 404 si el cobro no
  existe y 409 si la plantilla del catálogo no está.

### 6.3.1 Recibo de cobro en PDF (F4-01)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/cobros/{id}/pdf` | `factura.ver` | PDF en memoria (`application/pdf`, `recibo-{id}.pdf`) con el diseño de `docs/DESIGN.md §7`. 404 si el cobro no existe. No lleva `Idempotency-Key` ni `Mutar`: no muta nada. |

- **El recibo lo pide quien registra el cobro y es opcional** (§3.10.1). No se genera al registrar el
  cobro: un papel de dinero que aparece solo es un papel que alguien puede imprimir y archivar sin
  haberlo pedido.
- **El compositor va partido en dos: `Modelo` y `Generar`**, igual que el de la factura
  (`DocumentoReciboPdf`): el texto de un PDF de QuestPDF no se puede leer del archivo, así que el modelo
  decide y formatea lo que el papel dice y el render solo lo pinta. Las pruebas afirman sobre el modelo;
  del archivo solo comprueban que es un PDF, que se sirve como tal y que dos generaciones dan los mismos
  bytes.
- **El número es `REC-XXXXXXXX`**: los ocho primeros caracteres del identificador del cobro en
  mayúsculas. **Un cobro no tiene número** (`Data/Cobro.cs`) y la especificación no le pone ninguno
  (§3.10.1 solo dice «recibo PDF opcional»), así que **no hay secuencia ni tabla nueva** para numerar
  recibos. Lo que hace falta es que el mismo cobro reimprimido diga lo mismo, porque es un documento que
  la gente archiva. `TODO(A#)`: si el responsable quiere una numeración propia («REC-000123» con su
  contador), el sitio es `DocumentoReciboPdf.Identificador` y una secuencia transaccional como la de
  facturas (§3.7). Un identificador derivado no es un número de documento y no se presenta como tal.
- **Los datos de §3.10.2 salen cuando el cobro se registró en otra moneda**: la tabla de asignaciones
  crece con las columnas «Recibido» y «Cambio» de cada línea —cada asignación guarda los dos importes y
  el cambio aplicado— y los totales van por moneda, sin convertir entre ellas (§6). El tipo de cambio
  **del día** va en los datos del cobro, con su fecha (D9); el **aplicado**, en cada línea, que es donde
  §3.10.2 dice que se guarda.
- **Un cobro anulado se descarga**, con la marca de agua «ANULADO» y el motivo al pie, y **sus
  asignaciones salen con su marca** («— anulada»), igual que en `GET /api/v1/cobros/{id}`: lo que deja de
  contar no se esconde (AGENTS.md §6). Los totales **suman solo lo que vigora**.
- **La empresa de la cabecera es la del contrato del cobro** (`Contrato.Facturadora`). Sin contrato, el
  bloque sale entero del documento: es preferible a imprimir la de otro cobro.
- **El texto de usuario no se interpreta nunca** (SEC-001): la referencia, el nombre del cliente y el
  concepto salen con los elementos de texto de la librería, que no ejecutan marcado ni HTML ni
  plantillas. La regla la vigila `InterpretacionEnPdfEnCodigoTests`.
- `TODO(A#)`: las fuentes del design system (Geist y Geist Mono) siguen sin estar en el repositorio
  (`docs/DESIGN.md §8`), así que el documento sale con la tipografía que trae la librería. Es el mismo
  pendiente que los demás PDF.
- **Método de servicio para F4-05** (el trabajo de email): `ServicioCobros.PdfAsync(Guid id,
  CancellationToken ct)` → `Task<byte[]>`. Devuelve los bytes, sin escribir nada en disco.

### 6.4 Comprobante fiscal (F3-03)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/config/numeradores-fiscales` | `configuracion.editar` | Lista paginada de los numeradores; orden por defecto **por números que quedan**, de menos a más, que es como se revisa la pantalla. `?sortBy=restantes\|vencimiento\|actualizado`. |
| `GET /api/v1/config/numeradores-fiscales/{serieId}` | `configuracion.editar` | El numerador de una serie. 404 si esa serie no lo tiene, que es el estado en el que está la serie 3 recién migrada. |
| `PUT /api/v1/config/numeradores-fiscales/{serieId}` | `configuracion.editar` | Crea o cambia el numerador de la serie. Cuerpo `{ tipoPrefijo, rangoDesde, rangoHasta, fechaVencimiento, siguienteNumero, umbralAlerta }`. Un solo endpoint para las dos cosas porque son la misma decisión. |
| `PUT /api/v1/facturas/{id}/referencia-efactura` | `factura.emitir` | Referencia de la e-factura externa (D5). Cuerpo `{ referencia }`; vacía la borra. Devuelve la factura entera. |

- **El numerador es configuración, no un recurso de negocio**: lo escribe el administrador con el rango
  que le autoriza el organismo de los comprobantes. **No hay ninguno sembrado** —ni en la migración ni
  en la serie 3—, porque un rango escrito en el código sería una autorización fiscal inventada por la
  aplicación (D14 y el criterio de aceptación de `docs/PLAN.md`).
- **Sin numerador no se emite.** Una serie fiscal cuyo numerador no existe, está vencido o se agotó
  devuelve 409 con el motivo y **sin gastar número de factura**. No es una degradación: una factura de
  serie fiscal sin comprobante es un documento que no se puede declarar (§3.7).
- **`siguienteNumero` puede quedar una posición fuera del rango** (`rangoHasta + 1`) y eso es lo que
  declara agotado un rango ya gastado. Dos posiciones o más ya no se pueden volver a guardar.
- **El tipo se copia a cada comprobante emitido.** Si mañana el administrador cambia `tipoPrefijo`, los
  comprobantes que ya salieron siguen diciendo el que tenían: un documento no cambia de tipo porque
  alguien reconfiguró el rango.
- **La auditoría del numerador lleva el valor anterior y el nuevo** (`numerador_fiscal.creado` /
  `numerador_fiscal.actualizado`), y la de la emisión lleva el número de comprobante junto al de la
  factura (`factura.emitida`), porque los dos salen en la misma transacción.
- **La referencia de e-factura usa `factura.emitir` y no un permiso nuevo**: es lo mismo que emitir —
  tocar el documento fiscal de una factura—, y tocar la matriz de permisos por un campo de texto sería
  desproporcionado. **Sin validación ni lógica** (D5): es un texto que luego se imprime en el PDF.
  Se puede cambiar mientras la factura no esté anulada; en una anulada es 409, porque lo que salió no se
  reescribe.
- **Anular el comprobante es de F3-08.** El estado `Anulado` y las columnas `anulado_at`, `anulado_por` y
  `motivo_anulacion` ya existen con su restricción (anulado = con las tres rellenadas), y
  `comprobante_fiscal` **no concede `DELETE`**: un comprobante emitido no se borra nunca (D28).

### 6.5 Comprobante de retención (F3-04)

| Endpoint | Acción | Notas |
|---|---|---|
| `PUT /api/v1/comprobantes-retencion/{id}/recibir` | `factura.emitir` | Cuerpo `{ numero, fechaRecepcion }`. Anota la recepción del papel y **resuelve** el aviso vivo, en la misma transacción. 409 si ya se había recibido. Acepta `Idempotency-Key`. |

- **No hay endpoint de lectura.** El estado sale en la factura como `retencionSinComprobante`, que es lo
  que la pantalla necesita para saber si tiene que pedir el papel; el detalle completo no lo lee nadie
  todavía (F3-10).
- **La factura expone `retencion`, `netoACobrar` y `retencionSinComprobante`** (§5). Los dos primeros
  son columnas; el tercero es derivado y se resuelve con **una consulta agregada** sobre la página,
  igual que el cobrado de F3-06. El indicador sale también en la respuesta de la emisión cuando la
  factura retiene, porque el comprobante nace pendiente en esa misma transacción.
- **Recibir no es validar.** El número se guarda tal cual llega: no hay conexión con la DGII y §3.9 no
  pide más (D6, D14). Lo que sí hace es cerrar el aviso, y **un papel que no se guardó es un papel que
  no ha llegado**, así que la resolución va dentro de la misma transacción que la recepción.
- **Usa `factura.emitir` y no un permiso nuevo**: anotar lo que el cliente entregó es gestionar un
  documento ya emitido, igual que la referencia de e-factura. El empleado no lo tiene y recibe 403, con
  el intento en la auditoría.
- **No se reescribe lo recibido.** Un segundo `recibir` es 409 que dice cuál se había recibido: si el
  cliente entrega otro documento, lo que hay que anular es la factura (F3-08).
- **`comprobante_retencion` no concede `DELETE`**, igual que `comprobante_fiscal`: es la prueba de que se
  retuvo lo que dijo retener.

### 6.6 Fianzas (F3-09)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/fianzas` | `factura.ver` | Listado paginado, filtros `contratoId` y `moneda`. Cada fila trae exigido, número de fianzas, moneda, cobrado, devuelto, retenido, saldo y estado. Es lo que consume F3-10. |
| `GET /api/v1/fianzas/{id}` | `factura.ver` | La fianza con sus devoluciones. |
| `POST /api/v1/fianzas` | `fianza.mover` | Cuerpo `{ contratoId, importeExigido, moneda, numeroFianzas }`. 409 si el contrato ya tiene fianza en esa moneda. Acepta `Idempotency-Key`. |
| `POST /api/v1/fianzas/{id}/devolver` | `fianza.mover` | Cuerpo `{ importe, importeDescontado, motivo, fecha }`. Acepta `Idempotency-Key`. |
| `POST /api/v1/fianzas/{id}/retener` | `fianza.mover` | Sin cuerpo. Deja la decisión y su fecha. |
| `POST /api/v1/fianzas/{id}/liquidar` | `fianza.mover` | Sin cuerpo. Deja la decisión y su fecha. |

- **No hay endpoint para cobrar una fianza.** Cobrar es `POST /api/v1/cobros` con una asignación
  `{ fianzaId, importe }` en vez de `{ facturaId, importe }` (§3.10.1, §3.11): el dinero entra por la
  misma puerta y sale por los informes de caja (F5) por un solo camino.
- **`cobro_asignacion.factura_id` pasa a nulo y nace `fianza_id` nulo**, con
  `CHECK ck_cobro_asignacion_destino_unico` —`(factura_id IS NULL) <> (fianza_id IS NULL)`, exactamente
  uno— y **dos índices únicos parciales**, `ux_cobro_asignacion_cobro_factura` y
  `ux_cobro_asignacion_cobro_fianza`, filtrados a `<destino> IS NOT NULL AND vigente`.
  Sin el `CHECK`, una asignación sin destino no aplicaría nada y alguien la leería como dinero cobrado
  sin saber a qué; y una con los dos cerraría dos cuentas con el mismo céntimo.
  **La unicidad es por (cobro, destino) y no por destino**: dos cobros distintos pueden pagar la misma
  factura —terminarla a medias con el segundo es el caso normal de §3.10.1— y de lo mismo una fianza se
  puede ir cobrando por partes; lo que no puede repetirse es la misma línea dos veces en el mismo cobro.
  **El filtro incluye `vigente`** porque anular un cobro y volver a cobrar lo mismo es el camino de
  §3.10.2 para corregir un cambio equivocado, y la fila anulada no se borra (AGENTS.md §6): con la
  unicidad sobre todas las filas, esa segunda salida sería un 409 de «clave duplicada».
  El índice simple por factura se conserva, porque es el que resuelve la suma de lo cobrado de una
  página sin N+1 (§3.13).
- **Una asignación a fianza no es deuda de factura.** `CobradoPorFacturaAsync` filtra por
  `FacturaId != null`, así que el pendiente y el estado de cobro de ninguna factura se enteran, y la
  FIFO no propone fianzas: quien paga una fianza la señala.
- **`cobrado` y `saldo` no son columnas.** Salen de `cobro_asignacion` y `fianza_devolucion` con tres
  consultas agregadas para toda la página, igual que el `cobrado` de las facturas en F3-06. Un importe
  guardado al lado del movimiento que lo produce se desincroniza en cuanto el movimiento cambia.
- **`estado` guarda la decisión, no el estado visible.** Solo se escriben `Retenida`, `EnLiquidacion`,
  `Devuelta` y `RetenidaParcial`; `Pendiente` y `Parcial` se derivan de lo cobrado en `CalculoFianza`.
- **El saldo se muestra acotado a cero.** Anular el cobro que pagó una fianza (§3.10.1) deja de contar lo
  cobrado y, si parte ya se había devuelto, la resta daría negativa; un saldo negativo no es un saldo. El
  desajuste real sigue a la vista en el cobro anulado.
- **No se puede filtrar ni ordenar por estado** en el listado: es derivado, y filtrar en la base solo
  dejaría filtrar por la mitad que es columna, con filas que entran en un estado y se leen en otro.
- **Devolver más de lo cobrado es 409 con el saldo y lo pedido**; una devolución parcial **sin importe
  descontado o sin motivo es 400**, que es el criterio de aceptación de la tarea.
- **`fianza` y `fianza_devolucion` no conceden `DELETE`**, y `fianza` es única por `(contrato_id, moneda)`.

### 6.7 Vista única de cuentas y cobros (F3-10)

La pantalla principal del día a día (§3.13): **una sola lista paginada con facturas, proformas y
fianzas**, con una insignia de tipo en cada fila, y los totales al pie **por moneda**.

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/cuentas` | `factura.ver` | Filtros `anio`, `serieId`, `moneda`, `estado`, `estadoCobro`, `clienteId`, `unidadId`, `vencidas`, `modificadas`, `incluirProformas` (por defecto **false**) e `incluirFianzas` (por defecto **true**). Paginación obligatoria; solo se admite `sortBy=fecha`, con `sortDir` (por defecto ascendente, como el resto de la API). |
| `POST /api/v1/cobros/simular` | `cobro.registrar` | Además de lo de F3-06, acepta `facturasSeleccionadas` (lista de `facturaId`). Con ellas la propuesta es para esas facturas y **no propone ninguna otra**; sin ellas sigue siendo la FIFO. |

#### Columnas de la fila y de dónde sale cada una

| Columna | Factura | Proforma | Fianza |
|---|---|---|---|
| `tipo` | `Factura` | `Proforma` | `Fianza` |
| `id` | id de la factura | id de la factura | id de la fianza |
| `serieId`, `serieNumero`, `numero` | serie y número del documento | ídem | `null`: una fianza no tiene serie |
| `comprobanteTipo`, `comprobanteNumero` | `comprobante_fiscal` (F3-03); `null` si la serie no lo genera | `null` (nunca hay comprobante) | `null` |
| `fecha` | `fecha_emision` | `fecha_emision` | `CreatedAt` de la fianza, en zona de negocio: es la fecha del alta |
| `clienteId`, `cliente` | los de la factura | ídem | los del **contrato** de la fianza |
| `unidadId`, `unidad` | los de la factura | ídem | los del contrato de la fianza |
| `contratoId` | el de la factura o `null` | ídem | el de la fianza |
| `concepto` | el de la factura | ídem | `Fianza de N fianzas` |
| `moneda` | la de la factura | ídem | la de la fianza |
| `total` | `Redondeo.Importe(total)` | ídem | **importe exigido**, que es lo que se pidió |
| `retencion` | `Redondeo.Importe(retencion)` | `null` | `null`: una fianza no retiene |
| `cobrado` | `CalculoCobro` (F3-06) | `null` | `CifrasAsync` de F3-09 |
| `pendiente` | `CalculoCobro`: `neto − cobrado − retenciones − ajustado` | `null`: no genera deuda | **exigido − cobrado**, lo que falta por exigir (`CalculoFianza.PendienteDeCobro`) |
| `saldo_a_devolver` | `null` | `null` | `cobrado − devuelto − descontado` (`CalculoFianza.Saldo`) |
| `vencimiento`, `dias_vencida`, `vencida` | de la factura | `null` | `null`: una fianza no vence |
| `estado` | estado de **cobro** (`CalculoCobro`), y el del documento en una proforma | estado del documento | estado de la fianza (`CalculoFianza.Estado`) |
| `modificada` | `modificada_at` (F3-08) | ídem | `null` |
| `tiene_reserva_con_anticipo` | `true` si su reserva tiene anticipo anotado (§3.13) | `false` | `false` |
| `estado_envio` | `null` hasta que exista F4-01 | `null` | `null` |
| `cobrable` | pendiente > 0 y no anulada | `false`: un presupuesto no se cobra | pendiente > 0 y estado cobrable |

- **Lo que no aplica viene en `null` y no en cero.** Cero es un dato («esta factura está pagada») y un
  cero donde no hay dato se lee como una deuda que no existe o como una retención que no se retuvo.
- **`pending` y `saldo_a_devolver` de una fianza son dos preguntas distintas**: lo pendiente se pide y el
  saldo se devuelve. Una fianza recién dada de alta tiene pendiente > 0 y saldo 0.
- **`tiene_reserva_con_anticipo` se resuelve por el concepto, no por una clave ajena.** F3-05 no dejó
  columna: el concepto de la factura de una reserva corta es `Reserva corta {reservaId}` porque es lo
  único que la distingue (no tiene contrato). `TODO(A#)`: si el icono tiene que ser un dato, la salida
  es `factura.reserva_id`; hasta entonces, una factura tecleada a mano con ese concepto y un
  identificador que exista en `reserva` mostraría el icono sin ser su factura.

#### Filtros: los que existen y los que no

| Filtro | Estado | Notas |
|---|---|---|
| `anio` | **Sí** | Año de la fecha de emisión; en las fianzas, el del alta. |
| `serieId` | **Sí** | Solo facturas: una fianza no tiene serie. |
| `moneda` | **Sí** | Aplica a los dos tipos. |
| `estado` | **Sí** | Estado del **documento** (`EstadoFactura`), que es columna. |
| `estadoCobro` | **Sí** | Estado derivado (`Pendiente`, `Parcial`, `Pagada`, `Anulada`), filtrado en la base con la cuenta de `CalculoCobro` como subconsulta correlacionada. |
| `clienteId`, `unidadId` | **Sí** | En las fianzas, los del contrato. |
| `vencidas` | **Sí** | Pendiente > 0 y vencimiento pasado. El vencimiento del mismo día no está vencido (§3.8). |
| `modificadas` | **Sí** | `modificada_at IS NOT NULL`. |
| `incluirProformas` | **Sí** | Por defecto `false`: §3.13 pone las proformas «con filtro». |
| `incluirFianzas` | **Sí** | Por defecto `true`: en esta vista son filas normales de dinero. |
| `noEnviadas` | **Sí** (F4-02) | **Nadie lo intentó**: no hay ninguna fila en `envio` para ese documento. |
| `envioFallido` | **Sí** (F4-02) | **Se intentó y no salió**: hay al menos un envío `Fallido` y ninguno con estado distinto de `Fallido`. |
| `modificadasTrasEnvio` | **Sí** (F4-02) | `factura.modificada_at IS NOT NULL` y hay un envío que salió con `documento_modificado_at IS NULL` (cuando salió, aún no se había tocado) **o** con `factura.modificada_at > envio.documento_modificado_at`. |

- **Los tres de envío no se solapan a propósito.** «No enviada» y «envío fallido» son preguntas
  distintas —«¿se le llegó a mandar?» y «¿se intentó?»— y si la primera significara «no llegó a salir»
  serían la misma lista con dos nombres.
- **Un envío `Fallido` nunca hace de documento «enviado»**, ni para `estado_envio` ni para
  `modificadasTrasEnvio`: no llegó a haber un papel en manos del cliente que después se pudiera cambiar.
- **`estado_envio` sale real**: es el estado del **último** envío del documento (por `enviado_at` y, a
  igualdad, por identificador), o `null` si nunca se mandó. Antes de F4-02 era un `null` constante; F3-10
  lo dejó así y escribió por qué.
- **`modificadaTrasEnvio`** sale en la fila como `modificadaTrasEnvio`, con la misma condición que el
  filtro y no con otra parecida: si la marca de la fila y el filtro no dijeran lo mismo, uno de los dos
  estaría mintiendo y no habría forma de saber cuál.

- **Un filtro que una fianza no puede expresar la deja fuera** (`serieId`, `estado`, `estadoCobro`,
  `vencidas`, `modificadas`, `noEnviadas`, `envioFallido`, `modificadasTrasEnvio`): devolverla «por si
  acaso» sería una fila que el filtro no tocó. Una fianza no es un documento que se mande, así que su
  `estado_envio` y su `modificadaTrasEnvio` son `null`.
- **Los totales se calculan sobre todo el conjunto filtrado**, no sobre la página, y con la misma cuenta
  que las filas, para que el pie cuadre con lo que hay arriba.
- **Paginación por mezcla.** Cada tipo se trae hasta el final de la página pedida (`page × pageSize`) y
  la ventana se corta de la unión. Traer solo `pageSize` de cada uno perdería filas: una fianza puede
  desplazar la última factura de la página 2 a la 3, y entonces no saldría nunca. La clave de orden es
  fecha y, a igualdad de fecha, identificador, que es lo que hace la paginación estable.

#### Totales

- Vienen en `totales` **por moneda**, cada moneda sumada consigo misma y **sin convertir**: consolidar
  en dólares es de F5-01 con el tipo de cambio congelado de cada documento (D9). Cuatro sumas por
  moneda: `total`, `retencion`, `cobrado` y `pendiente`; los nulos suman cero, así que el pie cuadra con
  lo que se ve en pantalla.
- **El permiso es `informe.ver` (gerente y administrador). Para el empleado `totales` es `null`**, no un
  objeto con ceros: cero es un total que se puede sumar, imprimir y archivar. Es el criterio de
  aceptación de la tarea.
- El `saldo_a_devolver` de las fianzas **no se suma** en el pie: es dinero que se devuelve, no deuda.

### 6.7.1 Estado de cuenta del cliente en PDF (F4-01)

| Endpoint | Acción | Notas |
|---|---|---|
| `GET /api/v1/clientes/{id}/estado-de-cuenta.pdf` | `informe.ver` | PDF en memoria (`application/pdf`, `estado-de-cuenta-{id}.pdf`) con el diseño de `docs/DESIGN.md §7`. `?anio=` es opcional y es el mismo filtro que la vista. 404 si el cliente no existe. No lleva `Idempotency-Key` ni `Mutar`: no muta nada. |

- **Las filas del estado de cuenta son las de la vista única** (`ServicioCuentas.ListarAsync`), no una
  consulta propia. §3.15 exige que los informes cuadren con la vista, y un segundo cálculo de pendiente
  es exactamente cómo se rompe eso: dos fórmulas que un día dicen cosas distintas. **Los totales se suman
  sobre esas filas** —la misma cuenta que hace una persona al pie de la tabla— y **no se usa el bloque
  `totales` de la respuesta**, que sale de `IPolicyService.Puede` y en un trabajo de Hangfire, sin
  sesión, saldría `null`.
- **El permiso es `informe.ver` y no `factura.ver`.** El documento lleva totales agregados de dinero de
  un cliente, y AGENTS.md §6 dice que el empleado no los recibe por ningún endpoint: el empleado recibe
  un 403 y el intento queda auditado.
- **El servicio también comprueba el permiso**, con `IPolicyService.PuedeAsync`, y no solo el atributo
  del endpoint: es lo que permite que F4-05 lo llame desde un trabajo, sin sesión HTTP ni filtro. El
  intento denegado se audita antes de lanzar.
- **El detalle está topado a 500 filas** (`ServicioCuentas.TopeFilasEstadoCuenta`) y **el recorte se dice
  en el papel**: el documento avisa de cuántas filas muestra de cuántas hay, y los totales son los de las
  mostradas. Un papel que recorta en silencio es peor que uno que no existe. `TODO(A#)`: si el
  responsable lo pide, la salida es un anexo con el resto del año o partir por periodo.
- **La cabecera no lleva empresa facturadora**: un cliente puede tener contratos con facturadoras
  distintas, así que el bloque sale vacío antes que imprimir la de cualquiera de ellos.
- **El compositor va partido en dos: `Modelo` y `Generar`**, igual que el de la factura
  (`DocumentoEstadoCuentaPdf`): el texto de un PDF de QuestPDF no se puede leer del archivo.
- **Método de servicio para F4-05** (el trabajo de email): `ServicioCuentas.PdfAsync(Guid clienteId,
  int? anio, Guid usuario, CancellationToken ct)` → `Task<byte[]>`. El `usuario` es al que se le comprueba
  `informe.ver`: en un trabajo no hay sesión HTTP de la que sacarlo.

### 6.8 Envío de documentos por correo y plantillas (F4-02)

| Endpoint | Acción | Notas |
|---|---|---|
| `POST /api/v1/envios` | `cotizacion.enviar` (ver nota) | Manda un documento a un destinatario y devuelve la fila del envío. Acepta `Idempotency-Key`. 404 si el documento no existe, **409 `API-005`** si no hay correo configurado, 409 si el documento ya se envió a ese destinatario y no viene `forzar`. |
| `GET /api/v1/envios` | `factura.ver` | Paginado y filtrable por `tipo`, `estado` y `documentoId`. `sortBy`: `enviadoAt` (por defecto), `createdAt`, `estado`. |
| `GET /api/v1/envios/{id}/pdf` | `factura.ver` (puerta de pelo grueso; la comprobación fina vive en el servicio, ver abajo) | Devuelve **`envio.pdf_copia`**, la columna, no una regeneración del documento. **403** si el contenido del adjunto no es del tipo de permiso que tiene quien pregunta. |
| `GET /api/v1/config/plantillas-mensaje` | `configuracion.editar` | Listado, opcionalmente por `tipo`. |
| `PUT /api/v1/config/plantillas-mensaje` | `plantilla.editar` | Guarda por (tipo, idioma): crea si no existe y edita si existe. No hay `DELETE`: una plantilla se desactiva. |

- **El atributo de `POST /api/v1/envios` es `cotizacion.enviar` y el envío sale con `documento.enviar`,
  y no es una errata.** §2 reparte los dos permisos y hay que elegir el más alto de los dos como puerta
  del endpoint, porque un atributo no puede mirar qué tipo de documento trae el cuerpo.
  `cotizacion.enviar` es el más alto (los dos roles que traen `documento.enviar` lo traen también), así
  que el atributo no le cierra la puerta a nadie que la matriz le abra. El permiso exacto lo comprueba
  `ServicioEnvios` con `IPolicyService` contra el usuario que se le pase, y es la misma comprobación que
  necesitan los trabajos de Hangfire, que no tienen sesión HTTP.
- **`GET /api/v1/envios/{id}/pdf` devuelve la copia guardada y no vuelve a componer el documento.** Si
  regenerara, el papel cambiaría en cuanto cambiara el documento y dejaría de ser la copia de lo que el
  cliente recibió. Es también el motivo de que `envio.pdf_copia` guarde los bytes exactos y no una
  referencia a ellos.
- **El permiso del adjunto se decide por su contenido, no por su tipo de envío.** Es el mapa
  `ServicioEnvios.AccionesDeContenido(tipo)`, y se exige en los tres caminos —`EnviarAsync`,
  `EnviarPendienteAsync` y `CopiaAsync`— con `IPolicyService` contra el usuario de la petición o del
  trabajo. El criterio es el de §2 y AGENTS.md §6 (**lo que lleva totales agregados exige
  `informe.ver`**):

  | Tipo de envío | Acción de envío (¿puede mandarlo?) | Acción de contenido (¿puede verlo?) |
  |---|---|---|
  | Factura, proforma, recibo | `documento.enviar` | `factura.ver` |
  | Cotización | `cotizacion.enviar` | `factura.ver` (es la lectura de la cotización: no hay acción propia en §2) |
  | Contrato | `documento.enviar` | `cliente.ver` (§2, «ver clientes, unidades, reservas y contratos») |
  | **Estado de cuenta** | `documento.enviar` | **`informe.ver`** |

  - **Por qué el atributo del endpoint no puede hacer esto**: `[Accion]` solo ve el verbo y la ruta. El
    tipo de documento está en el cuerpo y el contenido del adjunto no está en ninguna parte de la
    petición; para `GET /{id}/pdf` la ruta solo lleva un identificador, y el contenido del archivo
    cambia de una fila a otra. Por eso el atributo se queda como puerta de pelo grueso y la
    comprobación fina vive en `ServicioEnvios`. **Esto no es teórico**: con el criterio anterior, la
    copia de un estado de cuenta —que *es* el estado de cuenta, con filas, totales por moneda y saldo
    pendiente— se descargaba con `factura.ver`, que el empleado sí tiene (§2). Ver BUG-014.
  - **Por qué también en el camino de envío**: `documento.enviar` lo tienen gerente y administrador, y
    quien lo tiene puede reenviar el archivo a quien quiera; ese archivo vuelve a entrar por la
    descarga. Si «puede mandarse» y «puede verse» fueran la misma pregunta, un permiso de envío sería
    un permiso de informe. Los recordatorios de W5 llevan el estado de cuenta adjunto y caen en la
    misma fila de la tabla.
  - Con los roles actuales la comprobación del envío solo cambia algo por el **recorte por usuario**
    (`usuario_permiso`, D17): un gerente al que le apaguen `informe.ver` sigue mandando facturas y
    deja de poder mandar o reenviar un estado de cuenta.
- **`GET /api/v1/envios` (el listado) se queda con `factura.ver` y no aplica el mapa.** La fila que
  devuelve es metadato —a quién, cuándo, en qué estado, con qué nombre de archivo— y ningún importe,
  así que no deja pasar ningún total agregado. Lo que sí muestra es el hecho de que se mandó un estado
  de cuenta y a quién. Cierre de BUG-014, punto (a) del riesgo que queda: es un hecho que la matriz no
  le concede al empleado y sigue abierto.
- **Los estados que esta sección describe y nadie escribe todavía.** `Pendiente` lo pone el envío masivo de
  F4-03 al encolar, y `Enviado` el trabajo de la cola. **`Entregado` y `Leido` no los escribe nadie**: no hay
  ninguna respuesta de confirmación del servidor de correo que los traiga, y poner «entregado» en el momento
  del `Send` sería una afirmación que nadie ha comprobado. Quedan pendientes del webhook de confirmación del
  proveedor o del enlace de seguimiento, con su `TODO(A#)` en `Data/Envio.cs`.
- **Un fallo del SMTP deja la fila en `Fallido` con su motivo, y el motivo no lleva credenciales.**
  Mismo criterio que F3-01 (§8.0.1): `ServicioEnvios.MotivoDe` copia el mensaje solo de
  `CorreoNoConfiguradoException` y de cualquier otra excepción solo el tipo, porque el mensaje de una
  excepción de SMTP puede traer el host, la respuesta del servidor o la cadena de conexión.
- **El antidiplicado de §3.14 solo mira los envíos que salieron.** Un `Fallido` no bloquea nada: si
  bloqueara, reintentar un correo que falló por un problema del servidor obligaría a forzarlo cada vez, y
  forzar es lo que hay que reservar para el reenvío que alguien ha pedido dos veces a propósito.
- **La llamada al servidor de correo va fuera de la transacción** a propósito: esperar a un servidor
  ajeno con la transacción abierta mantiene bloqueadas las filas del documento que ya se está mandando.
  Lo que sí va en la misma transacción es la fila de `envio` y su auditoría (AGENTS.md §6).
- **Sin correo configurado el envío es un 409 `API-005` que dice qué escribir**, con los nombres de las
  variables. Es el criterio que fijó BUG-007: un ajuste que se permite sin que nadie haya escrito un
  valor es una pérdida silenciosa, y un «Pendiente» en la fila afirmaría que hay un envío en la cola que
  no existe. La clave sale de variables de entorno o de `dotnet user-secrets` y **no hay ninguna columna
  donde escribirla** (`docs/SEGURIDAD.md` §A.7).
- **El correo va en texto plano.** Es la misma regla que los PDF por código y no por HTML y navegador
  (SEC-001): el texto del cliente va literal, sin escapar, y un correo en texto plano no puede ejecutar
  nada. La sustitución de las variables de la plantilla es textual y no evalúa nada.

#### Tablas que añade

| Tabla | Qué guarda | Notas |
|---|---|---|
| `envio` | Un correo con su documento adjunto y **la copia exacta del PDF que salió** | El documento se apunta con una referencia lógica (`tipo_documento` + `documento_id` + `documento_anio`), **sin clave foránea**: un envío apunta a facturas, estados de cuenta, recibos, cotizaciones y contratos, que están en cuatro tablas distintas y el estado de cuenta no tiene fila propia. La contrapartida es que nada impide dejar un envío apuntando a algo borrado, que no importa: un envío no se borra nunca y un documento tampoco (D28). `TODO(A#)`: si algún día hiciera falta integridad referencial real, la salida es una tabla por tipo con su clave foránea y una vista por encima |
| `plantilla_mensaje` | Asunto y cuerpo por tipo de mensaje e idioma | Índice único en `(tipo, idioma)`. Nada se borra: se desactiva. |
| `recordatorio` | **La decisión de avisar** a un cliente y una unidad un día, no el correo (F4-05) | No cabe en `envio` porque no es un documento —no tiene número ni archivo— y su clave de deduplicación es `(cliente_id, unidad_id, fecha)`, no la de un documento. Único en esa terna con **`NULLS NOT DISTINCT`** (escrito a mano en la migración, porque `unidad_id` puede ser nula y en PostgreSQL los nulos de un único no chocan entre sí). Guarda a qué envío dio lugar, el motivo si se omitió y la trazabilidad; **no guarda importes**, porque los dice el cuerpo del envío y van en su `pdf_copia`. Se concede con `SELECT, INSERT, UPDATE` y **sin `DELETE`**: un recordatorio es la explicación de por qué a un cliente se le escribió o no se le escribió un día dado. |

- `envio` se concede a `rent_app` con `SELECT, INSERT, UPDATE` y **sin `DELETE`**, como todas las de F1 en
  adelante (AGENTS.md §6). La migración siembra las **cinco plantillas en español** de §3.14 con
  identificadores fijos: sin plantilla no hay envío, y el identificador fijo hace que la migración sea
  idempotente.
- **Un idioma sin plantilla no es un error.** Sale la de «es» y la fila guarda el idioma que se usó de
  verdad, no el que se pidió sin existir. Bloquear el correo porque nadie escribió la versión en francés
  dejaría al cliente sin factura.
- `documento_modificado_at` guarda el `modificada_at` que el documento tenía **en el momento del envío**,
  y es lo que permite saber si se tocó después (F3-08). Sale `null` en documentos que no tienen esa
  columna (recibos, contratos) y en facturas que aún no se habían tocado cuando salieron.

#### Método de servicio para W4 (envío masivo) y W5 (recordatorios)

Un trabajo de Hangfire encola un envío de un documento a un destinatario con:

```csharp
Task<EnvioFila> ServicioEnvios.EnviarAsync(
    EnvioPeticion peticion,      // TipoDocumento, DocumentoId, y destino por ContactoClienteId, Email o ClienteId
    Guid usuario,                // a quién se le comprueba el permiso: el de envío (documento.enviar / cotizacion.enviar) y el del contenido del adjunto
    string ip,
    MutacionIdempotente? mutacion,   // null si el trabajo no lleva Idempotency-Key
    CancellationToken ct);
```

- No hay sesión HTTP de la que sacar nada: el `usuario` va en la firma, igual que en
  `ServicioCuentas.PdfAsync`. Un trabajo de recordatorio usa un usuario de servicio con los permisos de
  envío.
- **Un envío `Fallido` se reintenta sin forzar**: el antidiplicado solo mira los envíos que salieron y
  `intentos` suma el fallo. La fila a reintentar se busca con `estado = 'Fallido'`, y el reintento es
  otra llamada a `EnviarAsync` con la misma `EnvioPeticion`.
- **Envío masivo**: para encolar, `POST /api/v1/envios` es de uno en uno a propósito —cada envío compone
  su PDF, su texto y su copia, y `forzar` y el antidiplicado son de un documento y un destinatario. El
  trabajo recorre los documentos filtrados y llama a `EnviarAsync` por cada destinatario; `Pendiente` y
  la escritura previa de la fila son de esa tarea, no de este servicio.

### 6.9 Envío masivo (F4-03)

Los cuatro endpoints, todos con el permiso `mensajeria.masivo` (§2: gerente y administrador) y todos con
`Idempotency-Key`:

| Endpoint | Qué hace |
|---|---|
| `POST /api/v1/envios/masivo/preparar` | Cuenta qué entraría, enseña la vista previa y **no manda nada** |
| `POST /api/v1/envios/masivo/confirmar` | Vuelve a calcular el lote, escribe las filas `Pendiente` y **encola** un trabajo por fila |
| `GET /api/v1/envios/masivo/{loteId}` | Total, enviados, fallidos, pendientes y el detalle de los fallidos |
| `POST /api/v1/envios/masivo/{loteId}/reintentar` | Vuelve a encolar **solo los fallidos** |

#### Decisiones que sostienen la pantalla

- **Los documentos salen de la vista única.** El cuerpo es `{ filtro: FiltroCuentas, idioma, forzar }` y
  `FiltroCuentas` es el mismo tipo que devuelve `GET /api/v1/cuentas`; el lote se arma con
  `ServicioCuentas.ListarAsync`. **No hay un filtro paralelo**: un segundo «qué facturas hay» es el
  sitio donde una pantalla y un correo dejan de hablar de lo mismo.
- **Confirmar vuelve a calcular el lote.** No reutiliza el resumen anterior, aunque el cliente lo mande
  de vuelta: el resumen es para leerlo, y lo que sale es lo que se calcula en la confirmación. Un lote
  confirmado sobre datos que alguien pudo editar entre las dos llamadas sería un lote que nadie miró.
- **La vista previa es una promesa.** El `asunto` y el `cuerpo` del resumen llevan las variables ya
  sustituidas y son **exactamente** los que se escriben en la fila del envío; salen de
  `VariablesMensaje`, el mismo sustitutor que usa el envío suelto. La vista previa **no** compone el PDF:
  encolar mil PDFs en la pantalla costaría el doble que mandarlos, y lo compone el trabajo, en el mismo
  orden, con el mismo `ModeloPdfAsync`.
- **Cada exclusión lleva su motivo, y son disjuntos** (`MotivoExclusionEnvio`): `YaEnviado` (salvo
  `forzar`, y solo mira los envíos que salieron), `SinDireccionCorreo`, `SinContactoElegible`,
  `FacturaAnulada`, `SinPlantilla`, `NoGeneraDocumento` (una fianza es una fila de dinero, no un papel) y
  `SuperaElTopeDelLote`. **Se informa del primero que se encuentra y no se acumulan quejas**: un lote que
  se recorta sin decir qué se quedó fuera se parece a un lote entero que funcionó.
- **El destino es el del envío suelto, en el mismo orden**: el correo del cliente; si no lo tiene, el
  contacto principal de §3.2 («el que se usa si no se elige otro»); si no hay principal, el único
  contacto con casilla. **Con varias casillas y ningún principal no se elige uno: se excluye.** Mandar la
  factura al primero de la lista sería mandarla a alguien que nadie eligió.
- **Un idioma sin plantilla excluye en lote, no en envío suelto.** F4-02 decidió que en un envío suelto
  no es un error y sale en «es» (§6.8); en un lote de cientos de correos eso mandaría a todos un idioma
  que nadie pidió, así que se excluye y se dice cuál falta. Los dos criterios están escritos en el enum.

#### El lote no es una tabla

- `envio.lote_id uuid` anulable, con su índice. **El lote es el conjunto de las filas que comparten ese
  identificador**: el progreso, el recuento y el reintento son agregaciones sobre esa columna.
- Lo que hay que guardar del lote —quién lo pidió, con qué filtros y cuándo lo confirmó— lo escribe la
  auditoría `envio.masivo.confirmado` (`entidad = "lote"`, `entidad_id = <uuid>`), que es append-only y
  ya existe para eso (AGENTS.md §6). Una tabla de lotes sería una segunda fuente de lo mismo, y el repo
  ya pagó por ese problema en BUG-007.
- El reintento escribe `envio.masivo.reintentado` con la lista de envíos que vuelve a poner en la cola.

#### Los estados que esta tarea pone en uso

- `Pendiente` lo escribe la confirmación; `Enviado`, el trabajo. `Entregado` y `Leido` **siguen sin
  escribir**: SMTP no dice si un mensaje llegó a su destinatario, solo que el servidor lo aceptó, y poner
  «entregado» ahí sería una afirmación que nadie ha comprobado. Quedan pendientes del webhook de
  confirmación del proveedor o del enlace de seguimiento. Su `TODO(A#)` sigue escrito en `Data/Envio.cs`.
- Mientras el envío está `Pendiente`, `pdf_copia` y `nombre_archivo` van **vacíos**: todavía no ha salido
  nada. La copia es la del intento que salga, y el reintento **reutiliza esa copia** en vez de regenerarla,
  porque es el archivo que ya se mandó y lo que queda como prueba.
- `intentos` empieza en `0` en una fila encolada y sube en el trabajo, antes de llamar al servidor: si la
  llamada se cae a mitad, el contador refleja lo que pasó de verdad.

#### Ventanas conocidas

- Si el proceso se cae **entre el commit y el encolado** quedan filas `Pendiente` que nadie manda. La
  cola se crea después del commit a propósito (encolar dentro de la transacción haría que un trabajo
  encontrara filas a medio escribir, y que un trabajo encolado en una transacción deshecha mandara
  correo que ya no está en ninguna parte). El cierre es un trabajo de barrido que re-encole lo
  `Pendiente` más viejo de una hora, con su `TODO(A#)`.
- El resumen lee `ModeloPdfAsync` una vez por documento: es lo que garantiza que la vista previa y el
  correo dicen lo mismo, y se paga en tiempo de la pantalla de confirmación.

### 6.10 Recordatorios (F4-05)

Los tres endpoints de los avisos y el de la pausa, con el permiso que corresponde:

| Endpoint | Permiso | Qué hace |
|---|---|---|
| `GET /api/v1/recordatorios` | `mensajeria.masivo` | Avisos filtrables por fecha, cliente, unidad y estado, con el envío al que dio lugar |
| `GET /api/v1/recordatorios/{id}` | `mensajeria.masivo` | Un aviso con su envío |
| `POST /api/v1/recordatorios/{id}/enviar` | `mensajeria.masivo` + `Idempotency-Key` | Confirma el envío de un aviso del modo manual |
| `PUT /api/v1/clientes/{id}/recordatorios-pausados` | `cliente.editar` | Pausa o levanta los avisos de ese cliente |

#### Decisiones que sostienen la pantalla

- **La lista y el envío usan `mensajeria.masivo`, no `factura.ver`.** Los avisos dicen qué debe cada
  cliente y a cuál no se le avisó, y eso es información de cobranza (§2: gerente y administrador). La
  pausa usa `cliente.editar` porque es un atributo del cliente, no una operación de mensajería.
- **El `POST .../enviar` devuelve 200 con el aviso en `Omitido`** si mientras tanto se pagó o si tiene
  la pausa puesta. No es un error: es el resultado que se pidió, que es no molestar a quien ya pagó. El
  409 queda para lo que sí es un conflicto (mandar dos veces el mismo aviso, mandar uno sin envío).
- **`recordatorio` no lleva importes.** La fila y su respuesta no muestran cifras: lo que se le dice al
  cliente está escrito en el cuerpo del envío y lo que se le adjunta en su `pdf_copia`. Una tercera
  copia de la misma cifra es una que se puede desincronizar y que nadie lee.
- **El aviso lo compone `VariablesMensaje.DeRecordatorio`**: `{cliente}`, `{pendiente}`,
  `{vencimiento}` y `{unidad}`, y **ni `{total}` ni `{numero}`**, porque §3.14 con pago parcial pide
  mencionar solo lo que falta. `{unidad}` lleva delante «Unidad:» y va vacío si el cliente no tiene
  contrato con unidad, para que la línea desaparezca en vez de decir «pendiente de 1.000,00 USD en».
- **El pendiente se dice por moneda** y sin convertir: un cliente y una unidad pueden tener facturas
  en USD y en DOP, y sumarlas sería inventar un tipo de cambio (§3.12).
- **La regla de a quién se manda es una sola.** `DestinatarioEnvio` la comparten el envío masivo de
  F4-03 y el recordatorio, y `PendienteDeFactura` la comparten la vista de cuentas y el barrido: dos
  sitios donde escribir la misma decisión de negocio son dos sitios que un día dejan de decir lo mismo.

#### La alerta de envíos fallidos que faltaba (F4-05)

§3.16 enumera «envíos fallidos» entre las alertas del sistema y hasta F4-05 no había ninguna: un envío
que no salía se quedaba en su fila con su `ultimo_error` y nadie se enteraba salvo quien abriera esa
fila. Ahora `ServicioEnvios` levanta, por los dos caminos de envío, una alerta de tipo `envio` con clave
**estable por envío** (`envio:{envioId}:fallido`) y **sin fecha**, porque el asunto es «este envío falló»
y no «hoy falló un envío»: reintentar el mismo documento mañana suma repeticiones en la misma fila en vez
de llenar la bandeja de copias. El motivo que se copia es el que ya vive en `envio.ultimo_error`, que
por F3-01 es el mensaje de una excepción escrita para leerse o el tipo de cualquier otra: **una alerta se
lee fuera del sistema** y no puede llevar ni una credencial ni una cadena de conexión. Al mandar el mismo
envío otra vez con éxito la alerta **no se resuelve sola**: se resuelve quien mira la bandeja, que es el
camino que ya usan las alertas de F2-10.

#### Método de servicio para W5 (recordatorios)

Un trabajo de Hangfire que mande el estado de cuenta de un cliente, que ya es un destinatario concreto,
va por la misma fila que el envío masivo, no por una copia:

```csharp
// 1. La fila: se escribe una vez y el trabajo la manda; el reintento sube `intentos` en esa misma fila.
Task<EstadoEnvio> ServicioEnvios.EnviarPendienteAsync(
    Guid envioId,          // la fila de `envio` que hay que mandar
    Guid usuario,          // a quién se le comprueba documento.enviar (no hay sesión HTTP)
    string ip,
    CancellationToken ct);

// 2. La cola: el trabajo se llama a sí mismo con la fila, el usuario y la IP que quedan en la auditoría.
void IColaEnvios.EncolarEnvio(Guid envioId, Guid usuario, string ip);
```

- **No compongas el documento en el trabajo.** `EnviarPendienteAsync` compone el PDF solo si la fila no
  tiene copia, y manda la copia si la tiene. Para el estado de cuenta eso significa: componerlo **una
  vez**, en el endpoint que deja la fila `Pendiente`, y dejar que el trabajo mande esa copia. Regenerarlo
  en el trabajo daría bytes distintos de los que anunciarían los que ya salieron.
- **Repite el antidiplicado de §6.8**: mira `envio.tipo_documento`, `documento_id`, `documento_anio` y
  `destinatario_email` de las filas con `estado <> 'Fallido'`, y salta con `forzar`. El estado de cuenta
  no tiene fila propia: es una composición de documentos, así que la referencia lógica del envío apunta
  al cliente y al año.
- **Agrupa y cuenta por `lote_id`, no por tu propia tabla.** Si los recordatorios necesitan un
  «lote», la columna es la misma y el `GET /api/v1/envios/masivo/{loteId}` ya sabe contarlo.

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
  - `subempresa` va **sin RNC sembrado**: el cifrado AES-256-GCM llega con F1-09 y la columna con F4-01; el administrador lo configura cuando lo tiene.
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
| Avisos del numerador de comprobante (§3.7) | F3-03 | Hangfire | diario 06:00 AST | clave estable por (serie, condición) |
| Avisos de comprobante de retención (§3.9) | F3-04 | Hangfire | diario 06:00 AST | clave estable por factura |
| Tipo de cambio | F3-01 | Hangfire | diario 05:00 AST ⚠️ | `UNIQUE (fecha, moneda)` |
| Recordatorios (§3.14, D12) | F4-05 | Hangfire | diario 09:00 AST | por (cliente, unidad, fecha) en `recordatorio` |
| Alertas de vencimiento de factura | *previsto* (F3) | Hangfire | cada hora | dedupe por (tipo, entidad) |
| Envíos (§3.14) | F4-03 | Hangfire (**cola**) | al confirmar un lote | por `envio.id`: la fila ya enviada no se vuelve a mandar |
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

### 8.0.2 Reglas del trabajo de envío (F4-03)
- **No es un trabajo recurrente.** `ProgramacionJobs` solo programa lo que se repite por reloj y su propio
  comentario dice que los envíos no se inventan ahí: un trabajo aparece porque alguien ha pedido mandar
  algo, no porque sea la hora. Por eso se encola con `IBackgroundJobClient.Enqueue` desde
  `ServicioEnviosMasivos.ConfirmarAsync` y **no está en `ProgramacionJobs.Todos`**.
- **Un trabajo por envío, no uno por lote.** Un lote son cientos de correos y un trabajo que los recorre
  se pierde entero si se corta a mitad; uno por documento se reintenta solo y el resto del lote sigue.
  `TrabajoEnvio.EjecutarAsync(envioId, usuario, ip, ct)` es una clase fina que solo dispara a
  `ServicioEnvios.EnviarPendienteAsync`, para que las pruebas la llamen sin planificador.
- **`Fallido` no es una excepción.** El trabajo no lanza cuando el correo no sale: el fallo es un dato
  del lote que se consulta con `GET /api/v1/envios/masivo/{loteId}` y se reintenta con una llamada
  expresa. Si el trabajo fallara, Hangfire lo reintentaría por su cuenta, sin que nadie lo pidiera, que
  es justo lo que §3.14 deja en la persona que mira el progreso.
- **Idempotencia en la fila, no en el planificador**: una fila ya `Enviado` no se vuelve a mandar
  aunque el trabajo se entregue dos veces.
- **`IColaEnvios` detrás de una interfaz**, y resuelta por fábrica: `AddHangfire` solo se registra con el
  servidor activo, así que una instancia sin planificador (la segunda, o las pruebas) recibe
  `ColaEnviosSinServidor`, que **falla al encolar** en vez de dejar el lote en «pendientes» para
  siempre. `IServicioCorreo` va detrás de una interfaz por el mismo motivo.

### 8.0.3 Reglas del trabajo de recordatorios (F4-05)
- **Una transacción por cliente y unidad**, y un fallo de una no tira las demás: veinte clientes a los
  que avisar y uno sin casilla de correo no pueden convertir los otros diecinueve avisos en nada.
- **La idempotencia se pregunta antes de escribir** y el único de `(cliente_id, unidad_id, fecha)` es la
  red. Preguntar evita la transacción que el índice iba a rechazar; el índice cubre la carrera entre
  dos ejecuciones.
- **Tras una violación del único se vacía el `ChangeTracker`.** EF deja en `Added` la fila que no pudo
  guardar, así que el siguiente `SaveChanges` del mismo barrido la vuelve a intentar y a fallar otra
  vez: un cliente que ya tenía aviso de hoy se llevaría por delante a todos los que sí tocaban. El
  `23505` se cuenta como omitido —«otra ejecución se adelantó»— y el trabajo sigue, igual que en F2.
- **Auditoría sin usuario de sesión**: `UsuarioId = null` y `Origen = "job"`, escrita dentro de la
  transacción antes del commit, igual que los trabajos de F2.
- **`Fallido` y `Omitido` no son una excepción.** Un cliente que ya pagó o que tiene la pausa puesta
  produce un aviso `Omitido` **con su motivo**: es un resultado, no un fallo, y en la pantalla de
  revisión explica por qué a ese cliente no se le escribió hoy en vez de dejar un hueco sin explicación.
- **El trabajo no lanza nunca.** Cada grupo va en su transacción y un fallo se cuenta y se registra; el
  trabajo diario se reintenta solo en Hangfire, y una excepción suelta dejaría clientes a medias entre
  dos avisos.
- **La comprobación del saldo y de la pausa está en el envío, no solo en la preparación**, y es la misma
  en el automático y en el `POST .../enviar` de la pantalla. Es la mitad del criterio de aceptación de
  §3.14 («se detienen al pagar») y la otra mitad es que la pausa también frena el envío manual.
- **La clase del trabajo es fina**: `TrabajoRecordatorios.EjecutarAsync` solo dispara a
  `ServicioRecordatorios.EjecutarAsync`, para que las pruebas lo llamen sin planificador.

### 8.1 Estado (F1-12)
Implementados y programados, con la tabla de arriba como fuente de verdad: **backup cifrado** (02:00), **verificación de integridad de auditoría** (03:00) y **vigilancia de la copia** (04:00), de F1-12; **aviso de reserva a 7 días** (05:00), de F2-03; **renovaciones e incrementos** (06:00), de F2-06; **fin de contrato por fecha efectiva** (07:00), de F2-09; **avisos de operación** (08:00), de F2-10; **tipo de cambio** (05:00), de F3-01; **avisos del numerador de comprobante** (06:00), de F3-03; **avisos de comprobante de retención** (06:00), de F3-04; **periodos sin facturar** (06:00), de F3-05; **fianzas sin devolver** (06:00), de F3-09; y **recordatorios de pago** (09:00), de F4-05. Sigue esperando el dominio que todavía no existe: alertas de vencimiento de factura (F3-10), outbox, limpieza y comprobación de actualizaciones. Los **envíos de §3.14 ya existen desde F4-03**, pero no como job programado: son una **cola** que se llena al confirmar un lote, y por eso no aparecen en `ProgramacionJobs.Todos` ni tienen hora (ver §8.0.2).

- **Avisos del numerador de comprobante (`TrabajoComprobantes`, 06:00 AST):** recorre los numeradores y
  levanta tres alertas de tipo `comprobante`, con clave estable por serie y condición —
  `numerador:{serieId}:pocos` (los números que quedan son ≤ `umbral_alerta`), `:vencido` (hoy ya pasó
  `fecha_vencimiento`) y `:agotado` (no queda ninguno) — y **resuelve cada una cuando deja de aplicar**.
 Escribe en `alerta`, así que cumple la misma regla que el fin por fecha efectiva y los avisos de
 operación: **detrás** de la copia de las 02:00, la integridad y la vigilancia, y **delante** del fin y
 de los avisos de operación, que siguen siendo los últimos. Comparte hora con la renovación de
 contratos, igual que hace el tipo de cambio con el aviso de reservas: son cortos e independientes, y
 a las 06:00 todavía queda la mañana por delante para lo que este aviso pide, que es **pedir un rango
 nuevo al organismo que autoriza los comprobantes**. Es persistente sin duplicar, como las de F2-09:
 la clave no lleva fecha porque el aviso no es «hoy quedan pocos» sino «quedan pocos». **«Agotado»
 suprime «pocos»**: cuando no queda ningún número, el aviso que exige una acción es el que dice que
 hay que pedir rango, y los dos juntos serían dos alertas para el mismo hecho.
 **Una alerta cerrada vuelve a levantarse si el problema regresa (BUG-008):** el único de `alerta` es
 **parcial** y va sobre `(tipo, clave)` filtrado a `WHERE resuelta_at IS NULL`, no sobre `clave` a secas.
Con el único de `clave`, una alerta cerrada bloqueaba su propia clave para siempre y el `ON CONFLICT`
 no insertaba nada ni la resucitaba: un numerador que se quedaba corto, se ampliaba y volvía a
 quedarse corto se quedaba sin avisar. Afectaba igual a las alertas de F2-09. La restricción solo tiene
 que regir las **vivas** —es lo único que no puede duplicarse— y lleva `tipo` delante porque la
 identidad de un fallo es «de qué tipo y con qué clave»: dos tipos que coinciden en la clave son dos
 hechos.

- **Periodos sin facturar (`TrabajoPeriodosSinFacturar`, 06:00 AST, F3-05):** recorre los contratos
  `Activo` y `BajaProgramada` cuya vigencia toca el **mes natural anterior** —no el que está en
  curso, que aún se factura— y levanta una alerta de tipo **`factura`** por cada uno que no tenga
  factura de ese periodo, con clave estable por contrato y periodo
  (`factura-sin-facturar:{contratoId}:{yyyy-MM}`). Tipo nuevo a propósito: los que ya había son de
  copia, reserva, contrato, comprobante y tipo de cambio, y §3.16 enumera los avisos que espera
  —este es uno—; reutilizar «contrato» metería un problema de facturación entre las bajas y los
  finales sin liquidar. Una factura **anulada no cuenta** como factura del periodo: el aviso tiene que
  seguir vivo. Se **resuelve al facturar** (dentro de la transacción que cierra la ejecución
  periódica), no aquí: quien sabe que la factura ha salido es el servicio que la emite. Comparte hora
  con los otros tres avisos de las 06:00 porque los cinco son cortos e independientes.
- **Avisos de comprobante de retención (`TrabajoRetenciones`, 06:00 AST, F3-04):** recorre los
  `comprobante_retencion` en estado `Pendiente` —que es exactamente lo que cubre su índice— y levanta
  una alerta de tipo `comprobante` por cada uno, con clave estable **por factura**
  (`retencion:{facturaId}:sin-comprobante`). Comparte tipo y hora con los avisos del numerador de F3-03
  porque son el mismo asunto: un comprobante que hay que pedir. **La clave no lleva fecha** porque el
  aviso no es «hoy falta el papel» sino «falta el papel», así que dos días seguidos refrescan la misma
  fila y suman repeticiones en vez de llenar la bandeja de copias. El mensaje lleva los días que lleva
  pendiente, en días de negocio, que es lo que dice si esto es una chase de hoy o una factura que nadie
  ha gestionado en semanas. **Se resuelve en la recepción, no aquí**: quien sabe que el papel llegó es
  quien lo anotó, y resolverlo aquí obligaría al trabajo a enterarse por su cuenta. Con el único parcial
  de BUG-008, una retención que vuelve a quedarse sin comprobante vuelve a levantar el aviso.
- **Fianzas sin devolver (`TrabajoFianzas`, 06:00 AST, F3-09):** recorre las fianzas **sin decisión
  guardada** —las que están en `Pendiente` o `Parcial`, que es la mitad de `estado_fianza` que es
  columna— cuyo contrato está en `Finalizado` o `Liquidado`, y levanta una alerta de tipo **`fianza`**
  por cada una, con clave estable **por fianza** (`fianza:{fianzaId}:sin-devolver`).
  **El tipo `fianza` es nuevo** y es lo que §3.16 pide cuando enumera «fianzas pendientes de devolver»:
  los que ya había no encajan. `contrato` es de contratos sin liquidar (F2-09) y su clave es el
  contrato, así que un contrato con dos fianzas y solo una sin devolver daría un aviso que señala el
  contrato y no el depósito. `comprobante` es de numeradores y retenciones (F3-03 y F3-04) y su clave es
  un papel que hay que pedir a alguien; aquí no hay papel, hay dinero ya cobrado. `factura` es de
  periodos sin facturar (F3-05) y su clave lleva un periodo natural.
  **La clave lleva la fianza y no el contrato ni la fecha** por lo mismo que en el resto: el aviso no es
  «hoy falta devolver» sino «falta devolver», así que dos días seguidos refrescan la misma fila y suman
  repeticiones. **Se resuelve en el movimiento, no aquí**: la devolución y la puesta en liquidación
  cierran el aviso en su propio commit, igual que la recepción de un comprobante cierra el suyo, porque
  quien sabe que el depósito vuelve a su dueño es el servicio que lo devuelve. Con el único parcial de
  BUG-008, un aviso resuelto antes de tiempo vuelve a levantarse si el asunto regresa.
  **Una fianza retenida a medias también cuenta como resuelta**: devolver en parte es devolver, y una
  fianza retenida a propósito por faltantes de inventario (§3.6) no es un olvido. Avisar de ella todos
  los días sería la forma de alerta que enseña a ignorar la bandeja.
  Comparte hora con los otros cuatro avisos de las 06:00 porque los cinco son cortos e independientes:
  leen fianzas, contratos, comprobantes y facturas y escriben en `alerta` con claves distintas.
- **Tipo de cambio (F3-01):** ExchangeRate-API, con `TipoCambio__Url` y `TipoCambio__ApiKey`
  opcionales; sin clave va al endpoint abierto y con clave al plan de pago, que devuelve la tabla en
  `conversion_rates` en vez de `rates` (el cliente entiende las dos). La columna `valor` es
  `numeric(18,6)` y se redondea con `Redondeo.TipoCambio`, que es la única razón de que el tipo de
  cambio no pase por `numeric(18,2)` de la convención global.
- **Recordatorios de pago (`TrabajoRecordatorios`, 09:00 AST, F4-05):** recorre las facturas
  `Emitida` **vencidas y con saldo pendiente**, las agrupa por **cliente y unidad** —§3.14 quiere «un
  mensaje por cliente y unidad», no uno por factura— y solo avisa a los grupos cuyo atraso da la
  vuelta exacta de los plazos: el primero a los `dias_primer_recordatorio` (5) del vencimiento y a
  partir de ahí cada `dias_entre_recordatorios` (15), o sea a los 5, 20 y 35 días. **Ni al 6 ni al 21**:
  un aviso diario en vez de uno cada quince no es «recordar más», es el correo que enseña a ignorar la
  bandeja. Los dos plazos **viven en `ajuste_general`** (§6 de AGENTS.md) y los siembra la migración; con
  ellos a `null` el trabajo **no avisa a nadie** y deja una alerta de tipo `recordatorio` con clave
  `recordatorio:plazos-sin-configurar` que dice qué escribir, porque un recordatorio con plazos sin
  decidir es ruido para el cliente (criterio de BUG-007). La alerta se **resuelve sola** en cuanto hay
  plazos.
  Va **la última del día a propósito**: un aviso que sale cuando la casa ya cerró se lee por la mañana
  con la cabeza del día entero, que es cuando se paga; y es el único trabajo que compone un PDF por
  cliente, así que quiere ir detrás de los que no tocan la red.
  **Salta a los clientes con `recordatorios_pausados`** y, cuando aun así toca avisar, **el saldo se
  vuelve a mirar en el momento de mandar**, no solo al preparar: una factura vencida que alguien paga
  entre el resumen y el clic no puede recibir un recordatorio. Si ya está pagada, el aviso queda
  `Omitido` con su motivo, que es un resultado y no un fallo. **La pausa también frena el envío manual**
  de uno en uno, porque quien la activa está diciendo que a ese cliente no se le avisa y un clic en
  «enviar» no es una excepción a eso.
  **El PDF del estado de cuenta se compone una vez**, en el camino que deja la fila `Pendiente`, y se
  guarda en `envio.pdf_copia`: mandar es mandar esa copia. Si se compusiera al enviar, dos generaciones
  del mismo documento —la que se revisó y la que sale— podrían diferir (mismo criterio que el envío
  masivo de F4-03).
  El modo decide: `Apagado` no hace nada, `Manual` deja las filas `Pendiente` sin enviar nada, y
  `Automatico` prepara y manda. El aviso automático lo firma **el último usuario que puso el modo**
  (`ajuste_general.updated_by`), porque es al que se le comprueba `informe.ver` para el PDF y
  `documento.enviar` para el envío (docs/DOC_TECNICA.md §6.7.1); si no hay ninguno, no se manda nada y
  se deja alerta.
  **Una transacción por cliente y unidad** y **un fallo de una no tira las demás**, como en las
  renovaciones y los fines de contrato. Ver §8.0.

- **Dónde:** `AtlasRent.Api/Jobs/`. El servidor de Hangfire arranca con la API; el panel **no** se monta (`SEGURIDAD.md` A.1).
- **Cadencia:** `ProgramacionJobs` convierte la hora de `TZ_NEGOCIO` al UTC que le corresponde, porque Hangfire 1.8 (edición gratuita) planifica en UTC y no entiende el prefijo `CRON_TZ=` de los crons con zona. Las 02:00 AST son las 06:00 UTC. Si algún día la zona pasa a tener horario de verano, `CronDiarioEnUtc` falla al arrancar en vez de desplazar la copia una hora seis meses al año.
- **Almacenamiento:** base de datos propia `atlas_rent_jobs`, esquema `hangfire` dentro de ella (Hangfire.PostgreSql 1.21 usa ese nombre por defecto y crea esquema y tablas solo, con `PrepareSchemaIfNecessary`). No es un capricho de aislamiento: `pg_dump` del backup diario recorre `atlas_rent` entera y hace `LOCK TABLE` sobre todo lo que encuentra, así que con las tablas de Hangfire dentro el volcado falla con `permission denied for schema hangfire` y **el backup diario deja de hacerse**. En su propia base, ningún backup la recorre. La credencial sí es la de migraciones (`ConnectionStrings__Migraciones`, rol `rent_migrador`) con la base cambiada en código; `rent_app` no conecta ahí ni tiene migración que le conceda nada.
- **Apagado:** `Jobs__ServidorActivo=false` apaga el planificador entero. Los tests de integración lo usan para que no haya ningún servidor de jobs real.
- **Integridad de auditoría:** corre con `app.rol = admin` dentro de una transacción, porque la política `auditoria_lectura` esconde las filas a cualquier otro rol y una lectura vacía daría un falso «todo correcto» sobre una cadena manipulada. `TrabajoIntegridadAuditoria` lo documenta en el sitio donde alguien lo vaya a «simplificar».
- **A mano:** `AtlasRent.Watchdog copiar` (mismo `ServicioBackup` y misma cadena `ConnectionStrings__Backup`, idempotente por fecha).
- **Fallo → alerta (F1-12):** `ServicioBackup` no se limita a escribir en el log. Cada fallo del día deja una fila en `alerta` con la clave `backup:{fecha}`, mediante `ServicioAlertas`, que hace `INSERT … ON CONFLICT DO UPDATE` sobre el índice único parcial de `alerta (tipo, clave) WHERE resuelta_at IS NULL`: el mismo fallo repetido suma a `repeticiones` en vez de llenar la tabla de filas idénticas, y una alerta ya cerrada vuelve a levantarse si el fallo se repite (BUG-008). El motivo que se copia a la alerta solo se traslada si la excepción es del propio servicio; una excepción inesperada puede llevar dentro la cadena de conexión, y la alerta es texto que se lee fuera, así que ahí solo va el tipo de excepción. La bandeja de la interfaz es de F2-10; la tabla es el registro, no la pantalla.
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
