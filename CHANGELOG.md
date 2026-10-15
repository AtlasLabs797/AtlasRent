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

## [15/10/2026 09:30] FEAT · F1-10 · Inmuebles, unidades, tarifas y documentos de propiedad
- Qué:
  - Migración `20261015053702_InmueblesUnidadesYDocumentos`: `inmueble`, `unidad`, `modalidad_unidad`, `tarifa_unidad` y `documento_inmueble`, con `GRANT` a `rent_app` y **RLS sobre `documento_inmueble`** (solo admin y gerente).
  - `Services/AlmacenDocumentos.cs`: los documentos se guardan **fuera del webroot** y se sirven por la API; la subida valida extensión **y** la **firma real** `%PDF-`; máximo **25 MB**.
  - `ServicioInmuebles`, `ServicioDocumentosInmueble` y `Controllers/InmueblesController` con `[Accion]`, paginación de §6 y auditoría.
  - 17 tests nuevos en `backend/AtlasRent.Tests/Inmuebles/`.
- Por qué: F1-10 del plan. Un título de propiedad es el documento más sensible del inmueble (SEC-004) y la app está en internet.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **249/249 en verde**. Revisé dos cosas que suelen fallar aquí y están bien:
  - **El nombre en disco es `Guid.NewGuid():N`**, no viene de la petición, y además `RutaDe()` normaliza con `Path.GetFullPath` y exige que la ruta empiece por la carpeta raíz. Un `../` no tiene dónde colarse. El nombre enviado queda solo como metadato para la cabecera de descarga.
  - **La validación del tipo real no rompe el archivo**: la cabecera leída se vuelve a escribir antes de copiar el resto.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (requiere secretos de `.env.dev`). Tampoco se probó la concurrencia de dos subidas simultáneas de la misma categoría: el índice único las rechaza con 409, pero eso no está probado.
- Migraciones: sí. `Migrations/20261015053702_InmueblesUnidadesYDocumentos`.
- Riesgo / pendiente:
  - **Bug real cazado por los tests**: el PDF se guardaba **sin su cabecera `%PDF-`**, porque la validación se había comido los primeros bytes. Es decir, **todos los documentos subidos estaban corruptos**. Corregido antes de dar la tarea por buena.
  - **Defensa en profundidad probada**: hay un test que consulta la base **saltándose la API**, con `app.rol = empleado`, y RLS devuelve **0 filas**. La segunda barrera funciona aunque el endpoint no aplique el permiso.
  - `Almacen__Documentos` **debe apuntar a una carpeta temporal en los tests**, nunca a `C:\AtlasRent\data`.
  - `TODO(A#)` y supuestos por confirmar con el responsable: **tamaño máximo 25 MB** (SEC-004 A.4 exige un límite pero no lo fija) y las **listas de `TipoInmueble` y `TipoUnidad`**, que la especificación no enumera.
  - Las lecturas de `documento_inmueble` van en **transacción explícita** con `app.rol`: sin transacción, la política vería el rol vacío y no saldría ninguna fila.
  - No implementado (no pedido): ocupación por fechas, borrado de documentos e `Idempotency-Key` en la subida.
- Commit/PR: `feat(inmuebles): inmuebles, unidades, tarifas y documentos de propiedad con RLS`.

## [15/10/2026 08:40] FEAT · F1-09 · Clientes con AES-256-GCM, índice ciego y cierre de SEC-005
- Qué:
  - `Services/Cifrado.cs`: `ServicioCifrado` (AES-256-GCM con versión de clave y **nonce aleatorio por cifrado**), `DocumentoIdentidad.Normalizar` e `IndiceCiegoDocumento` (HMAC-SHA256 con clave propia).
  - Clientes, contactos y prospectos bajo `/api/v1/clientes` y `/api/v1/prospectos`, con `[Accion]`, paginación de §6, `Idempotency-Key` y auditoría en la misma transacción.
  - Número de cliente por `SEQUENCE` iniciada en `max+1`, sin duplicados bajo concurrencia.
  - **SEC-005 cerrado**: `usuario.mfa_secreto` queda cifrado en reposo con el mismo servicio.
  - Migración `20261015050454_ClientesYProspectos` con `CREATE SEQUENCE cliente_numero_seq` y `USAGE, SELECT, UPDATE` sobre la secuencia (`setval` la exige).
  - 26 tests nuevos en `backend/AtlasRent.Tests/Clientes/`.
- Por qué: F1-09 del plan. D16 exige el documento de identidad visible completo pero cifrado en la base de datos, y la app está en internet.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **232/232 en verde**. Revisé que los tests **leen la fila cruda** y comprueban que el documento en claro no aparece; el del secreto MFA va más lejos: **descifra** el valor guardado con la clave real de la aplicación para demostrar que es un cifrado válido y no un texto cualquiera.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (requiere secretos de `.env.dev`); la migración sí aplica desde cero en cada ejecución de la suite.
- Migraciones: sí. `Migrations/20261015050454_ClientesYProspectos`.
- Riesgo / pendiente:
  - **⚠️ SEC-006, nuevo y abierto: `idempotencia` guarda el cuerpo completo de la respuesta de una mutación en JSON sin cifrar.** Cualquier dato personal que una mutación devuelva queda en claro en esa tabla, aunque el dato principal esté cifrado. Para F1-09 se esquivó **no devolviendo el documento en las respuestas de alta y edición** (solo en `GET /clientes/{id}`), pero el problema es de fondo y volverá con cualquier campo personal nuevo. **Decisión pendiente: cifrar el cuerpo de `idempotencia`, o guardar una versión redactada.** Anotado en SEGURIDAD.md.
  - **Normalización del documento**: mayúsculas invariantes, se eliminan espacios, guiones (incluidos U+2010/2013/2014) y puntos; se conservan letras y dígitos. Motivo: un documento es una identidad, no un texto, y el índice único solo sirve si "001-1234567-8", "001 12-34567.8" y "00112345678" son el mismo cliente. Las letras se conservan porque un pasaporte las tiene.
  - Formato almacenado: `v{version}:{nonceBase64}:{textoCifrado||etiquetaBase64}`. Nonce de 12 bytes aleatorio por cifrado, etiqueta de 16 bytes al final. La versión viaja dentro para poder rotar la clave.
  - **No verificado: rotación real de clave.** `ServicioCifrado` sube de versión sin recifrar, así que subir `Seguridad__CifradoVersion` deja los datos antiguos ilegibles. Es un `TODO(A#)` dangerous: cambiar la versión a ciegas inutiliza los documentos existentes.
  - Las tablas nuevas **no llevan RLS**, igual que las de F1-04 a F1-08; el aislamiento lo da `[Accion]`. RLS por tabla sería un paso más en la migración.
  - Dos bugs reales cazados por los tests y corregidos: las altas no fijaban `Activo` (ningún cliente aparecía en la lista) y en .NET 10 `AesGcm.Decrypt` ordena los parámetros distinto que `Encrypt`, lo que cruzaba nonce y etiqueta en silencio. Corregido y con argumentos con nombre.
  - `TODO(A#)`: nacionalidad, idioma y canal preferido (§3.2); conversión de prospecto a cliente (necesita cotizaciones); importación de Factusol.
- Commit/PR: `feat(clientes): cifrado AES-256-GCM, indice ciego, contactos y prospectos`.

## [15/10/2026 07:55] FEAT · F1-08 · Configuración: series, ITBIS, retenciones y ajustes, sin cifras fiscales en código
- Qué:
  - Migración `20261015042341_Configuracion`: `serie`, `numero_serie`, `tipo_itbis`, `tipo_retencion`, `forma_pago`, `cuenta_receptora`, `subempresa`, `ajuste_general` e `idempotencia`, con `GRANT` a `rent_app` y sin columnas `xmin`.
  - `Controllers/ConfigController.cs` bajo `/api/v1/config`, con `[Accion(Acciones.ConfiguracionEditar)]`, **auditoría en la misma transacción**, listas paginadas de §6 e **`Idempotency-Key`** en las mutaciones.
  - `ServicioSeries`: contador transaccional `UPDATE … RETURNING` con `UNIQUE (serie_id, numero)`; un número emitido no se reutiliza y una factura anulada no lo libera.
  - `Dominio/CalculoImpuestos`: ITBIS y retenciones (base subtotal / ITBIS / total) **sin ningún porcentaje propio**; los porcentajes entran como datos.
  - 19 tests nuevos en `backend/AtlasRent.Tests/Configuracion/`.
- Por qué: F1-08 del plan. D14 exige que las cifras fiscales sean datos editables por el admin, no constantes en el código.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **206/206 en verde**. Leí `CalculoImpuestos.cs` y confirmo que no contiene ninguna cifra fiscal: solo multiplica por los porcentajes que recibe. El test de concurrencia exige lo fuerte: 24 emisiones simultáneas dan **exactamente 1..24, sin repetidos y sin huecos**, no solo "que no se duplique".
- NO verificado: `dotnet ef database update` contra la base de desarrollo (requiere secretos de `.env.dev`); la migración sí aplica desde cero en cada ejecución de la suite.
- Migraciones: sí. `Migrations/20261015042341_Configuracion`.
- Riesgo / pendiente:
  - **El guard de D14 es mecánico**: un test recorre el fuente de la API, quita comentarios y cadenas y falla si aparece una cifra junto a un concepto fiscal. Se verificó **que falla** plantando `ItbisTemporalDePrueba = 18m` y `RetencionPaisTemporal = 10m`. "IVA" quedó fuera de la lista a propósito porque colisiona con "activar"/"desactivar".
  - **Tolerancias de diferencia de cambio nacen en `null`**, no en los valores `[Supuesto]` de la especificación (1,00 USD / 50,00 DOP): una cifra fiscal no debe ser un valor por defecto en el código. `TODO(A#)` para que el contador confirme los valores y qué hacer si no hay tolerancia fijada.
  - Los tipos de ITBIS y retención **no vienen sembrados**: sin filas, hasta que el administrador las cree. Igual para formas de pago, cuentas y subempresas. Las series sí se siembran (1, 2, 3, 4 y 9) por venir numeradas en §3.7.
  - Los **prefijos de serie no están en la especificación**: los pone el admin. `TODO(A#)`.
  - `Subempresa` va **sin RNC a propósito**: cifrado AES-256-GCM, en la tarea de cifrado.
  - Supuestos: la serie 9 (histórica) no suma a facturado ni a saldos, y cualquier serie no desactivada suma a ambos. `AnularNumeroAsync` existe como API de servicio pero sin endpoint, porque aún no hay facturas que la llamen.
  - "Genera comprobante" no debería poder cambiarse en una serie con facturas emitidas: comprobación pendiente en la tarea de facturas.
- Commit/PR: `feat(configuracion): series, ITBIS, retenciones y ajustes sin cifras fiscales en codigo`.

## [15/10/2026 07:15] FEAT · F1-07 · Roles, permisos, denegar por omisión y auditoría de denegaciones
- Qué:
  - Migración `20261015035827_RolesYPermisos`: tablas `rol`, `permiso_rol`, `usuario_rol` y `usuario_permiso`, con la matriz de `docs/ESPECIFICACION.md` §2 **sembrada como datos** (no como `switch` en el código) y conversión de `usuario.es_admin` al modelo de roles. Sin columnas `xmin` y con `GRANT` explícito a `rent_app`.
  - `backend/AtlasRent.Api/Authorization/`: `Acciones` (los identificadores de las 19 acciones), `MatrizPermisos`, `AccionAttribute`, `IPolicyService`, `PolicyService`, `PermisoDenegadoException` y `FiltroAccionGlobal`.
  - **Denegar por omisión**: un endpoint de controlador sin `[Accion]` se rechaza.
  - La denegación escribe auditoría firmada con resultado `denegado` (usuario, acción, recurso e IP).
  - Endpoints de gestión de usuarios y de **desbloqueo de IP y de cuenta** (solo admin), que era lo que faltaba desde F1-05.
  - `ServicioMfa` y `ServicioEmergencia` pasan a usar `IPolicyService`; `es_admin` desaparece. `app.rol` de RLS lleva ahora el rol real del usuario.
  - 83 tests nuevos en `backend/AtlasRent.Tests/Permisos/`.
- Por qué: F1-07 del plan. Es la bisagra: desbloquea F1-08, F1-09 y F1-10, que dejan de decidir por su cuenta a quién dejan pasar.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **187/187 en verde**. La matriz se comprueba **contra la base de datos real**, no contra una constante en memoria: 19 acciones × 3 roles, verificando si cada permiso está **dentro del techo** del rol. Revisé en el código que `cotizacion.enviar` para empleado aparece como `true` en el techo y que hay un test aparte que confirma que llega desactivada, que es lo que pide D15.
- NO verificado: la migración de datos sobre una base **con usuarios reales**: en el contenedor se aplica sobre una base vacía, así que el `INSERT … SELECT` de `es_admin` corrió con cero filas. En la base de desarrollo sí transformaría datos reales. Tampoco verificado con proxy delante (`X-Forwarded-For` sigue pendiente).
- Migraciones: sí. `Migrations/20261015035827_RolesYPermisos`.
- Riesgo / pendiente:
  - **Identificadores de acción** (los que usarán F1-08 a F1-10): `configuracion.editar`, `cliente.ver`, `cliente.editar`, `inmueble.documento`, `contrato.borrador`, `contrato.estado`, `factura.ver`, `factura.borrador`, `factura.emitir`, `factura.anular`, `cobro.registrar`, `fianza.mover`, `documento.enviar`, `cotizacion.enviar`, `mensajeria.masivo`, `plantilla.editar`, `informe.ver`, `auditoria.ver`, `ip.desbloquear`, `cuenta.desbloquear`. Técnicas, sembradas aparte: `sesion.ver`, `sesion.cerrar`, `mfa.obligatorio`, `seguridad.acceso_emergencia`.
  - La fila 6 de la matriz (aprobar/activar/activo/baja/liquidar) se mapea a **un** identificador porque la especificación les da el mismo alcance; la 19 se parte en **dos** (`ip.desbloquear` y `cuenta.desbloquear`).
  - **El filtro global es un filtro MVC**: cubre endpoints de controlador. Los endpoints mínimos de `/health` quedan fuera, como ya estaban.
  - `TODO(A#)`: el interruptor de configuración «MFA para gerente» de D4 no está; hoy "obligatorio" es un permiso por rol, y activarlo para el gerente sería una migración.
  - **Decisión de producto pendiente:** no se bloquea que un admin se quite a sí mismo `configuracion.editar`. Conviene confirmar si debe poder quedarse sin ese permiso o si hay que impedirlo.
- Commit/PR: `feat(autorizacion): roles, techo de permisos, denegar por omision y auditoria de denegaciones`.

## [15/10/2026 06:40] FEAT · F1-06 · MFA con TOTP, códigos de recuperación y acceso de emergencia
- Qué:
  - Migración `20261015031813_MfaYAccesoEmergencia`: `mfa_desafio`, `codigo_recuperacion` y `acceso_emergencia`, con `GRANT` explícito a `rent_app`.
  - Activación en dos pasos: se devuelve la URI `otpauth://` (el QR lo dibuja el frontend) y el MFA **solo** queda activo tras confirmar un código válido; mientras tanto el usuario entra con el factor pendiente marcado.
  - Login en dos pasos: contraseña correcta devuelve un **desafío** con token provisional opaco de **5 min**, de un solo uso, en cookie `atlas-rent-mfa` (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`). El TOTP incorrecto cuenta para el bloqueo de cuenta.
  - **10 códigos de recuperación** de un solo uso, alfabeto sin I/O/0/1, formato `XXXXX-XXXXX`, SHA-256 hasheados y mostrados una sola vez.
  - **Acceso de emergencia** con doble aprobación: hacen falta **dos administradores distintos**, el código vive 10 min y la sesión y la auditoría quedan marcadas como emergencia.
  - `Otp.NET` 1.4.1 añadido a `Directory.Packages.props`.
  - 23 tests nuevos en `backend/AtlasRent.Tests/Seguridad/`.
- Por qué: F1-06 del plan. D4 exige MFA para admin y el sistema necesita una vía para recuperar el control si el admin pierde su dispositivo.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **104/104 en verde**. Los siete escenarios del encargo tienen test propio, incluido el de aceptación (`AdminSinMfaNoPuedeHacerLoginNormal`).
- NO verificado: `dotnet ef database update` contra la base de desarrollo (usaría secretos de `.env.dev`); la migración sí aplica desde cero en cada ejecución de la suite. Sin verificación visual del QR ni del frontend.
- Migraciones: sí. `Migrations/20261015031813_MfaYAccesoEmergencia`.
- Riesgo / pendiente:
  - **⚠️ El secreto TOTP vive en claro en `usuario.mfa_secreto`.** Es imprescindible para validar el código y todavía no existe el cifrado AES-256-GCM (llega en F1-09 con `Seguridad__CifradoClave`). Quien lea la tabla `usuario` puede generar códigos TOTP válidos de cualquier usuario con MFA. **No desplegar MFA en producción antes de F1-09**, o adelantar el cifrado. Anotado en SEGURIDAD.md.
  - Bug real cazado por los tests: el token provisional se consumía en memoria sin persistir (`AsNoTracking`), así que **no era de un solo uso**. Corregido antes de dar la tarea por buena.
  - «Admin» es la columna booleana `usuario.es_admin` porque aún no existe la tabla de roles. Los ganchos están aislados en `ServicioMfa.NecesitaMfa` y `ServicioEmergencia.EsAdmin` para que F1-07 los sustituya sin reescribir nada. **Hasta entonces no hay forma de crear un administrador por la API.**
  - El interruptor de configuración «MFA para gerente» de D4 no está implementado: no tiene sentido sin roles. `TODO(A#)` para F1-07.
  - RFC 6238 §5.2 (no reutilizar un mismo paso TOTP) no está aplicado; queda mitigado porque el token provisional es de un solo uso. `TODO(A#)`.
- Commit/PR: `feat(mfa): segundo factor TOTP, codigos de recuperacion y acceso de emergencia`.

## [15/10/2026 06:00] FEAT · F1-05 · Límites de peticiones, bloqueo de cuenta y bloqueo de IP escalado
- Qué:
  - Migración `20261015023651_BloqueosYLimites` con `intento_login` y `bloqueo_ip`, permisos para `rent_app` y `bloqueada_hasta` en `usuario`.
  - `IpBanMiddleware` con escalado **1 h → 24 h → indefinido**, lista blanca desde `Seguridad__IpListaBlanca` y desbloqueo por el administrador.
  - Límites con `Microsoft.AspNetCore.RateLimiting` (paquete del framework, ninguno añadido): global 300/min por usuario autenticado o por IP si es anónima, login 20/min por IP.
  - Bloqueo de cuenta: 5 fallos → 30 min, con contador de fallos **desde el último acierto**.
  - Detección y registro del evento `auth.ip_nueva` (entrada desde una IP que el usuario no había usado).
  - 21 tests nuevos en `backend/AtlasRent.Tests/Seguridad/`.
- Por qué: F1-05 del plan. La app está expuesta a internet y el login es el objetivo natural.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **91/91 en verde**. Los siete escenarios del encargo tienen test propio: bloqueo a los 5 fallos, reutilización a los 30 min, el acierto intermedio no cuenta como fallo, IP bloqueada recibe 403 en cualquier ruta, escalado en tres peldaños, IP de lista blanca nunca se bloquea y 429 con código de referencia.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (usaría secretos de `.env.dev`); la cobertura equivalente la da el contenedor de pruebas con `rent_migrador`.
- Migraciones: sí. `Migrations/20261015023651_BloqueosYLimites`.
- Riesgo / pendiente:
  - **Orden real del pipeline**: `IpBan` → `UseAuthentication` → `UseRateLimiter` → sesión → CSRF. El limitador va después de la autenticación porque su clave global es el usuario; lo que protege de fuerza bruta (bloqueo de IP) sigue primero. Documentado en DOC_TECNICA §7.
  - **El límite de login es solo por IP**, no por IP + usuario como pedía SEGURIDAD.md A.2: la clave sale del cuerpo de la petición, `PartitionedRateLimiter.Create` solo admite particiones síncronas y Kestrel prohíbe leer el cuerpo de forma síncrona. El control por nombre de acceso está cubierto por el bloqueo de cuenta, que es persistente. `TODO(A#)` en `ClavesLimite.Login`.
  - **Los contadores de peticiones están en memoria de la instancia.** Con una sola instancia (Windows Server, un servicio) es correcto; con dos o más el límite pasaría a ser por réplica. El bloqueo de IP está en base de datos y sí es global.
  - `IpBanMiddleware` es **fail-open** si la base de datos no responde, para que `/health/live` siga vivo. Toda ruta real necesita la base de datos.
  - **La pantalla de administración de desbloqueo de IP no existe.** `ServicioBloqueos.DesbloquearIpAsync` está implementada y probada, pero exponerla sin `IPPolicyService` (F1-07) dejaría un endpoint de administración abierto. Es lo único del encargo que queda pendiente.
  - El aviso al admin de IP nueva no se envía por ningún canal: el evento está escrito y probado, pero el envío de correo es de fases posteriores.
  - `Seguridad__Limites__*` añadidos a `config/variables-entorno.example`.
- Commit/PR: `feat(seguridad): limites de peticiones, bloqueo de cuenta y bloqueo de IP escalado`.

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
