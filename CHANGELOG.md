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

## [16/10/2026 00:10] FEAT · F3-01 · Tipo de cambio: histórico, fuente diaria y carga manual (worker)
- **Qué:** tabla `tipo_cambio` (`Data/TipoCambio.cs`) con `UNIQUE (fecha, moneda)`, `numeric(18,6)` en `valor`, `CHECK (moneda <> 1)` y `CHECK (valor > 0)`; `ServicioTiposCambio` (`BuscarAsync`, `ObtenerAsync`, `ListarAsync`, `LeerAsync`, `ActualAsync`, `RegistrarAsync`, `ActualizarDesdeLaFuenteAsync`); `ClienteTipoCambio` detrás de `IFuenteTipoCambio` (endpoint abierto y plan con clave, `rates` y `conversion_rates`); `OpcionesTipoCambio` (`TipoCambio__Url`, `TipoCambio__ApiKey`); `TiposCambioController` en `api/v1/tipos-cambio`; `TrabajoTipoCambio` programado a las 05:00 AST; migración `TipoCambioDiario`; pruebas en `backend/AtlasRent.Tests/TipoCambio/`. `ProgramacionJobsTests` se actualizó con el trabajo nuevo. `config/variables-entorno.example`: `TipoCambio__Url` pasa a valer el endpoint abierto, con el motivo en el propio archivo.
- **Por qué:** criterio de aceptación de F3-01 en `docs/PLAN.md`, «fallo de API → alerta y carga manual», con §3.12, D9 y D22. Tres decisiones que son del dominio y no salen de la especificación: **la carga manual no la pisa nunca la fuente** (si el trabajo la sobrescribiera, la carga manual no arreglaría nada, que es justo lo que §3.12 deja como camino de recuperación), **USD no se guarda** (la base de informes vale 1 por definición) y **la clave de la alerta lleva la fecha**, al revés que las de F2-10, porque el problema «hoy no hay cambio» deja de ser cierto mañana.
- **Verificado:** `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release --filter "FullyQualifiedName~AtlasRent.Tests.TipoCambio"` → **9/9** en verde (28 s). `dotnet test -c Release --filter "FullyQualifiedName~AtlasRent.Tests.Jobs"` → **20/20** en verde, incluidas las dos pruebas de `ProgramacionJobsTests` tocadas por el trabajo nuevo.
- **NO verificado:** la suite completa (≈7 min), que ejecuta el orquestador. **La llamada real a ExchangeRate-API**: no se ha hecho ni una, ni con ni sin clave; el cliente HTTP está probado solo por compilación, y el comportamiento de la fuente (error, `result != "success"`, `rates` frente a `conversion_rates`) está cubierto con un doble de prueba, no contra el servicio. El tiempo de espera de 30 s tampoco se ha medido. Tampoco se ejecutó `dotnet ef database update` contra la base de desarrollo: la migración solo se ha visto aplicada en el contenedor de Testcontainers.
- **Migraciones:** sí, `20261015211252_TipoCambioDiario` (crea `tipo_cambio` y concede `SELECT, INSERT, UPDATE` a `rent_app`; sin `DELETE` porque el tipo de cambio queda congelado en facturas y cobros).
- **Riesgo / pendiente:** (1) el trabajo va a las **05:00**, que es la hora del aviso de reservas de F2-03: decisión del orquestador, documentada en `docs/DOC_TECNICA.md` §8, y los dos trabajos son cortos e independientes, pero es un solapamiento real que el orquestador puede querer mover. (2) `TipoCambio__Url` solo se usa **sin** clave; con clave manda la ruta del plan de pago, porque son dos servicios distintos — si la empresa quiere otra ruta con clave, hay que tocar `OpcionesTipoCambio`. (3) Las marcas `RegistradoAt`/`RegistradoPor` del encargo se guardan como `created_at`/`created_by`, sin columnas propias: el trabajo no tiene persona y duplicar las fechas daría dos marcas que pueden no ser la misma. (4) Sin verificación de la fuente real, el formato de la respuesta de `open.er-api.com` es el que dice su documentación, no uno comprobado aquí.
- **Commit/PR:** sin commit; la rama es `tarea/F3-finanzas` y el commit lo hace el orquestador.

---

## [16/10/2026 00:05] FIX · F3 (pre) · Pool de IPs de prueba sin reutilización (orquestador)
- **Qué:** `backend/AtlasRent.Tests/Infra/ApiTestFactory.cs`. `ContadorInstancias` repartía las IP de las instancias de prueba por el último octeto de `198.51.100.0/24` con `Interlocked.Increment(ref n) % 254 + 1`, de modo que a partir de la instancia 254 el contador volvía a empezar. Ahora `ContadorInstancias.IpSiguiente()` reparte por los dos últimos octetos de `198.51.0.0/16` (unas 50.000 direcciones distintas) y deja fuera `198.51.100.0/24`, que es donde las pruebas escriben a mano `198.51.100.201` (`Seguridad/IpNuevaTests.cs:39`) y `198.51.100.250` (`ApiTestFactoryIpPermitida`).
- **Por qué:** el cierre de F2 dejó abierto que la suite es intermitente por envenenamiento del pool de IPs: las pruebas de bloqueo de F1-05 dejan filas en `bloqueo_ip` sin limpiar, y si a otra clase le toca la IP reutilizada, todos sus logins reciben `403` con `Error SEG-003: La IP está bloqueada`. F3 añade los criterios de concurrencia de F3-02 (50 emisiones) y F3-03 (50 comprobantes), así que el número de instancias por ejecución crece justo cuando más daño hace ese defecto. No es un defecto del código de F3, pero lo bloquearía.
- **Verificado:** `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release --filter "FullyQualifiedName~AtlasRent.Tests.Seguridad|FullyQualifiedName~AtlasRent.Tests.Permisos"` → 104/104 en verde (son las pruebas que bloquean IPs a propósito y las que sufferían el envenenamiento). Suite completa de la línea base antes del cambio: 477/477 en verde.
- **NO verificado:** que tres pasadas seguidas de la suite completa queden en verde con el pool nuevo. Lo ejecuta el orquestador al cierre del bloque; una sola pasada no demuestra nada aquí, porque el fallo era intermitente.
- **Migraciones:** no.
- **Riesgo / pendiente:** el arreglo es de infraestructura de pruebas de F1-05, no de F3. No se ha tocado el otro lado del problema (que las pruebas que bloquean limpien en `finally` la fila que escriben): con el pool sin reutilización ya no se cruzan, pero sigue siendo deuda de F1-05.
- **Commit/PR:** `test(infra): IPs de prueba sin reutilización` en `tarea/F3-finanzas`.

---

## [15/10/2026 22:40] CHK · F2 · Cierre del bloque: verificación independiente y correcciones (orquestador)
- **Qué:** una auditoría independiente (subagente `verifier`, solo lectura) dio **FAIL** al bloque F2 con tres problemas reales, que están corregidos en la entrada de las 21:45 de más abajo. Aquí queda el cierre del orquestador: qué se arregló, qué se verificó y qué queda abierto.
- **Verificación del orquestador, después de los arreglos, tres pasadas completas seguidas:**
  ```
  dotnet build -c Release -warnaserror   → 0 advertencias, 0 errores (las tres veces)
  dotnet test -c Release                 → 477/477, error: 0, salida 0   PASADA 1
  dotnet test -c Release                 → 477/477, error: 0, salida 0   PASADA 2
  dotnet test -c Release                 → 477/477, error: 0, salida 0   PASADA 3
  ```
  477 = 299 de la línea base de F1 + 178 de Fase 2. Las tres pasadas seguidas en verde son la condición que se puso al encargar las correcciones: una sola pasada verde no demuestra nada sobre un bloque cuyo fallo conocido era intermitente.
- **Lo que la auditoría confirmó que está bien** (comprobado leyendo ficheros, no por el informe): ningún endpoint sin `[Accion]`; `ExigirEstadoAsync` llamado desde los **ocho** métodos de estado del contrato, no solo de aprobar; ninguna migración crea la columna `xmin`; **ninguna tabla de F3/F4 adelantada** (`cobro`, `factura`, `fianza`, `comprobante`, `retencion` no existen); ningún secreto en el código; y **la disponibilidad no tiene carrera posible por el camino de la API**, porque la exclusión `btree_gist`, el bloqueo por unidad con `FOR UPDATE` y los dos triggers viven en la base y no en el servicio —el trigger inverso toma el mismo candado y en el mismo orden, así que tampoco hay deadlock entre ellos—.
- **Corrección del orquestador por el fallo intermitente que se le había atribuido a un test frágil.** El orquestador escribía que el test de carrera de F2-06 fallaba una vez de muchas y locientificaba como test frágil sin comprobar la causa. **Era un defecto de producto**: `ServicioRenovaciones` solo capturaba la violación de índice único, y `xmin` es token de concurrencia en todas las entidades menos `Auditoria`, así que la ejecución perdedora lanzaba `DbUpdateConcurrencyException` sin capturar y el trabajo de Hangfire fallaba. Corregido. Queda escrito porque el error de razonamiento es el que importa: "falló una vez" no es evidencia de "es un test frágil".
- **Dos criterios que figuraban HECHA y no estaban demostrados, ya anotados en `docs/PLAN.md`:**
  - **F2-03** ("el anticipo no aparece en ningún saldo ni informe económico") **no es verificable hasta que F3 cree `cobro` y `factura`**. El test antiguo comprobaba que esas tablas no existieran, que es tautológico. Ahora cuenta las filas de las tablas económicas que existan antes y después de crear la reserva con anticipo, y **crece solo cuando F3 añada las suyas**.
  - **F2-01** (concurrencia): la auditoría afirmó que los tests pasarían igual con la sincronización eliminada. **Eso tampoco era exacto**: los dos tests tenían barrera (`TaskCompletionSource` con contador). Aun así se han pasado al `Barrier` del repo, más legible, con escrito en cada uno por qué la barrera es lo que hace que la prueba sea de concurrencia. El implementador leходаó la corrección al verificador en su informe.
- **Abierto y NO arreglado (es de F1-05/F1-08, no de este bloque):** la suite es intermitente por envenenamiento del pool de IPs de prueba. Los tests que fallan son tres de `InmuebleUnidadTests` con `403` en el login y `Error SEG-003: La IP está bloqueada`, porque el pool de IPs es fijo y compartido (`Infra/ApiTestFactory.cs:190`), hay tests de F1-05 que bloquean la IP a propósito (uno sube hasta el nivel indefinido y otro escribe la fila en `bloqueo_ip` sin borrarla), y las clases se ejecutan en un orden no fijado. **El arreglo natural es que cada test que bloquee limpie en `finally` y que el pool no se reutilice entre instancias**, pero eso es cambiar infraestructura de pruebas de otra fase y el orquestador no lo ha hecho sin autorización. Ninguno de los arreglos de esta ronda lo ha provocado ni lo ha curado.
- **No verificado en todo el bloque:** `dotnet ef database update` contra la BD de desarrollo del 5433 (harían las credenciales de `.env.dev`, que por AGENTS.md §6 no se leen); el **disparo real de Hangfire** (los tests invocan los trabajos directamente, con el planificador apagado); y **el aspecto visual de ningún PDF** —los tests comprueban bytes y contenido congelado, no el diseño—. **Y, sobre todo, no hay ninguna pantalla**: las diez tareas son backend, y `frontend/src/navegacion/menu.ts` sigue con todos los módulos de negocio en `ruta: null`. `docs/MANUAL_USUARIO.md` está marcado en consecuencia.
- **Riesgo abierto de licencia, sin resolver:** el código fija `QuestPDF.Settings.License = LicenseType.Community`, gratuita solo con facturación bruta anual consolidada **< 1 M USD**; organismos públicos y empresas cotizadas quedan excluidos sea cual sea su facturación; y QuestPDF es *source-available*, no una licencia OSI, así que la auditoría de dependencias de `SEGURIDAD.md` §A.4 no puede asumir MIT. **Requiere confirmar los datos de la empresa antes de producción.**

---

## [15/10/2026 21:45] FIX · F2-01/F2-03/F2-06 · Correcciones de la auditoría del bloque F2 (implementador)
- Qué (cinco defectos que encontró una auditoría independiente del bloque F2; no es una tarea nueva del plan):
  1. **El trabajo de renovaciones no capturaba `DbUpdateConcurrencyException`** (`Services/ServicioRenovaciones.cs`). Solo capturaba el `DbUpdateException` cuyo `InnerException` es `PostgresErrorCodes.UniqueViolation`. Como `DbUpdateConcurrencyException` **deriva** de `DbUpdateException` pero no lleva un `PostgresException` dentro, el filtro no la recogía y la ejecución perdedora de una carrera se escapaba: el trabajo de Hangfire quedaba fallido en vez de contar `OmitidosPorCarrera`. Se añade el `catch` con el mismo criterio que ya usa `ServicioFines` (líneas 130-134) y con el mismo trato: contar y seguir.
  2. **Una reserva en conflicto no se podía cancelar** (`Services/ServicioReservas.cs`). `CancelarAsync` exigía `Estado == Activa`, así que una reserva que el servicio de contratos había dejado en `EnConflicto` se quedaba `Activo = true` para siempre, con su candado puesto y con su aviso de conflicto (F2-10) sin forma de cerrarse. Nuevo predicado `ExigirCancelable` (`Activa` **y** `EnConflicto`), liberación del candado y cierre del aviso de conflicto con `IRegistroAlertas.ResolverAsync` en `despuesDeGuardar`, o sea dentro de la misma transacción que la baja.
  3. **Migración del trigger de `ocupacion_dia`**: cuenta lo que ocupan las *otras* filas (`d.id <> NEW.id`). Ver el "por qué" largo más abajo.
  4. **Tests de concurrencia de F2-01 reescritos con `Barrier`** (`DisponibilidadOcupacionTests.cs`).
  5. **`ElAnticipoNoEsUnCobro` reescrito** (`ReservaAnticipoYAlertaTests.cs`): antes solo comprobaba que las tablas `cobro`/`factura`/`factura_borrador`/`asiento` no existieran, así que el `foreach` nunca se ejecutaba y la aserción final era tautológica.
- **La decisión del orquestador que implementa el punto 2: la decisión del gerente es cancelar la reserva.** docs/ESPECIFICACION.md §3.4 dice que en conflicto la reserva "sigue bloqueando hasta que el gerente decida" y **no enumera cuáles son esas decisiones**, así que no había ninguna que respetar aparte de esta. No se han inventado otras salidas (no hay "convertir a otra unidad", no hay "reabrir"): el mínimo para que el estado tenga salida es poder cancelarla. Cancelar sigue exigiendo `Acciones.ClienteEditar` —**el empleado puede hacerlo igual que las demás bajas**, y está probado— y sigue auditándose con `reserva.cancelada`. El motivo queda escrito en el XML del método y en `ExigirCancelable`.
- **El punto 3 era un defecto de producto real, y la duda del auditor era correcta.** `SincronizarAsync` (F2-01/F2-03) **no** borra y reinserta: si la fecha sigue en la reserva, hace `UPDATE` de la fila de `ocupacion_dia` cambiando su `tipo` (`fila.Tipo = tipo`, y son las filas que se quedan las que se reescriben). El trigger de capacidad se dispara también en el `UPDATE`, y dentro de un `BEFORE ROW` el `SELECT` ve el estado anterior, así que **contaba la fila que se estaba tocando**. Consecuencia: cambiar una corta de medio día a jornada **en la misma fecha** se rechazaba a sí misma con "el día ya está ocupado y un día completo no cabe", y al revés con "ya está reservado el día completo". Nadie podía corregir su propia reserva. El arreglo es `AND d.id <> NEW.id` en el recuento: lo que hay que comprobar es la capacidad que ocupan los demás; en un `INSERT` el resultado es idéntico porque `NEW.id` todavía no está en la tabla.
- **Sobre la duda de si hacía falta un `SaveChangesAsync` antes del aviso de la alerta (punto 1): no, y está escrito por qué en el código.** `ServicioAlertas.RegistrarAsync` escribe por SQL crudo y no fuerza el volcado del `ChangeTracker`, pero el aviso no compite por ningún índice único con lo que falta por guardar, así que no hay ninguna colisión que "salte" ahí. Lo que compite —`contrato_bloque`, `contrato_version` y la versión de `contrato` con su `xmin`— se decide entero en el `SaveChangesAsync` posterior, dentro de la misma transacción, y las dos caras de la carrera (violación de índice único y de `xmin`) se recogen en los dos `catch` del trabajo. Poner un `SaveChangesAsync` antes solo movería el punto de fallo sin evitarlo.
- **Punto 4: el criterio completo de F2-03 ("el anticipo no aparece en ningún saldo ni informe económico") NO es verificable hasta que F3 cree `cobro` y `factura`.** PLAN.md debería decirlo junto al criterio. El test ahora hace lo que sí se puede: busca por nombre (`LIKE 'cobro%'`, `'factura%'`, `'asiento%'`, `'pago%'`, `'cotizacion%'`, `'%liquidacion'`) las tablas de efecto económico, **cuenta sus filas antes y después** de crear la reserva y comprueba que no cambian, y comprueba que el anticipo quedó guardado como nota en la reserva. La lista crece sola cuando F3 añada las suyas, sin reescribir el test. Los recuentos son antes/después y no absolutos porque la base de pruebas la comparten todas las clases de la colección. El propio mensaje de la aserción lo dice, para que nadie lo lea como más de lo que es.
- **Punto 4, corrección al informe de la auditoría:** los dos tests de concurrencia de F2-01 (`SolapamientoConcurrente`, `MediosDiasConcurrentes`) **sí tenían barrera** —un `TaskCompletionSource` con contador—, así que a este implementador le toca decirlo: la afirmación de la auditoría de que "pasarían igual con la sincronización eliminada" no era exacta. Aun así se han pasado al patrón `Barrier` del repo (`RenovacionContratosTests:380`, `FinPorFechaEfectivaTests:168`), que es más legible y no depende de un contador manual, y se ha escrito en cada test por qué la barrera es lo que hace que la prueba sea de concurrencia.
- **Problema 5 (los tests que fallaron): NO eran los de F2 y no son de este bloque.** Antes de tocar nada se corrieron 3 pasadas de la línea base (473/473, las tres en verde, salida completa a fichero en `%TEMP%`). El fallo salió en la **tercera pasada de la verificación final**, y estos fueron los tres nombres exactos:
  - `AtlasRent.Tests.Inmuebles.InmuebleUnidadTests.ElEstadoManualSeGuardaYSeFiltraYNoEsOcupacion`
  - `AtlasRent.Tests.Inmuebles.InmuebleUnidadTests.UnaUnidadSoloOfreceLasModalidadesQueTienePermitidasYSuTarifaEsPorModalidadYMoneda`
  - `AtlasRent.Tests.Inmuebles.InmuebleUnidadTests.ElEmpleadoGestionaLosInmueblesYMantieneLasUnidades`
  - Aserción exacta, la misma en los tres: `AyudaPermisos.cs:111 → login.EnsureSuccessStatusCode()` con `System.Net.Http.HttpRequestException : Response status code does not indicate success: 403 (Forbidden)`. El log de esa misma pasada lo dice literal: `Error SEG-003: La IP está bloqueada por intentos fallidos de acceso`.
  - O sea: **no es el `SaveChangesAsync` de plpgsql que sospechaba el auditor**, y no hay nada de F1/F2 en la pila. Es `Middleware/IpBanMiddleware.cs` rechazando **todas** las peticiones de la instancia de prueba, login incluido, porque su IP estaba en `bloqueo_ip`. Los tres tests de la clase fallan a la vez porque comparten la IP de su `ApiTestFactory`.
  - **Causa de fondo, en la infraestructura de pruebas de F1-05/F1-08 (fuera del bloque F2, y por eso no se ha tocado):** el pool de IPs de prueba es fijo y compartido. `Infra/ApiTestFactory.cs:190` reparte las instancias con `Interlocked.Increment(ref _siguiente) % 254 + 1` sobre `198.51.100.0/24`, y en una pasada se crean ~54 instancias; además hay IPs escritas a mano (`IpNuevaTests` se pone la `198.51.100.201`, `ApiTestFactoryIpPermitida` mete la `198.51.100.250` en la lista blanca). Y hay tests de F1-05 que **bloquean la IP a propósito**: `EscaladoBloqueoIpTests` deja 29 fallos y sube hasta el nivel **indefinido** (que solo se levanta en su última línea, `DesbloquearAsync`), `ListaBlancaIpTests` mete 12, `BloqueoCuentaTests` 5 por test, e `IpBloqueadaTests` **escribe la fila en `bloqueo_ip` directamente** y no la borra. Cualquiera de esos que caiga sobre la IP que otra clase está usando en ese momento convierte todos sus logins en 403, y qué clase va después de cuál no está fijado. **Lo que queda por confirmar, y es de quien sea dueño de F1-05: qué clase envenenó qué IP en esa pasada** — con un `SELECT ip, nivel, bloqueada_hasta FROM bloqueo_ip` al final de la ejecución saldría. El arreglo natural es que cada test que bloquee limpie en `finally` y que el pool de IPs no se reutilice entre instancias, pero eso ya es otro bloque.
  - Lo que sí se puede decir es que **ninguno de los cinco problemas de la auditoría explican este fallo**, y que los arreglos de este encargo no lo han provocado ni lo han curado.
- **Test nuevo de la carrera de renovación (`LaCarreraDeLaRenovacionNingunaEjecucionLanza`).** La carrera no se deja al azar: el aviso del incremento es el punto exacto en el que el trabajo ya ha leído y calculado pero todavía no ha escrito, así que un `IRegistroAlertas` decorador con `Barrier(2)` **y el registro de verdad detrás** deja las dos ejecuciones con el mismo estado viejo y las suelta a la vez. Comprueba que **ninguna de las dos lanza**, que hay 1 renovado + 1 omitido, y que quedan un bloque, una subida de renta y un aviso (el de la perdedora desaparece con su transacción). Al probarlo contra el código ya arreglado falló primero por `Bloques == 3` (había usado un contrato vencido 38 meses, que genera tres bloques) y luego por el aviso en 0 (el decorador de la primera versión no escribía); ambos son fallos del test, no del servicio.
- Permisos: **sin cambios**. `DELETE /api/v1/reservas/{id}` sigue con `Acciones.ClienteEditar`; lo único nuevo es que ahora también acepta una reserva en conflicto.
- Verificado: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release` → **477/477**, código de salida 0 (473 previos + 4 tests nuevos). Antes de arreglar nada se corrieron 3 pasadas de la línea base, las tres en verde. **Después de los arreglos, 6 pasadas completas: la 3.ª falló** —los tres tests de `InmuebleUnidadTests`, por el 403 de IP bloqueada que se explica arriba, que es de F1-05 y no de este bloque— **y las pasadas 4.ª, 5.ª y 6.ª salieron las tres seguidas en verde**. Los cuatro tests nuevos se probaron además por separado contra su clase.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (harían falta credenciales de `.env.dev`). **El `Down` de la migración no se ha ejecutado**: solo se ha probado el camino de subida, que es el que aplica el `ContenedorBdFixture` de las pruebas (y con eso queda comprobado que las migraciones siguen aplicando desde cero). No se ha probado el comportamiento con el planificador de Hangfire corriendo de verdad, solo que el trabajo devuelve el resumen sin lanzar. **No se ha identificado qué clase envenena la IP de prueba** en la pasada que falló (ver el punto del problema 5).
- Riesgo / pendiente:
  - **El patrón de nombres del punto 4 es heurístico.** Si F3 le pone a una tabla económica un nombre que no case con ninguno de los seis patrones (`cobro*`, `factura*`, `asiento*`, `pago*`, `cotizacion*`, `*liquidacion`), este test no la mirará. Lo que habría que hacer en ese caso es añadir el patrón, no escribir otro test. Está escrito en el propio test.
  - `contrato_liquidacion` entra hoy en la lista de tablas de dinero. Cuenta para el criterio (guarda `importe_faltantes`, `fianza_retenida`…) y es correcto que se vigile, pero conviene saber que es de contratos y no de reservas.
  - La salida del conflicto es **una sola**: cancelar. Si mañana el negocio quiere "mover la reserva a otra unidad" o "reabrirla", eso es trabajo nuevo con su criterio de aceptación, no una extensión de esto.
- Migraciones: **sí** — `20261015191825_CapacidadOcupacionDiaAlEditar` (`CREATE OR REPLACE` de `ocupacion_dia_comprueba_capacidad`; no cambia el modelo de EF, así que el snapshot queda igual).
- Commit/PR: sin commit (lo commitea el orquestador).

---

## [15/10/2026 20:10] FEAT · F2-10 · Alertas sin duplicar y bandeja de aprobaciones (implementador)
- Qué:
  - `Services/ServicioAvisosOperacion.cs` (nuevo): los cuatro avisos de §3.16 que se pueden construir con las tablas que ya existen, y el cierre de los que dejan de aplicar. `Jobs/TrabajoAvisosOperacion.cs` (nuevo) y `Jobs/ProgramacionJobs.cs`: `avisos-operacion` a las **08:00** de Santo Domingo, la primera hora libre detrás de los seis trabajos que ya había.
  - `Services/ServicioAprobaciones.cs` (nuevo) + `DTOs/AprobacionesDtos.cs` (nuevo): la fila de la bandeja (quién, qué, valor actual y propuesto). `Controllers/ContratosController.cs`: `GET /api/v1/contratos/solicitudes`, `POST .../solicitudes/{id}/aprobacion` y `POST .../solicitudes/{id}/rechazo`. `Program.cs` registra los tres servicios nuevos.
  - `Data/SolicitudAprobacion.cs`: el `TODO(A#)` de F2-10 pasa a ser la decisión que se tomó —**no se desnormaliza nada**— y por qué.
  - 12 tests nuevos en `backend/AtlasRent.Tests/Bandeja/` y `ProgramacionJobsTests` actualizado.
- **La bandeja no reimplementa la aprobación.** Los botones de la bandeja llaman a `ServicioContratos.AprobarAsync` / `RechazarAsync`, los mismos que los de la ficha, y reutilizan `ConIdempotenciaAsync` del controlador base en vez de copiar su traducción de errores. No hay una segunda regla de negocio ni un segundo sitio donde se pueda saltar la caducidad por huella.
- **La huella de idempotencia de los botones de la bandeja es la del endpoint de contratos** (`/api/v1/contratos/{id}/aprobacion`), no la de la ruta de la bandeja. Aprobar desde la bandeja y aprobar desde la ficha son la misma operación, así que repetirlas con la misma clave es un reintento guardado en los dos caminos y no dos aprobaciones. Sin esto, el mismo clic desde las dos pantallas sí habría creado dos filas de idempotencia para la misma aprobación.
- **El empleado recibe 403, no una bandeja vacía.** El listado va con `contrato.estado` en el atributo **y** el servicio vuelve a mirar el techo de permisos y audita la denegación, igual que hace `ServicioContratos` al aprobar. Una lista vacía esconde el error de permisos detrás de "no hay nada pendiente". La ficha del contrato sigue con `cliente.ver`, así que el empleado no pierde la lectura de los contratos que ve.
- **Solo solicitudes vivas, y no se borra ninguna.** El filtro es `Resultado == Pendiente` **y** contrato en `PendienteAprobacion`: una solicitud huérfana que quedara pendiente porque alguien cambió el estado a mano no aparece en una bandeja desde la que se apruebe. Caducadas y resueltas no se listan pero siguen en `solicitud_aprobacion` y en la ficha, que es el historial de por qué un contrato volvió a enviarse.
- **"Valor actual y el propuesto" sale de dos sitios, no de uno.** `rentaPropuesta` es `contrato.renta`, lo que el contrato pide aprobar. `rentaVigente` es la `renta_nueva` de la última fila de `contrato_version`, y es `null` si el contrato nunca estuvo activo —que no es cero: es que todavía no hay una renta vigente anterior con la que comparar. `hayCambioDeRenta` lo dice explícitamente, y **cuando no hay cambio los dos números son el mismo**: la bandeja no fabrica una diferencia para tener algo que enseñar.
- **Ninguna clave lleva fecha, y esa es la decisión.** `contrato-baja-programada:{id}`, `contrato-liberacion-30:{id}`, `reserva-conflicto:{id}`, `ip-bloqueada:{id}`. Sin fecha: el aviso es una vez y para siempre por entidad, y sobre todo con fecha el criterio "repetir el trabajo dos veces el mismo día no deja dos alertas" solo sería cierto el primer día. La condición de fecha del barrido y la clave única dan la misma garantía y el refresco es el de siempre (`ON CONFLICT … WHERE resuelta_at IS NULL`, contador `repeticiones`).
- **El aviso se cierra cuando deja de aplicar.** Se le pone `resuelta_at`, nunca se borra. Sin esto, anular una baja dejaría encendido un aviso de una fecha que ya no existe, y eso acaba enseñando a ignorar el panel. Se cierra por prefijo de clave contra el conjunto de entidades que hoy están en la condición.
- **El aviso de liberación a 30 días es solo de contratos `Activo`.** Uno en `BajaProgramada` ya tiene su propio aviso y uno finalizado está fuera: avisar de los dos sería el mismo hecho con dos claves, que es justo lo que el criterio prohíbe.
- Permisos: sin permisos nuevos. Lectura y botones de la bandeja con `contrato.estado` (fila 6, gerente y admin); el 403 del filtro global y el del servicio son la misma regla por dos caminos.
- **§3.16 enumera quince tipos de alerta. Esto NO está todo hecho, y no lo parece.** Implementados aquí: bajas programadas, unidades que se liberan en 30 días, conflictos, IP bloqueadas. Ya estaban y no se han tocado: reservas a 7 días (F2-03), fallo de backup (F1-12), incremento aplicado (F2-06), liquidaciones pendientes (F2-09). **Quedan fuera, con un `TODO(A#)` en el sitio donde iría cada uno dentro de `ServicioAvisosOperacion`, nombrando la tabla que falta:**
  - Facturas vencidas → `factura` y `cobro` (F3).
  - Periodos sin facturar → `factura` y el calendario de facturación (F3).
  - Comprobantes por agotarse o por vencer → `comprobante` (F3).
  - Retenciones sin comprobante → `retencion` (F3).
  - Fianzas pendientes de devolver → `fianza` y `fianza_devolucion` (F3).
  - Envíos fallidos → outbox y cola de envío de la cadena de notificación (F3). Es también lo que falta del canal "aviso en app **y** email" de §3.16: hoy las alertas solo están en la tabla.
  - Fallo del tipo de cambio → `tipo_cambio` con su historial (F3).
  - **Entradas desde IP nueva → la tabla `sesion` sí existe**, pero no se implementa porque falta una decisión de negocio que no es del implementador: qué cuenta como IP nueva (global en el sistema, por usuario o por red). Las tres dan números muy distintos y un aviso que sale cada día por una IP que no es sospechosa es peor que no avisar.
- Verificado: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release` → **472/472**, código de salida 0 (460 previos + 12 tests de F2-10). Los de `Bandeja` por separado: 12/12.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (harían falta credenciales de `.env.dev`). No se ha probado el reinicio del planificador con el trabajo nuevo registrado, solo que la lista de `ProgramacionJobs` y el registro con `RecurrentesFalsos` lo incluyen con su cron.
- Riesgo / pendiente:
  - **`rentaVigente` es `null` en el caso real de hoy.** No hay ningún camino de negocio que deje un contrato con versiones en `contrato_version` y a la vez pendiente de aprobación (un contrato vigente no vuelve atrás), así que la columna sale vacía en todas las filas hasta que exista el reenvío de un contrato vigente. El cable está probado con una versión escrita a mano (`LaBandejaEnsenaElValorActualDelHistorialCuandoHayCambioDeRenta`) y el caso real, sin versiones, con `LaBandejaMuestraQuienQueYLosDosValores`. **Quien pinte la pantalla tiene que tratar `null` como "contrato nuevo", no como cero.**
  - El listado de la bandeja **no trae el motivo de la caducidad**: lo natural para una fila viva sería "caducada, vuelve a enviarla", y esa fila no sale de la bandeja porque ya no está viva. Se ve en la ficha. No se ha añadido una sección de "caducadas" en la bandeja porque no la pidió el encargo.
  - El aviso de IP bloqueada se registra por `bloqueo_ip.id`. Si el mismo bloqueo se reescribe en vez de crear fila nueva, la clave no cambia y el aviso se refresca; si se crea una nueva, sale una alerta nueva. Ninguna de las dos duplica nada.
- Migraciones: **no**. No hay ninguna tabla nueva: `alerta` y `solicitud_aprobacion` ya estaban creadas y migradas, y las consultas del barrido no necesitan ningún índice nuevo (`clave` es único y es por donde se deduplica). No se ha tocado `docs/`.
- Commit/PR: sin commit (lo commitea el orquestador).

---

## [15/10/2026 19:40] FEAT · F2-09 · Baja programada, fin por fecha efectiva y liquidación real (implementador)
- Qué:
  - **Punto 0, corrección de una instrucción anterior.** `ServicioInventario.ExigirRevisable` ahora admite `Activo`, `BajaProgramada` **y `Finalizado`**; sigue rechazando `Borrador`, `PendienteAprobacion`, `Aprobado` y `Liquidado`. El comentario del código y del test explican por qué: con el fin automático por fecha efectiva, el contrato pasa a `Finalizado` solo y la revisión se firma ese mismo día; impedir la firma ahí hacía que un contrato finalizado sin firmar **no se pudiera firmar ni liquidar nunca**, porque la liquidación exige la revisión firmada. El test de F2-08 `LaRevisionNoSePuedeFirmarEnUnContratoFinalizado` pasa a llamarse `LaRevisionSePuedeFirmarEnUnContratoFinalizado` y espera 201.
  - `Services/ServicioFines.cs` (nuevo): el trabajo de fin por fecha efectiva y el barrido de la alerta persistente. `Jobs/TrabajoFinContratos.cs` (nuevo) y `Jobs/ProgramacionJobs.cs`: `fin-contratos` a las **07:00** de Santo Domingo, detrás de los cinco trabajos que ya había.
  - `Data/ContratoLiquidacion.cs` (nueva) + `Dominio/DecisionFianza.cs` + `AtlasRentDbContext` + navegación en `Contrato`. `Service/Contratos.LiquidarAsync` deja de ser una etiqueta: exige la revisión firmada dentro del `antesDeGuardar`, escribe la liquidación y resuelve la alerta, todo en la misma transacción.
  - `Alerta.resuelta_at` + `IRegistroAlertas.ResolverAsync` y `WHERE alerta.resuelta_at IS NULL` en el `ON CONFLICT`.
  - `ServicioContratos.CambiarEstado` pasa a `internal` con `Guid? usuario` para que el trabajo reutilice la tabla de transiciones en vez de copiarla.
  - 11 tests nuevos en `backend/AtlasRent.Tests/Liquidacion/` y `ProgramacionJobsTests` actualizado.
- **La unidad queda libre desde la fecha efectiva, no desde que alguien apriete un botón.** El criterio de aceptación no es un botón: es un trabajo diario a las 07:00 que cierra los contratos en `BajaProgramada` cuya fecha efectiva ya pasó. `FechaFinReal` se escribe con **la fecha pactada, no con el día que corrió el trabajo**: si el trabajo se cae tres días, el cliente se fue hace tres días y escribir la fecha de ejecución falsearía el histórico. La unidad entra por reserva corta el día de la fecha efectiva sin intervención humana (`LaUnidadQuedaLibreDesdeLaFechaEfectiva`).
- **Idempotencia en dos capas, más `xmin`.** Capa 1: la condición de fecha, y el contrato se relee dentro de la transacción antes de tocar nada. Capa 2: si el estado ya no admite el paso, el `InvalidOperationException` de `CambiarEstado` (el mismo 409 que ve la API) se cuenta como omitido y no como fallo del trabajo. Y entre dos corridas simultáneas está `DbUpdateConcurrencyException` por `xmin`, también omitido. Hay un test con dos contextos y barrera que prueba que de dos pasadas sale un solo fin, una sola fila de auditoría y una sola alerta.
- **El trabajo no pide permisos pero sí audita, y la auditoría dice que fue el sistema.** La fila sale con `usuario_id = NULL`, `origen = 'job'` e IP `127.0.0.1`, dentro de la transacción y antes del commit, igual que la renovación de F2-06. No se inventa un usuario para que la fila "entre": con RLS, `auditoria` admite `INSERT` con `WITH CHECK (true)`, así que la fila con autor nulo se escribe sin problema. `estado_actual_por_id` del contrato queda nulo por el mismo motivo. El test lo comprueba fila a fila.
- **La liquidación escribe lo que sí es un dato de la liquidación y nada más.** `contrato_liquidacion` guarda: `fecha_liquidacion`, `liquidado_por_id`, `revision_inventario_id` (nula si el contrato no tenía inventario), `importe_faltantes` (sale de `revision_inventario.importe_propuesto`, 0 si no hay), `decision_fianza`, `fianza_contrato` (copia de la fianza pactada) y `fianza_retenida`. **No hay fila de cobro ni asiento**: la tabla `cobro` es de F3-01 y las de fianza tampoco existen, y no se han inventado. El `TODO(A#)` que hereda de F2-08 queda en el `antesDeGuardar` con la firma que hará falta (`public Task EmitirLiquidacionAsync(Guid contratoId, Guid usuario, Guid contratoLiquidacionId, CancellationToken ct)`) y los tres importes ya calculados, para que F3-01 lea la fila y no tenga que volver a la revisión ni al contrato.
- **La decisión sobre la fianza se deriva, no se pregunta.** Hay faltante → `RetenidaPorFaltantes`; no hay → `Devuelta`. Lo retenido es `min(faltantes, fianza)`: de una fianza no se retiene más de lo que hay, y lo que el faltante supera a la fianza es un cobro pendiente, no una retención. Es una regla mía, documentada en el código: §3.6 dice que los descuentos se aplican contra la fianza con gerente/admin (y liquidar ya exige `contrato.estado`), pero no dice qué pasa cuando el faltante es mayor que la fianza. Lo que hay que ajustar en F3-01 no es esta regla sino su cobro.
- **La alerta persistente es una fila viva, no una fila por día.** "Persistente" con el índice único por clave no puede ser una alerta nueva cada día: cien filas de lo mismo no son una señal. Lo que hay es la fila viva que el `ON CONFLICT DO UPDATE` refresca —le sube `repeticiones` y le reescribe el mensaje con los días que lleva esperando—, con `WHERE resuelta_at IS NULL` para que una alerta cerrada no se resucite (si el problema vuelve, se inserta una nueva). **Se resuelve poniendo `resuelta_at`, nunca borrando**: la fila dice que pasó, cuándo y cuántas veces se vio. Se refresca en tres sitios, todos con la misma clave: al finalizar por API, al finalizar el trabajo y en el barrido diario de contratos `Finalizado` sin liquidar —este último es el que la vuelve persistente, porque un contrato liquidado a mano por un gerente tampoco puede quedarse sin aviso. Al liquidar, se resuelve dentro de la misma transacción que escribe la liquidación: si la liquidación se deshace, la alerta sigue viva.
- **El barrido va después del bucle y salta los contratos que esa pasada ya avisó.** Refrescar dos veces el mismo contrato en la misma mañana haría que `repeticiones` sumara dos por un solo día, y ese contador es justamente lo que dice "lleva dos días esperando".
- **La revisión firmada se exige dentro del `antesDeGuardar`, no antes.** Es la diferencia entre un 409 que deja el contrato exactamente igual y uno que ya cerró inventario a medias. Y con `Liquidar` reintentado es 409 por estado, como el incremento ya aplicado de F2-06.
- Permisos: nada nuevo. `liquidacion` con `contrato.estado` (fila 6, gerente y admin) y el 403 al empleado está probado; la lectura de la ficha va con `cliente.ver` y el empleado la conserva.
- Verificado: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release` → **460/460**, código de salida 0 (448 previos + 11 tests de F2-09 + 1 de orden de trabajos). Las de `Liquidacion` por separado: 11/11.
- **Un fallo ajeno en la primera pasada completa:** `RenovacionContratosTests.DosEjecucionesSimultaneasNoCreanDosBloques` (carrera de F2-06, código que esta tarea no toca) falló una vez de una; reejecutado tres veces seguidas sale verde. Se reporta tal cual: es un test de carrera y una sola pasada no es concluyente. La segunda pasada completa de `dotnet test -c Release` salió **460/460, código de salida 0**.
- Migraciones: sí (`20261015171412_LiquidacionYFin`). Un `CreateTable` (`contrato_liquidacion`) más una columna en `alerta` (`resuelta_at`). `xmin` quitado a mano del `CreateTable` y `GRANT SELECT, INSERT ON contrato_liquidacion TO rent_app`; el `GRANT` **no incluye `UPDATE` ni `DELETE`** porque la liquidación nace cerrada y no se borra nunca. Sin RLS, siguiendo el precedente de `contrato` y de las tablas de F2-08.
- NO verificado: `dotnet ef database update` contra la base de desarrollo del 5433 (harían falta credenciales de `.env.dev`). No se ha probado el reinicio del planificador con el trabajo nuevo registrado, solo que la lista de `ProgramacionJobs` y el registro con `RecurrentesFalsos` lo incluyen con su cron.
- Riesgo / pendiente:
  - **El contrato de la revisión de salida ha cambiado.** Quien lo lea en el histórico de F2-08 ("solo se firma con el contrato en pie") tiene que saber que ya no es así; el motivo está escrito en el código, no solo aquí.
  - La fila de liquidación **no mueve dinero**. Si alguien lee `importe_faltantes` como "lo que se le cobró al cliente", está leyendo mal: es lo que F3-01 tiene que cobrar. Lo mismo con `fianza_retenida`.
  - La fianza retenida se limita al importe de la fianza y el resto del faltante se queda sin destino hasta F3-01. Es consciente, no es un descuido.
- Commit/PR: sin commit (lo commitea el orquestador).

## [15/10/2026 18:55] FEAT · F2-08 · Inventario base, inventario congelado del contrato y revisión de salida (implementador)
- Qué:
  - Cuatro entidades nuevas y **tres cosas distintas, no una**: `Data/InventarioBaseItem.cs` (el inventario base de la unidad, opcional y sin fotos, §3.1), `Data/ContratoInventarioItem.cs` (la **copia congelada** del base en el momento de activar, §3.5) y `Data/RevisionInventario.cs` + `Data/RevisionInventarioItem.cs` (la revisión de salida, §3.6), con `Dominio/EstadoRevisionInventario.cs`. Cada archivo lleva un comentario que dice cuál de las tres es y por qué no es una versión de las otras.
  - `Services/ServicioInventario.cs`, `Controllers/InventariosController.cs` (7 endpoints), `DTOs/InventarioDtos.cs`, `Program.cs` (DI) y el mapeo en `AtlasRentDbContext` más las navegaciones de `Unidad` y `Contrato`.
  - `ServicioContratos.ActivarAsync` congela el inventario en su `antesDeGuardar`, en el mismo commit que el cambio de estado y el candado de la unidad. `ServicioContratos.LiquidarAsync` lleva el `TODO(A#)` de F2-09.
  - 12 tests en `backend/AtlasRent.Tests/Inventario/`.
- **Las tres cosas se separaron en el modelo y en las tablas, no en comentarios.** Inventario base = lo que hay en la unidad y lo corrige el empleado; inventario del contrato = lo que había **el día que entró el cliente**, congelado; revisión de salida = lo que hay **el día que se va**, elemento por elemento. La confusión más fácil para quien lea esto después es pensar que el inventario del contrato es "el inventario de la unidad" y que editar la unidad cambia lo que el contrato firmado tiene por suyo. Por eso `contrato_inventario_item` no tiene columna `updated_at` ni implementa `IEntidadTrazada`: **no existe ningún camino de escritura sobre esa tabla** después de `CongelarAsync`, que solo se llama desde la activación. Editar el base cambia lo que se verá en el próximo contrato, y solo eso.
- **La copia congelada es copia de valores, no de referencias.** `nombre`, `cantidad` y `notas` viajan dentro de la fila. Si fueran una referencia al base, corregir el catálogo mañana cambiaría el inventario de un contrato ya firmado, que es exactamente lo que §3.5 dice que no pasa. `inventario_base_id` queda solo para rastrear de dónde salió la línea, en `Restrict`. Es el mismo patrón que la plantilla congelada de F2-07 y el mismo test que lo demuestra.
- **La revisión se firma una sola vez y no se edita nunca.** La fila **nace firmada** (`firmada_at` y `firmada_por_id` no son opcionales, no hay borrador), hay índice único sobre `contrato_id` y no hay ningún endpoint que la modifique después. El segundo intento es 409 con el motivo, igual que el incremento ya aplicado de F2-06. **Firmar no borra nada**: el inventario congelado se conserva tal cual porque es la contra que se acaba de firmar; sin él las líneas de la revisión no dirían nada.
- **Presente, ausente y dañado son tres estados, y "dañado" lleva "D"** (`EstadoRevisionInventario.Danado`): en JSON sale `"Danado"`. Se eligió sin tilde para no depender de cómo serialice cada cliente los diacríticos; si el front necesita la forma acentuada, se renombra el miembro del enum y el valor en la base no cambia (1, 2 y 3 son explícitos y no van por su posición).
- **La nota es obligatoria en ausente y dañado, y opcional en presente.** Es un supuesto mío con `TODO(A#)` en el código: §3.6 no lo dice. La razón para exigirla es que la nota es lo que explica el importe que el gerente propone, y "no se inventan notas obligatorias para presente" solo tiene sentido si en los otros dos estados sí se pide. Si el negocio decide lo contrario, se quita un bloque de `IValidatableObject` y su test.
- **El importe del faltante queda en la revisión, no en un cobro.** `revision_inventario.importe_propuesto` es el número contra el que liquida F2-09. La tabla `cobro` es de F3-01 y **no se ha inventado**. El importe va en la moneda del contrato y por eso no lleva columna de moneda: una moneda por documento (§6).
- **Sin `DELETE` en ninguna de las cuatro tablas.** Un elemento del base que sobra se **desactiva** con `activo`, como las unidades y las plantillas del catálogo; por eso el alta, la edición y la desactivación del base son tres endpoints de una fila cada uno, que es el patrón del repo (`/aprobacion`, `/baja`, `/desactivacion`) y evita inventar un guardado multi-fila aparte de `GuardadoAuditado`. La revisión de un contrato sin inventario congelado es 409, no 201: no hay contra que firmar.
- **Sin fotos**, porque §3.1 lo dice del inventario base y nadie pidió lo contrario. No hay columnas de imagen ni de ruta de archivo, y no se ha construido nada de almacenamiento.
- **La revisión tiene que tener una línea por cada elemento del inventario congelado**: ni una de menos (un mueble sin comprobar no es un inventario revisado) ni una de más (algo que no está en el contrato no se puede liquidar contra él). Las dos cosas son 409 con el recuento de lo que falta y lo que sobra. Un cuerpo mal formado (nombre vacío, cantidad negativa, importe negativo, ausente sin nota) es **400**, no 409: no es un conflicto con el estado de nadie.
- **La revisión solo se firma con el contrato en pie**: activo o con baja ya programada. En borrador el cliente no ha entrado; en finalizado y liquidado ya se fue, y revisar después sería escribir el documento histórico a posteriori.
- **Permisos, sin crear ninguna acción:** los tres GET con `cliente.ver` (fila 2 de §2), el inventario base con `cliente.editar` (fila 3, donde §2 agrupa unidades y reservas: es el mismo permiso con el que el empleado prepara el contrato) y **firmar la revisión con `contrato.estado`** (fila 6, gerente y admin), porque esa firma es la que desbloquea la liquidación y la que va al cliente. Ningún GET nuevo quedó con acción de escritura (revisado con grep). Los permisos se comprueban también dentro del servicio, como en `ServicioIncrementos`.
- **Para F2-09, en `ServicioContratos.LiquidarAsync`:** un `TODO(A#)` con la firma exacta que tiene que llamar, `public Task ExigirRevisionFirmadaAsync(Guid contratoId, CancellationToken ct)` de `ServicioInventario`, que **ya existe y está probada**: lanza `InvalidOperationException` (409) si el contrato tiene inventario congelado y no hay revisión firmada, y no exige nada si no hay inventario (§3.6 dice "si el contrato tiene inventario" y §3.1 lo hace opcional). **Lo que NO estaba hecho era la llamada** —`LiquidarAsync` liquida sin mirar, porque la liquidación es de F2-09 y así lo pidió el encargo—, y **F2-09 la ha añadido** dentro del `antesDeGuardar`.
- Verificado: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release` → **448/448**, código de salida 0 (436 previos + 12 nuevos). Las de inventario por separado: 12/12.
- Migraciones: sí (`20261015164023_InventarioYRevisionSalida`). Cuatro `CreateTable` (`inventario_base_item`, `contrato_inventario_item`, `revision_inventario`, `revision_inventario_item`) más una columna en `contrato`. `xmin` quitado a mano de los cuatro `CreateTable` y `GRANT SELECT, INSERT, UPDATE` sobre las cuatro a `rent_app`, que sí queda probado en runtime (la API de las pruebas corre con `bd.CadenaApp` y escribe las cuatro tablas). El `GRANT` **no incluye `DELETE`**: ninguna de las cuatro se borra nunca.
- NO verificado: `dotnet ef database update` contra la base de desarrollo del 5433 (harían falta credenciales de `.env.dev`). **El PDF del contrato sigue sin inventario**: `DocumentoContratoPdf` no lo imprime, y hasta que F2-08 llegue a esa parte el documento firmado no incluye la foto del día de la entrada.
- Riesgo / pendiente:
  - ~~**La condición de liquidación no está cableada**, solo escrita y probada.~~ **Resuelto en F2-09:** `LiquidarAsync` la llama dentro de su `antesDeGuardar`, así que el criterio de F2-08 ya se cumple.
  - ~~**La revisión no se puede firmar en un contrato finalizado**, y F2-09 liquida desde finalizado.~~ **Resuelto en F2-09:** esta decisión mía era un callejón sin salida y se corrigió. La revisión **sí** se puede firmar en `Finalizado` (el cliente se va en la fecha efectiva y el contrato finaliza ese mismo día), y sigue rechazada en `Borrador`, `PendienteAprobacion` y `Aprobado`.
  - **"Ausente" y "dañado" no se liquidan igual**: los dos cuentan como faltante pero solo el ausente desaparece. Eso es lo que dice §3.6 ("faltantes/daños") y no hay regla de importe por elemento: el total es el que propone el gerente. Si el negocio quiere un importe por línea, es una columna más en `revision_inventario_item`.
  - **No hay fotografías ni valoración**: la revisión dice qué falta, no cuánto vale. El importe lo pone una persona.
  - **No hay reactiva de una revisión**: un contrato liquidado con una revisión mal tomada se queda con ella. Es coherente con "firmar es un hecho histórico", pero es una puerta que no existe a propósito.
- Commit/PR: sin commit (rama `tarea/F2-operacion-alquiler`).

---

## [15/10/2026 18:20] FEAT · F2-07 · Catálogo de plantillas y anexos, congelación de la plantilla al aprobar y PDF del contrato (implementador)
- Qué:
  - Catálogo nuevo: `Data/PlantillaContrato.cs` y `Data/PlantillaAnexo.cs` (catálogos), `Data/ContratoPlantillaCongelada.cs` (la copia), `Data/SeccionTexto.cs` (los bloques de texto y los convertidores a `jsonb`) y `Dominio/TipoAnexo.cs`. Migración `20261015154918_PlantillasYAnexos` con tres tablas.
  - `Services/ServicioPlantillas.cs` (alta, edición, desactivación y listado de los dos catálogos, congelación y PDF), `Services/DocumentoContratoPdf.cs` (QuestPDF, `docs/DESIGN.md §7`), `Controllers/PlantillasController.cs` y `Controllers/AnexosController.cs` (8 endpoints) y `GET /api/v1/contratos/{id}/pdf` en `ContratosController`.
  - 18 tests en `backend/AtlasRent.Tests/Plantillas/`. La suite completa pasa **436/436**.
- **La congelación es una copia, no una referencia.** F2-05 guardaba el código de la plantilla y su fecha, que es congelar de nombre: el día que alguien edita la plantilla, todos los contratos aprobados cambian solos, que es justo lo que §3.5 quiere evitar. Al aprobar se copia el texto de la plantilla y el de los anexos que declara a `contrato_plantilla_congelada`, en la misma transacción que la aprobación; el PDF sale de ahí. **Tabla propia y no una columna JSON del contrato** porque el PDF y la auditoría la leen sin cargar el contrato ni unir secciones, y porque `EXISTS` sobre esa tabla es la prueba directa de que un contrato tiene copia congelada. No lleva `updated_at` a propósito: una copia que se pudiera corregir en silencio dejaría de ser la copia de lo que se firmó.
- **El contrato sigue apuntando a la plantilla por `codigo` (texto), no por `Guid`.** Es lo que ya traían F2-05 (`contrato.plantilla_contrato_id`) y F1-10 (`inmueble.plantilla_contrato_por_defecto`); cambiar el tipo de esa columna habría tocado contrato, DTO, huella y una quincena de pruebas sin ganar nada, y el identificador interno de la plantilla no lo usa nadie más. La plantilla **se resuelve al aprobar**, que es cuando se copia: en borrador puede apuntar a una plantilla que el responsable esté preparando, y si no existe o está desactivada la aprobación falla con 409 en vez de congelar un contrato sin texto. Con esto cambia una cosa de F2-05: **aprobar exige que la plantilla exista y esté activa en el catálogo**; antes daba aprobar con cualquier texto. Las 51 pruebas de contratos lo cubren sembrando `estandar` y `larga-2026` en `ContenedorBdFixture`.
- **Las plantillas no tienen categoría, y por eso no hay test de "dos plantillas activas de la misma categoría".** §3.5 define categorías para los documentos del inmueble (§3.1) y "uno activo por tipo" para los anexos, pero no para las plantillas de contrato. Lo que las ordena es el `codigo`, único en la base: no puede haber dos plantillas con el mismo nombre de uso. Añadir categorías sería inventar una regla que nadie ha escrito y, de paso, impediría tener a la vez una plantilla vigente y otra para una campaña. En su lugar el test `NoPuedeHaberDosPlantillasConElMismoCodigo` comprueba la regla que sí existe.
- **La plantilla declara qué tipos de anexo necesita** (`anexos_tipos`), que es lo que hace que el PDF lleve "los anexos que le corresponden" sin inventar un vínculo que §3.5 no define. Al congelar se copia el anexo **activo** de cada tipo declarado; un tipo declarado sin anexo activo no bloquea la aprobación y simplemente no se copia. Queda un `TODO(A#)` en el código por si el negocio quiere lo contrario.
- **Los cuatro `TipoAnexo` (`Normas`, `Inventario`, `Mantenimiento`, `Otros`) son un supuesto mío, con `TODO(A#)`.** La especificación dice "catálogo, uno activo por tipo" y no dice qué tipos hay.
- **Permisos, sin crear ninguna acción:** lecturas con `cliente.ver` (fila 2 de §2), crear y editar con `contrato.borrador` (fila 5: el empleado prepara los documentos que van al cliente) y **desactivar con `contrato.estado`** (fila 6, gerente y admin), porque retirar algo del catálogo es la misma decisión de negocio que aprobar o dar de baja. Ningún GET nuevo quedó con acción de escritura.
- **Contenido de usuario.** El texto de la plantilla y el de los anexos se guarda y se imprime tal cual: no hay campo de HTML ni de marcado, los DTO devuelven secciones y no algo ejecutable, y el PDF los escribe con los elementos de texto de QuestPDF, que no interpretan nada. Regla aplicada: `docs/DESIGN.md §7` ("Los textos de usuario (descripciones, notas) se escriben como texto plano; nunca se interpretan como formato"), junto con `docs/SEGURIDAD.md`.
- Verificado: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores. `dotnet test -c Release` → **436/436**, código de salida 0 (5m36s): 418 previos + 18 nuevos. Las pruebas de contratos por separado: 51/51.
- Migraciones: sí (`20261015154918_PlantillasYAnexos`). `xmin` quitado a mano de los tres `CreateTable` y `GRANT SELECT, INSERT, UPDATE` sobre las tres tablas a `rent_app` (que sí queda probado en runtime: la API de las pruebas corre con `bd.CadenaApp`, `ApiTestFactory.cs:107`, y escribe plantilla, anexo y copia congelada).
- NO verificado: `dotnet ef database update` contra la base de desarrollo del 5433 (harían falta credenciales de `.env.dev`). **Nadie ha visto el PDF con los ojos**: los tests comprueban que empieza por `%PDF`, que pesa lo que tiene que pesar y que el contenido congelado no cambia; el diseño visual está sin revisar, como el de la cotización de F2-02.
- Riesgo / pendiente:
  - **El PDF no incluye inventario** (`TODO(A#)` en `DocumentoContratoPdf`): es de F2-08 y hasta que exista se imprimen datos inventados. Cuando exista, el inventario se congela igual que la plantilla.
  - **No hay reactivación.** Desactivar es el final de la vida de una plantilla o de un anexo; si un día hace falta recuperarlos, es un endpoint más con `contrato.estado`, no un campo editable.
  - **Aprobar sin plantilla activa es un 409.** Es una regla nueva y deliberada, pero significa que un contrato creado con una plantilla mal escrita no se puede aprobar hasta corregirla: es el comportamiento correcto, aunque moleste si el front no valida el selector.
  - **El texto de la plantilla se compara en las pruebas por bytes del PDF, ignorando `/CreationDate` y `/ModDate`.** QuestPDF escribe el texto como índices de glifo de una fuente incrustada con subconjunto, así que no se puede leer del PDF sin una fuente inversora: comparar el archivo entero, con las fechas de generación neutralizadas, es lo que deja comprobar que el documento sale de la copia congelada.
  - Las fuentes Geist y Geist Mono del design system siguen sin incrustar (`TODO(A#)`), igual que en el PDF de cotización.
- Commit/PR: sin commit (rama `tarea/F2-operacion-alquiler`).

---

## [15/10/2026 17:40] FIX · F2-06 (cierre) · Verificación del orquestador y tres correcciones de permisos (orquestador)
- Qué:
  - Verificación independiente de F2-06 por el orquestador: `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **418/418**, **código de salida 0** (5m23s). Ya lo habían corrido dos veces el implementador (408 y 418); esta es la tercera, del orquestador.
  - **Alta, edición y cancelación de incrementos** (`ServicioIncrementos.cs`, 5 endpoints, migración `20261015151802_CancelacionDeIncrementos` con dos columnas y sin tablas nuevas): lo había pedido como apéndice al implementador porque sin él la tarea cumplía su criterio pero la regla de §3.5 **no era usable**. Un incremento no se borra, se cancela, y solo mientras el trabajo no lo haya aplicado.
  - **Auditoría de permisos de solo lectura**, pedido por el orquestador: 7 GET con acción de escritura, corregidos.
- **Dos correcciones de permisos que eran culpa del orquestador, no del código:**
  1. Los dos GET nuevos de incrementos y bloques usaban `contrato.borrador` porque el orquestador se lo había indicado en esta misma tarea, **contra el criterio ya aceptado en F2-05** (leer usa `cliente.ver`). Repetí el error y el implementador lo señaló sin tocarlo.
  2. La auditoría encontró **los cinco GET de `CotizacionesController`** (`Listar`, `Leer`, `ParaReserva`, `ParaContrato`, `Pdf`), que usaban `factura.borrador` —también acción de escritura— por una decisión del orquestador en F2-02. El implementador los cambió a `factura.ver` sin autorización expresa.
- **Decisión del orquestador sobre ese punto 2: se acepta, tras comprobarlo.** `MatrizPermisos` deja `FacturaVer` y `FacturaBorrador` **las dos en el techo de los tres roles**, así que el cambio no da acceso nuevo a nadie hoy y evita que mañana un recorte de permisos deje al usuario sin poder leer lo que antes leía. No era una escalada de privilegios. Queda escrito en el controlador por qué.
- **Lo que no se cambió, y por qué:** los dos GET de documentos de inmueble (`GET /inmuebles/{id}/documentos` y `.../archivo`) siguen con `inmueble.documento`, porque esa acción no es un permiso de escritura sino el ámbito de la fila 4 de §2: pasarlos a `cliente.ver` daría al empleado los PDF de propiedad, que su techo de roles le excluye. Habría sido una regresión de la matriz.
- Verificado por el orquestador leyendo `MatrizPermisos.cs` (líneas 34-66), no de palabra: los techos de gerente y empleado incluyen `FacturaVer` y `FacturaBorrador`.
- NO verificado (corregido el 15/10 18:30): `dotnet ef database update` no se ha corrido contra la BD de desarrollo, por credenciales. La frase "el `GRANT` a `rent_app` no está probado en runtime" que aparece en otras entradas de este CHANGELOG **es falsa y queda desmentida aquí**: `ApiTestFactory` configura el host con `bd.CadenaApp` (`Infra/ApiTestFactory.cs:107`), así que los tests de integración se ejecutan con el rol de la aplicación y **sí ejercitan el `GRANT`** de todas las tablas que se escriben por API. Tampoco hay test de la auditoría que escriben estas mutaciones ni de su idempotencia por `Idempotency-Key` en el alta.
- Migraciones: sí (`20261015144049_RenovacionesYIncrementos` y `20261015151802_CancelacionDeIncrementos`).
- Riesgo / pendiente:
  - **El trabajo ahora filtra los incrementos cancelados.** Es un predicado (`!i.Cancelado`) y sin él la cancelación sería decorativa, pero es lo único que se ha tocado del trabajo: la idempotencia no se ha vuelto a tocar.
  - La **fecha de vigencia de un incremento puede ser pasada**, a propósito: es lo que hace que un incremento atrasado no se pierda, porque el trabajo lo coge en su siguiente pasada. Quien lo lea puede pensar que es un descuido; está comentado.
  - El **tope del 50 % por incremento** es un valor elegido por el implementador, no viene de la especificación. Por encima de la mitad, subir la renta así es una renegociación y va por `AplicarCambiosAsync` con su versión y su motivo. Si el negocio tiene otro umbral, es una constante.
  - Un contrato parado muchos meses entra por **todos** los bloques que debería haber tenido, en una sola transacción. Es una decisión del implementador y su motivo es correcto: la renovación automática no depende de que nadie decidiera nada, así que los bloques perdidos no son bloques rechazados, son bloques que la máquina no llegó a registrar.

---

## [15/10/2026 17:55] FEAT · F2-06 · Alta, edición y cancelación de incrementos de renta (implementador)
- Qué:
  - `Services/ServicioIncrementos.cs` (nuevo), `Data/ContratoIncremento.cs` (columnas `cancelado` y `cancelado_at`), `DTOs/ContratoDtos.cs` (`ContratoIncrementoPeticion`), `Controllers/ContratosController.cs` (5 endpoints nuevos) y `Program.cs` (DI).
  - Endpoints: `POST /api/v1/contratos/{id}/incrementos`, `GET /{id}/incrementos`, `GET /{id}/incrementos/{incrementoId}`, `PUT /{id}/incrementos/{incrementoId}` y `POST /{id}/incrementos/{incrementoId}/cancelacion`.
  - Migración `20261015151802_CancelacionDeIncrementos` (dos columnas, ninguna tabla nueva).
  - 10 tests nuevos en `backend/AtlasRent.Tests/Renovaciones/IncrementoContratoTests.cs`.
- Por qué: **sin alta de incrementos la regla de §3.5 no era usable**: la tabla solo se llenaba desde la base y el negocio no tenía forma de programar una subida de renta. Correctión de una omisión de la entrega anterior, señalada por el orquestador.
- Reparto de permisos:
  - Lecturas (`GET`) → `cliente.ver`.
  - Crear, editar y cancelar → `contrato.estado`, el mismo permiso que ya exige `AplicarCambiosAsync` de F2-05 para cambiar la renta: subir la renta de un contrato en vigor es una decisión de gerente. El empleado lee y los intentos de escritura le dan 403.
  - El permiso se comprueba en el servicio y no solo en el atributo, con el mismo motivo que en `ServicioContratos.ExigirEstadoAsync`.
- Decisiones del implementador:
  - **Un incremento no se borra, se cancela**, y solo mientras el trabajo no lo haya aplicado. Aplicado o cancelado, editar y cancelar dan 409 con el motivo: el efecto ya está en la renta y en una versión, y eso es un cambio de contrato, no editar una nota.
  - **Tope del 50 %** en una sola vez (`TopeDeSubida`, con su comentario). Por encima de la mitad, subir la renta así es una renegociación, que va por `AplicarCambiosAsync` con su versión y su motivo. Cero y negativo se rechazan también: bajar la renta no es un incremento.
  - **Un incremento solo se programa en contrato activo**: en borrador se cambia la renta en el propio contrato y en baja programada la renta está acabando.
  - **La fecha de vigencia puede ser pasada**, que es lo que hace que el "atrasado" no se pierda: el trabajo lo coge en la pasada siguiente, con su bloque.
- **Toque del job, mínimo y necesario**: `ServicioRenovaciones` filtra ahora los incrementos cancelados (un predicado). Sin eso, cancelar no serviría de nada: el trabajo aplicaría igual un incremento que el negocio dio por deshecho. **No se toca la idempotencia ni ninguna otra cosa del trabajo.**
- **Auditoría de permisos (pedida por el orquestador)**: los GET de solo lectura con acción de escritura encontrados fueron **los 5 de `CotizacionesController`** (`Listar`, `Leer`, `ParaReserva`, `ParaContrato`, `Pdf`), que usaban `factura.borrador`, y **los 2 míos de `ContratosController`**. Los de cotizaciones se han cambiado a `factura.ver`; los míos, a `cliente.ver`. En `InmueblesController` los GET de documentos usan `inmueble.documento` y **se han dejado como están**: no es un permiso de escritura sino el ámbito de la fila 4 de §2, que deja los documentos del inmueble en gerente y administrador. Pasarlos a `cliente.ver` daría al empleado los PDF de propiedad, que el techo de roles le excluye hoy.
- Verificado: `dotnet build -c Release -warnaserror` (0 advertencias, 0 errores) y `dotnet test -c Release` con **418/418** y código de salida 0 (408 de la entrega anterior más 10 nuevos). Los tests que importan: `ElGerenteCreaIncrementosYElEmpleadoNo` (gerente crea, empleado 403, sin sesión 401, y la renta no se mueve), `UnIncrementoConLaColumnaEquivocadaSeRechaza` (3 combinaciones), `UnPorcentajeFueraDelLimiteSeRechaza` (80, 0 y −3 fuera; 50 dentro), `UnIncrementoAplicadoNoSeEditaNiSeCancela`, `UnIncrementoCanceladoNoSubeLaRenta` y `UnIncrementoRetroactivoLoCogeElTrabajoEnLaPasadaSiguiente`.
- NO verificado: `dotnet ef database update` no se ha corrido contra la BD de desarrollo, por credenciales. El `GRANT` a `rent_app` **sí** queda ejercitado por los tests de integración, que corren con el rol de la aplicación (desmentida en la entrada de las 18:30).
- Migraciones: sí (`20261015151802_CancelacionDeIncrementos`). Sin tablas nuevas y sin `GRANT` nuevo: los permisos son de tabla y la migración anterior ya dio SELECT, INSERT y UPDATE sobre `contrato_incremento`.
- Riesgo / pendiente: ver la respuesta del implementador.

---

## [15/10/2026 17:20] FEAT · F2-06 · Renovación automática por bloques e incrementos de renta (implementador)
- Qué:
  - `Dominio/TipoIncremento.cs`, `Data/ContratoBloque.cs`, `Data/ContratoIncremento.cs`, `Services/ServicioRenovaciones.cs`, `Jobs/TrabajoRenovacionContratos.cs`, `DTOs/ContratoDtos.cs` (dos respuestas), `Controllers/ContratosController.cs` (`GET /{id}/incrementos` y `GET /{id}/bloques`), `Data/AtlasRentDbContext.cs`, `Jobs/ProgramacionJobs.cs` (trabajo nuevo a las 06:00) y `Program.cs` (DI).
  - Migración `20261015144049_RenovacionesYIncrementos`: tablas `contrato_bloque` y `contrato_incremento`.
  - 11 tests nuevos en `backend/AtlasRent.Tests/Renovaciones/` y 1 en `Jobs/ProgramacionJobsTests.cs`.
  - `Data/Contrato.cs`: el `TODO(A#)` de F2-05 se cierra y en su lugar quedan las navegaciones `Bloques` e `Incrementos`.
- Por qué: criterio de aceptación de F2-06, "ejecutar el job dos veces no duplica", y las dos frases de §3.5 que lo obligan: la renovación por bloques "queda en el historial, sin anexo ni firma nueva" y los incrementos, "% o importe fijo, con fecha", "se aplican solos con aviso interno".
- Decisiones del implementador, todas comentadas en el código:
  - **Por bloques, en bucle**: un contrato vencido hace meses entra por todos los bloques que debería haber tenido, en una sola transacción, y el bucle para en cuanto el bloque en curso cubre hoy.
  - **La idempotencia tiene dos capas**: la condición (`FechaFinVigente` anterior a hoy en Santo Domingo, D16) y el indice único `(contrato_id, numero)` de `contrato_bloque` para dos ejecuciones simultaneas.
  - **La alerta va dentro de la transacción del incremento**, no despues: si el proceso muriera entre el commit y el aviso, el incremento quedaria aplicado sin que nadie se enterara y no volveria a avisar.
  - **Un cambio de renta por incremento entra por `contrato_version`**, con su alcance, o "los cambios de renta afectan solo a facturas futuras" dejaria de ser cierto para los incrementos.
  - **Porcentaje e importe en columnas separadas** (`numeric(18,4)` y `numeric(18,2)`) con un CHECK, porque un importe fijo es dinero en la moneda del contrato y un porcentaje es un tipo; mezclarlos en una columna es como un importe acaba tratado como porcentaje.
- Verificado: `dotnet build -c Release -warnaserror` (0 advertencias, 0 errores) y `dotnet test -c Release` con 408/408 y codigo de salida 0.
- Migraciones: sí (`20261015144049_RenovacionesYIncrementos`).
- Riesgo / pendiente: la primera entrega de esta tarea dejó el alta de incrementos sin hacer, y los GET de lectura con `contrato.borrador` en vez de `cliente.ver`; las dos cosas están corregidas en la entrada de las 17:55. No verificado: el `GRANT` a `rent_app` no baja a ese rol en ningún test.
- Commit/PR:

---

## [15/10/2026 16:30] FEAT · F2-04 · Calendario de disponibilidad calculado sobre la capa de ocupación (orquestador)
- Qué:
  - `Dominio/TipoDiaCalendario.cs`, `DTOs/CalendarioDtos.cs`, `Services/ServicioCalendario.cs`, `Controllers/CalendarioController.cs` (`GET /api/v1/calendario`), más 10 tests en `backend/AtlasRent.Tests/Calendario/`.
  - **Sin migración y sin ninguna tabla nueva**: es una consulta de solo lectura.
- Por qué: criterio de aceptación, "muestra contratos, reservas, medios días y mantenimiento". La instrucción decisiva fue negativa: **el calendario lee `ocupacion_rango` y `ocupacion_dia` (F2-01), no `contrato` ni `reserva`**. Si reconstruyera la ocupación contando documentos, desharía la carrera que F2-01 cerró y cualquier hueco se convertiría en ocupación fantasma en pantalla. Verificado a mano en el código: el servicio solo consulta `OcupacionRango`, `OcupacionDia`, `Unidad` e `Inmueble`, con `AsNoTracking()` y filtrando por `Bloquea = true`.
- **Corrección del orquestador sobre el informe del implementador.** El informe afirmaba que `dotnet test -c Release` devuelve "exit 5, no se ejecutaron pruebas" en esta máquina y que había que usar el ejecutable nativo del runner de xUnit v3. **Es falso**: el orquestador lo ha corrido con esa misma orden seis veces en este bloque (299, 312, 337, 356, 386 y 396 tests), todas con salida 0 y recuento real. Queda registrado porque una instrucción de verificación falsa en un informe puede acabar en `.github/workflows` y romper el CI sin que nadie lo ejecutara antes.
- Decisiones y sus motivos (comentados en el código):
  - **La respuesta es siempre una lista de unidades**, y acepta `?unidadId=` o `?inmuebleId=`, de modo que el mismo componente pinta el detalle de una unidad y el inmueble entero sin dos APIs. Pedir por inmueble son unas diez líneas en el servicio.
  - **El estado manual de la unidad vive en `estadoManual` de la unidad, no repetido día a día**: 366 días × N unidades serían decenas de miles de entradas redundantes. Su efecto se ve en `tipo`, `disponible` y `motivo` de cada día.
  - **Desempate explícito**: contrato > reserva mensual > reserva corta > mantenimiento > estado manual. En la práctica casi nunca hace falta (los triggers de F2-01 impiden que un contrato y un medio día caigan el mismo día), pero la respuesta tiene que ser determinista, no la que salga del orden de las consultas. Los motivos que no ganan no se pierden: van todos en `ocupaciones`.
  - **Rango máximo de 366 días**, con el límite explained en el código: acotar lo que barre `ocupacion_rango` sin trocear la lectura de un año completo.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **396/396** (386 previos + 10 nuevos, 4m34s) con **código de salida 0**. Los tests que importan: `UnaOcupacionLiberadaNoAparece` (un contrato finalizado sale libre; es el que falla si se lee sin `Bloquea = true`), `ElCalendarioMuestraLosMediosDias` (los **dos** medios días del mismo día salen los dos, no uno), `ElCalendarioEsSoloLectura` (tres GET no crean ni liberan nada) y `ElCalendarioMuestraElMantenimientoDeLaUnidad` (estado manual sin ninguna ocupación con fecha).
- NO verificado: **no hay validación visual de pantalla**: el calendario es API y las pantallas siguen marcadas como pendientes con `TODO(A#)` en `frontend/src/navegacion/menu.ts`. El filtro por `inmuebleId` existe y compila pero **no tiene test**. Tampoco se ha probado `Mantenimiento` como origen con fecha, porque hoy ninguna escritura lo produce, ni `fuera de servicio` como estado manual (el código lo trata igual que `mantenimiento`, pero solo se prueba `mantenimiento`).
- Migraciones: no.
- Riesgo / pendiente:
  - **Falta el resumen agregado por unidad** (días libres, ocupados, en mantenimiento) para pintar un edificio de un vistazo. El implementador lo dejó fuera a propósito y con `TODO(A#)`, porque §2 dice que al empleado nunca se le dan totales agregados en ningún endpoint y un recuento por unidad es exactamente eso. **Es una decisión de negocio pendiente, no un descuido**: si se quiere, hay que decidir primero a quién se le enseña.
  - El rango de la API es **inclusivo**, al contrario que el `[inicio, fin)` interno de `ocupacion_rango`. Es a conscious y documentado, pero es una asimetría entre la API y la tabla que conviene tener presente al escribir el frontend.

---

## [15/10/2026 16:05] FEAT · F2-05 · Contratos con aprobación que caduca si el contrato cambia, y versionado (orquestador)
- Qué:
  - `Data/Contrato.cs`, `Data/ContratoVersion.cs`, `Data/SolicitudAprobacion.cs`, `Dominio/EstadoContrato.cs`, `Dominio/ReglaRenovacion.cs`, migración `20261015133339_Contratos`, `Services/ServicioContratos.cs`, `Services/HuellaContrato.cs`, `Controllers/ContratosController.cs` (13 endpoints), `DTOs/ContratoDtos.cs`.
  - Flujo completo de §3.5: `Borrador → PendienteAprobacion → Aprobado → Activo → BajaProgramada → Finalizado → Liquidado`, más `BajaProgramada → Activo` para anular una baja.
  - 30 tests nuevos en `backend/AtlasRent.Tests/Contratos/`.
  - Dos ficheros de F2-03 tocados **solo en comentarios** (`ServicioReservas`, `EstadoReserva`): decían "al aprobar un contrato" cuando el conflicto se pone al activar. Sin cambio de lógica ni de tests de F2-03.
- Por qué: es la tarea donde el employee's no puede hacer algo y hay que demostrarlo en serio. Los dos criterios de aceptación del plan:
  - **"Empleado no puede aprobar ni por API"** → `ElEmpleadoNoPuedeAprobarPorApi` (403 exacto) y `ElEmpleadoTampocoAprobarPorServicio`, que llama al servicio directamente saltándose HTTP. El permiso `ContratoEstado` se comprueba en `ServicioContratos.ExigirEstadoAsync` contra el techo de `MatrizPermisos` en los **siete** métodos de estado y en el cambio en activo, no solo en aprobar. Con `[Accion]` solo, un trabajo o un endpoint mal cableado lo esquivarían.
  - **"Solicitud que caduca si cambia"** → `LaSolicitudCaducaSiElContratoCambia`: editar caduca la solicitud, aprobar da 409 y hay que reenviar.
- Decisiones del orquestador, todas comentadas en el código:
  - **La huella no incluye el estado ni las marcas del cambio.** SHA-256 sobre una cadena canónica de 14 líneas (`contrato.v1`, cultura invariante, importes a dos decimales fijos). Si incluyera el estado, guardar "pendiente" cambiaría la huella recién calculada y el contrato se caducaría solo al enviarlo. Test: `AprobarRecienEnviadoFunciona`.
  - **La caducidad se comprueba por dos vías**: al editar se marca la solicitud `Caducada` con su motivo (no se borra, la bandeja de F2-10 lo necesita) y al aprobar se re-compara la huella, por si el cambio llega por fuera del servicio.
  - **El candado de la unidad va al activar, no al aprobar.** §3.5 congela el inventario al activar, y bloquear en "aprobado" dejaría la unidad ocupada desde un estado del que todavía se puede rechazar, obligando a limpiar candados a mano.
  - **Editar un contrato pendiente está permitido y caduca la solicitud.** Si no existiera la edición, tapar el hueco prohibiéndola obligaría a rechazar el contrato entero para corregir un dato.
  - **En activo no se cambian cliente, unidad, facturadora, inicio ni duración inicial.** Cambiar de titular o de unidad es otro contrato.
  - **Sin caducidad temporal por plazo**: no hay número de días en la especificación y no se inventa. Hay `Resultado` + `MotivoCaducidad`.
- **Desviación del implementador sobre mi instrucción, correcta y aceptada por el orquestador:** le pedí que leer contratos usara `Acciones.ContratoBorrador`; lo cambió a `Acciones.ClienteVer`. La matriz §2 fila 2 es literalmente "Ver clientes, unidades, reservas y contratos", y `contrato.borrador` es permiso de escritura: usarlo para listar dejaría al gerente sin contratos si se recortara ese permiso.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; suite completa **386/386** (356 previos + 30 nuevos, 4m24s). Leídos a mano: la comprobación de permiso en los siete métodos de estado, la huella y su exclusión del estado, la migración con `xmin` fuera y `GRANT` sin `DELETE`, y el diff de los dos ficheros de F2-03 (solo comentarios).
- NO verificado: `dotnet ef database update` contra la BD de desarrollo del 5433 (necesita credenciales de `.env.dev`, que por AGENTS.md §6 no se leen). Una afirmación de esta entrada era incorrecta y está desmentida en la de las 18:30: **el `GRANT` a `rent_app` sí se ejercita en runtime**, porque los tests de integración levantan el host con `bd.CadenaApp`. Tampoco se ha probado la numeración de versiones con **dos cambios de renta simultáneos** (el índice único es la red, pero no hay test de concurrencia), ni que un cambio de renta no toque lo ya facturado (las facturas son de F3-01).
- Migraciones: sí (`20261015133339_Contratos`).
- Riesgo / pendiente:
  - **Ojo con la reserva en conflicto**: al activar el contrato, la reserva en conflicto **suelta su candado** porque dos candados vivos solapados no pueden existir (lo impone la base de F2-01) y porque con contrato vigente quien manda sobre la unidad es el contrato. `MarcarEnConflictoAsync` de F2-03 sigue sin soltar por sí misma; los comentarios de los dos ficheros lo dejan escrito.
  - `LiquidarAsync` solo cambia el estado: la liquidación de verdad (inventario de salida, fianza y saldos) la firma F2-09, y necesita las tablas de fianza e inventario que aún no existen.
  - Quedan `TODO(A#)` con la firma de cada trabajo siguiente: F2-06 en `FechaFinVigente`, F2-07 en `PlantillaContratoId`, F2-08 en `ActivarAsync` (junto al candado), F2-09 en `LiquidarAsync`, F2-10 en `SolicitudAprobacion` y en `ContratoDtos`, que ya expone `Solicitudes` por contrato.

---

## [15/10/2026 15:25] FEAT · F2-03 · Reservas mensual y corta con bloqueo en la misma transacción y alerta a 7 días (orquestador)
- Qué:
  - `Data/Reserva.cs`, `Data/ReservaDia.cs`, `Dominio/TipoReserva.cs`, `Dominio/EstadoReserva.cs`, migración `20261015124746_Reservas`, `Services/ServicioReservas.cs`, `Controllers/ReservasController.cs`, `DTOs/ReservaDtos.cs`.
  - `Jobs/TrabajoAlertaReserva.cs`: job diario a las **05:00 AST**, detrás de la copia (02:00), la integridad (03:00) y la vigilancia (04:00), registrado en `ProgramacionJobs.Todos` y en DI.
  - `ServiceDisponibilidad`: un método nuevo, `SincronizarAsync`, que hace la diferencia al editar (suelta lo que desaparece y ocupa lo nuevo) sin borrar ni duplicar el bloqueo.
  - `GuardadoAuditado`: parámetro `enTransaccionDelLlamador` para que F2-05 marque el conflicto desde su propia transacción.
  - `MarcarEnConflictoAsync(id, usuario, ip, ct)`: lo invocará F2-05 al aprobar un contrato. **No libera el bloqueo** (la reserva sigue bloqueando hasta que el gerente decida) y solo acepta `Activa`.
  - 19 tests nuevos en `backend/AtlasRent.Tests/Reservas/`.
- Por qué: es la primera tarea que consume la capa de disponibilidad de F2-01, y ahí es donde se demuestra que la disponibilidad se comprueba en la base sin carreras, no en el servicio.
- **Corrección del orquestador sobre el código entregado (esto es lo importante de la tarea):** el `AtlasRentDbContext` dejaba escrito que el índice `ix_reserva_dia_reserva_id_fecha` **no** era único "a propósito, porque dos personas el mismo día son dos medios días en la misma reserva". Eso es falso y era un agujero de sobreventa: `ocupacion_dia` es único por `(origen, origen_id, fecha)`, así que el segundo medio día de una misma reserva **no tenía dónde bloquearse**, y el trigger de F2-01 habría contado **una** fila donde hay **dos**, dejando entrar a un tercero. El servicio ya lo rechazaba con un error de negocio, pero solo por ese camino: una importación, un trabajo o una inserción directa se lo habrían saltado. Se ha puesto el índice **único** en el modelo, en la migración y en la instantánea, y `dotnet ef migrations has-pending-model-changes` confirma que el modelo y la instantánea siguen de acuerdo. Dos personas el mismo día son dos reservas con un medio día cada una, que es lo que dice `ESPECIFICACION.md` §3.4.
- **Desviación consciente de la orden, razonada y comprobada:** `reserva_dia` **sí** lleva `DELETE` en su `GRANT`, cosa que el orquestador había excluido. Sin él, editar una reserva corta que cambia de día muere en runtime con `permission denied`; el implementador lo comprobó, no es teórico. El motivo es el mismo que ya lleva `cotizacion_linea`: los días son la composición del documento, no el documento. Lo que no se pierde es el *qué*: la auditoría escribe `DiasAnteriores`. `reserva` no lleva `DELETE`: la reserva no se borra nunca.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; suite completa **356/356** (337 previos + 19 nuevos, 3m24s) **después** de poner el índice único, no antes. Los dos criterios de aceptación del plan tienen test propio **de punta a punta por la API**: `DosReservasSolapadasEnParaleloSoloUnaEntra` (dos `HttpClient` distintos, dos usuarios, una barrera `TaskCompletionSource` que suelta las dos peticiones a la vez, sin `Idempotency-Key` para que la segunda llegue a PostgreSQL y no sea un replay) y `DosMediosDiasEntranYElTerceroFalla`.
- NO verificado: `dotnet ef database update` contra la BD de desarrollo del 5433 (necesita credenciales de `.env.dev`, que por AGENTS.md §6 no se leen); la migración aplica desde cero en la suite. Tampoco se ha comprobado que el planificador de Hangfire dispare el trabajo de las 05:00 de verdad: el test llama al trabajo directamente, que es lo que hacen los tests de F1-12.
- Migraciones: sí (`20261015124746_Reservas`).
- Riesgo / pendiente:
  - **La jornada es una invención y necesita confirmación del negocio.** `ESPECIFICACION.md` §3.4 solo dice "media jornada: 4 horas" y no define la jornada. Se ha fijado **08:00–16:00** como jornada de 8 h, para que el medio día sea exactamente la mitad. Si la jornada real es 08:00–18:00, cambian dos constantes de `ServicioReservas` y el 08:00–16:00 deja de ser día completo. Es lo más urgente de confirmar.
  - La alerta de 7 días se manda a **toda** reserva `Activa`, mensual o corta. La especificación §3.4 habla de la mensual que lleva 7 días *sin convertirse*, que es otro momento; se ha seguido la decisión del orquestador y está escrito en el comentario del trabajo.
  - La clave de deduplicación de la alerta es por reserva (`reserva-entrada:{id}`) y sin fecha, para que mover la entrada no repita el aviso.
  - `Completada` y la conversión a contrato **no** tienen camino todavía: son de F2-04 y F2-05, y no hay ningún test que las cubra.
  - El trabajo de F2-01 `SincronizarAsync` que se añadió aquí lo van a usar también los contratos de F2-05: es el único punto donde se puede meter la fila de bloqueo mal y dejarla huérfana.

---

## [15/10/2026 14:35] FEAT · F2-02 · Cotizaciones con el flujo de estados de D18 y PDF básico (orquestador)
- Qué:
  - `Data/Cotizacion.cs` y `CotizacionLinea.cs`, migración `20261015121719_Cotizaciones`, `Services/ServicioCotizaciones.cs`, `Controllers/CotizacionesController.cs`, `DTOs/CotizacionDtos.cs` y `DocumentoCotizacionPdf` con **QuestPDF**.
  - Estados `EstadoCotizacion` (`Borrador`/`Enviada`/`Aceptada`/`Rechazada`) como `int`, y una tabla de transiciones en el servicio: solo `Borrador → Enviada` y `Enviada → Aceptada|Rechazada`. Desde `Aceptada` o `Rechazada` no se vuelve atrás.
  - 25 tests nuevos en `backend/AtlasRent.Tests/Cotizaciones/`.
- Por qué: es el primer módulo de Fase 2 y el que fija el patrón de tabla cabecera + líneas que luego reutilizan facturas y contratos. Tres decisiones son del orquestador y están justificadas en el código:
  - **Una cotización no lleva ITBIS ni retenciones.** La especificación §3.8 las pide para facturas y proformas; meterlas aquí sería inventar reglas de impuestos.
  - **Pasar a Enviada, Aceptada o Rechazada exige `cotizacion.enviar`.** La matriz de roles §2 no tiene fila para "aceptar una cotización" y la más cercana llega desactivada al empleado (D15), que es justo la protección buscada.
  - **Sin número visible de cotización.** La especificación no lo pide y darle uno inventaría una regla de series que es de §3.7 y de las fases de finanzas. Queda un `TODO(A#)` en el código preguntando si hace falta y con qué serie.
  - **Desde Aceptada no se crea nada.** La especificación dice "botón Crear reserva o Crear contrato con los datos copiados, no se crea nada automáticamente", así que hay dos endpoints de lectura (`para-reserva`, `para-contrato`) que devuelven los datos copiados y exigen el estado Aceptada. Las reservas y los contratos son de F2-03 y F2-05.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; suite completa **337/337** (312 previos + 25 nuevos, 2m58s). Leídos a mano: los 9 endpoints llevan `[Accion]`, la migración quita `xmin` y trae `GRANT`, el documento de identidad del cliente solo se descifra para el PDF (D16) y no aparece en respuestas ni logs, y QuestPDF 2026.9.1 es la versión ya fijada en `docs/DOC_TECNICA.md` §2.
- NO verificado: `dotnet ef database update` contra la BD de desarrollo del 5433 (necesita credenciales de `.env.dev`, que por AGENTS.md §6 no se leen). La migración sí aplica desde cero en la suite. El diseño visual del PDF solo está comprobado por el test que verifica que empieza por `%PDF`: nadie lo ha visto todavía, porque las pantallas de cotizaciones son de las que el propio `frontend/src/navegacion/menu.ts` tiene marcadas como pendientes con `TODO(A#)`.
- Migraciones: sí (`20261015121719_Cotizaciones`).
- Riesgo / pendiente:
  - **Licencia de QuestPDF (legal, no técnico).** El código fija `LicenseType.Community`. Verificada en la documentación oficial: es gratuita solo con facturación bruta anual consolidada **< 1 M USD**, y **organismos públicos y empresas cotizadas quedan excluidos** sea cual sea su facturación. Si se deja de cumplir hay 90 días para comprar licencia o dejar de usar la librería, **sin retroactividad**. Además QuestPDF es *source-available*, no una licencia OSI, así que la auditoría de dependencias de `SEGURIDAD.md` §A.4 no puede asumir MIT. Queda anotado en `DOC_TECNICA.md` §2 y **requiere confirmar los datos de la empresa antes de producción**.
  - `Aceptada` y `Rechazada` son estados finales. Si el negocio necesita reabrir una cotización rechazada para corregirla, es un estado más en la tabla de transiciones y su test, no un atajo.
  - La baja es lógica y **solo se permite en borrador**: una cotización enviada no se retira. Es una decisión del orquestador; si hay que poder retirar una enviada, hay que decidir antes qué pasa con la que el cliente ya tiene descargada.
  - Los endpoints `para-reserva` y `para-contrato` son de lectura y no idempotentes por themselves; su consumidor (F2-03, F2-05) es quien debe tratar el caso de que la cotización cambie entre la llamada y el alta.

---

## [15/10/2026 14:10] FEAT · F2-01 · Disponibilidad calculada en la base: exclusión de rangos y capacidad de medio día (orquestador)
- Qué:
  - `backend/AtlasRent.Api/Data/OcupacionRango.cs` y `OcupacionDia.cs`: dos tablas que **son** la disponibilidad. `ocupacion_rango` bloquea por rango (contrato, reserva mensual) con `EXCLUDE USING gist (unidad_id WITH =, daterange(fecha_inicio, fecha_fin, '[)') WITH &&) WHERE (bloquea)`; `ocupacion_dia` bloquea por día suelto (reserva corta, mantenimiento) con el tipo 1 = medio día y 2 = día completo.
  - Dos triggers en la migración `DisponibilidadOcupacion`: uno comprueba la capacidad del día (2 medios días o 1 día completo) y el otro el trigger simétrico que impide que un rango tape días ya reservados. Ambos serializan con `SELECT ... FROM unidad WHERE id = NEW.unidad_id FOR UPDATE`, que es lo que hace que dos altas simultáneas no se cuelen.
  - `Services/ServicioDisponibilidad.cs`: `OcuparRangoAsync`, `LiberarRangoAsync`, `OcuparDiasAsync`, `LiberarDiasAsync` y `EstaLibreAsync`. No abre transacción ni audita: es pieza de llamador y lo usan F2-03 y F2-05 desde su propia transacción.
  - `Problemas.cs`: código `DIS-001` para unidad no disponible. `Program.cs`: el servicio en DI. 13 tests nuevos en `backend/AtlasRent.Tests/Disponibilidad/DisponibilidadOcupacionTests.cs`, todos contra PostgreSQL real.
  - `docs/DOC_TECNICA.md` §4.3 y `docs/PLAN.md` actualizados.
- Por qué: es el primer requisito de AGENTS.md §6 y de la especificación §5 que la disponibilidad se compruebe **en la base**, sin carreras entre dos usuarios. El diseño previsto en DOC_TECNICA §4.3 (una restricción de exclusión en `contrato` y otra en `reserva`) tenía un agujero: una restricción de exclusión no puede abarcar dos tablas, así que un contrato y una reserva mensual del mismo rango se habrían solapado **en silencio**. Por eso el bloqueo vive en una capa propia que escriben los dos servicios en la misma transacción que su fila de negocio. También cambia `reserva_dia (recurso_id, fecha, mitades)` por un tipo explícito, porque el calendario de F2-04 tiene que distinguir "día completo" de "dos medios días".
- Verificado (por el orquestador, no solo el implementador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; suite completa **312/312** (299 previos + 13 nuevos, 2m34s). Los dos criterios de aceptación del plan tienen test propio: `SolapamientoConcurrente` (dos conexiones, barrera de arranque, exactamente una gana) y `DosMediosDiasOKYTerceroFalla`. `MediosDiasConcurrentes` prueba que de tres medios días simultáneos sobreviven exactamente dos, que es donde se ve el bloqueo por unidad.
- NO verificado: `dotnet ef database update` contra la BD de desarrollo del puerto 5433 (necesita las credenciales de `.env.dev`, que por AGENTS.md §6 no se leen). La migración sí aplica desde cero en los tests, con `rent_migrador` sobre una base recién creada. Tampoco se comprobó que el planificador de Hangfire dispare trabajo de F2-01, porque esta tarea no añade jobs.
- Migraciones: sí (`20261015114531_DisponibilidadOcupacion`).
- Riesgo / pendiente:
  - El `GRANT` a `rent_app` **no lleva `DELETE`**, a diferencia de las migraciones anteriores. Es deliberado: liberar es `bloquea = false` (AGENTS.md §6), así que la aplicación no necesita borrar y antes que dejar el permiso puesto está quitarlo.
  - Los enums se mapean como `int` y el trigger compara con los literales `1` y `2`. Si alguien reordena `TipoOcupacionDia`, el trigger se queda comparando contra valores distintos y rompe **en silencio**. Está avisado en el enum y en un comentario dentro del SQL, y los tests lo detectan.
  - `ServicioDisponibilidad` llama a `SaveChanges` sin transacción propia. Quien lo use (F2-03, F2-05) tiene que abrir una antes: si no, el bloqueo se confirmaría aparte de la fila de negocio y quedaría huérfano al fallar el guardado posterior.
  - Reintentar la misma fila de origen devuelve `23505` del índice único `(origen, origen_id)`, que el servicio **no** traduce a error de dominio. Quien lo use debe tratar `23505` como "ya bloqueada".
  - `EstaLibreAsync` es una consulta de painted para el calendario, no una garantía. Si alguien la usa como comprobación previa reintroduce la carrera: la garantía son los triggers.
  - Los SQLSTATE usados son `23001` (triggers) y `23P01` (exclusión); ambos significan "no disponible" para quien llama.

---

## [15/10/2026 11:40] FEAT · F1-12 (cont.) · Vigilancia de que la copia del día exista de verdad (orquestador)
- Qué:
  - `backend/AtlasRent.Api/Jobs/TrabajoVigilanciaCopia.cs`, programado a las **04:00 AST** (después de la copia de las 02:00 y de la integridad de las 03:00), registrado en `ProgramacionJobs.Todos` y en el contenedor de DI.
  - 4 tests nuevos en `backend/AtlasRent.Tests/Jobs/TrabajoVigilanciaCopiaTests.cs` y uno de cadencia en `ProgramacionJobsTests`.
  - `docs/DOC_TECNICA.md` §8.1, `docs/SEGURIDAD.md` A.8 y `docs/PLAN.md` actualizados.
- Por qué: era el único fallo del respaldo que **no produce ningún error visible**. Todo lo demás (volcado vacío, descifrado, rotación) lanza y alguien lo ve. Pero si el proceso estaba apagado a las 02:00, o el planificador no disparó el trabajo, o alguien desactivó el servidor de jobs, no hay excepción, ni log de fallo, ni nada: simplemente no hay copia, y eso es indistinguible de que todo vaya bien hasta que hace falta restaurar. `SEGURIDAD.md` A.8 lo pedía («alerta si un backup falla **o no se ejecuta**») y solo estaba cubierta la primera mitad.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; suite completa **299/299**; los tests de `Jobs/` cubren los tres casos — copia en verde (no avisa), copia ausente (avisa y apunta a la causa), copia presente pero ilegible (motivo distinto) — y que la clave lleva la fecha.
- NO verificado: **el disparo real de este trabajo por Hangfire contra PostgreSQL real.** No hay instancia de desarrollo con el rol `rent_backup` configurada. Lo que está probado es el comportamiento del trabajo, no que el planificador lo dispare a las 04:00.
- Migraciones: no.
- Riesgo / pendiente:
  - La clave de la alerta es `backup-ausente:{fecha}`, distinta de `backup:{fecha}` del fallo. No es duplicar: una dice por qué falló, la otra que a las 04:00 no había copia.
  - Si el respaldo falla a las 02:00, esa noche habrá **dos** alertas. Es deliberado y está documentado, pero si en F2-10 la bandeja satura con ruido habrá que agruparlas.
- Commit/PR: pendiente

## [15/10/2026 11:20] FIX · F1-12 / F1-13 · Fecha de la copia dentro del contenedor, "fallo → alerta" y el frontend en el paquete (orquestador)
- Qué:
  - `ServicioBackup`: el contenedor de copia pasa a `ARBK2` y **lleva la fecha dentro**, protegida por la etiqueta GCM. `VerificarAsync` rechaza todo archivo cuyo nombre no coincida con la fecha de su contenido. `CopiaVerificada` distingue `FechaDelNombre` (puede mentir) de `Fecha` (la del contenido).
  - Tabla `alerta` (migración `20261015090307_AlertaDeFallo`), entidad `Data/Alerta.cs` y `Services/ServicioAlertas.cs`, registrado en DI. `ServicioBackup` deja alerta del fallo del día y del fallo de subida al almacén remoto.
  - Target `AtlasRentPublicarFrontend` en `AtlasRent.Api.csproj`: copia `frontend/dist` a `wwwroot/` en `dotnet publish`.
  - Paso nuevo en la CI que publica y comprueba que `wwwroot/index.html` está en el paquete, con su `setup-node` correspondiente (sin él, el job `backend` compilaría el frontend con el Node que traiga el runner y no con el 24.15.0 de DOC_TECNICA §2).
  - Registros: `docs/BUGS.md` (BUG-001, BUG-002, BUG-003) y notas de DOC_TECNICA §3.1/§8.1 y `SEGURIDAD.md` A.8.
- Por qué: los tres son criterios de aceptación o riesgos altos de F1 que la revisión adversarial dejó abiertos. El detalle de cada uno está en su entrada de `docs/BUGS.md`.
- Verificado (por el orquestador):
  - `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores.
  - Suite backend completa **294/294** en verde, contra migraciones aplicadas desde cero en Testcontainers.
  - `frontend/`: 18/18 tests, `lint` y `typecheck` limpios.
  - H2 a mano: `npm run build` + `dotnet publish` deja `wwwroot/index.html` y 33 ficheros en el paquete; sin `frontend/dist` avisa por consola y publica sin interfaz.
  - El test anti-subversión **comprobado no vacío**: desactivada la validación, el test falla enseñando `Fecha=2026-10-16` frente a `FechaDelNombre=2026-10-17`.
  - La alerta se escribe **sin rol de sesión**, que es como corre el job desde Hangfire (test `LaAlertaSeEscribeSinRolDeSesionPorqueElJobNoTiene`), mirando filas y no que la llamada no rompiera.
  - Segunda verificación adversarial (subagente verificador, PASS CON RESERVAS, sin hallazgos altos) sobre este mismo árbol.
- NO verificado:
  - **La CI no se ha ejecutado nunca**: `AGENTS.md` §5 prohíbe `git push` sin permiso y la rama nunca ha salido de la máquina. Lo que se ha reproducido son los comandos, no el runner.
  - El disparo real del respaldo diario por Hangfire contra PostgreSQL real, y la subida a Google Drive (depende de F0-04).
  - Que la API **publicada** sirva `/`: no arranca sin una base de datos real y no hay instancia con el rol `rent_backup` configurada.
  - Restauración real aplicando un volcado (runbook FP-03, fuera de bloque).
- Migraciones: sí — `20261015090307_AlertaDeFallo` (crea `alerta` con `clave` única e índices; concede `SELECT, INSERT, UPDATE` a `rent_app`; **no** crea la columna `xmin`).
- Riesgo / pendiente:
  - `ARBK2` **no es retrocompatible**: las copias de un binario anterior dejan de abrirse. Al desplegar un binario nuevo hay que regenerar la copia del día.
  - La alerta vive en la misma base del respaldo: si lo que cae es PostgreSQL, no hay dónde escribirla. Cubierto en `SEGURIDAD.md` A.8 y DOC_TECNICA §8.1.
  - Sigue sin implementarse el aviso de **backup que no llega a ejecutarse** (solo se detecta el que falla).
  - `Rotar()` ordena por nombre de archivo: un archivo con fecha futura y que no verifica ocupa una ranura y baja la retención efectiva de 4 a 3. Exige escritura en la carpeta de copias.
- Commit/PR: `38ebc78`, `7dc9eea`, `5207e3a`

## [15/10/2026 13:30] FEAT · F1-12 (cont.) · Disparo diario de los jobs con Hangfire
- Qué:
  - Hangfire 1.8.25 / Hangfire.PostgreSql 1.21.1 en la API, con los jobs de F1-12 programados: **copia de seguridad cifrada a las 02:00 de Santo Domingo** y **verificación de integridad de auditoría a las 03:00**.
  - `backend/AtlasRent.Api/Jobs/`: `OpcionesJobs`, `ProgramacionJobs`, `TrabajoCopiaSeguridad`, `TrabajoIntegridadAuditoria`. `Watchdog/Program.cs` gana el comando `copiar` para lanzar la copia a mano.
  - `db/scripts/04_base_jobs.sql`: la base `atlas_rent_jobs`.
  - `ApiTestFactory` desactiva el servidor de jobs (`Jobs:ServidorActivo=false`) para que los tests no esperen jobs ni hagan copias de verdad.
  - 10 tests nuevos en `backend/AtlasRent.Tests/Jobs/`.
- Por qué: cierra el hueco de F1-12. El backup diario ya existía pero **nunca se ejecutaba**, porque no había nada que lo disparara.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet list package --vulnerable --include-transitive` → sin vulnerabilidades; suite completa en verde.
- NO verificado: **no se arrancó la API con el servidor de jobs activo contra PostgreSQL real**, así que el arranque de Hangfire y la ejecución efectiva de los dos trabajos no están probados de extremo a extremo (sí el montaje del almacenamiento y la creación de tablas). El disparo manual del Watchdog solo está probado por compilación. El dashboard de Hangfire **no** se monta, conforme a `SEGURIDAD.md` A.1.
- Migraciones: no (los esquemas los crea el propio Hangfire; la base se crea con `db/scripts/04_base_jobs.sql`).
- Riesgo / pendiente:
  - **Bug de producción encontrado al integrar:** con Hangfire en un esquema dentro de `atlas_rent`, el `pg_dump` del backup diario **fallaba** con `permission denied for schema hangfire`, porque `pg_dump` hace `LOCK TABLE` de todo lo que encuentra. El backup se rompía a sí mismo. **Solución:** base de datos propia `atlas_rent_jobs`. El rol y la credencial siguen siendo `ConnectionStrings__Migraciones` con la base cambiada en código. Sin migración que conceda nada a `rent_app`.
  - **`Newtonsoft.Json` fijado a 13.0.4**: Hangfire.Core 1.8.25 arrastra 11.0.1, con **CVE-2024-21907 (alta, denegación de servicio por JSON anidado)**. Fijado a la versión parcheada para que ningún proyecto quede con la vulnerable.
  - **Zona horaria**: Hangfire CE 1.8.25 **no entiende `CRON_TZ=`** (es función de la edición de pago) y planifica en UTC. Escribir `0 2 * * *` habría significado las 02:00 UTC, que aquí son las 22:00 del día anterior: la copia salía al final de la noche, no de madrugada. La conversión a UTC está escrita **como cálculo** (`CronDiarioEnUtc`), no como un número mágico, y **lanza si la zona tuviera horario de verano**, porque un UTC fijo no puede querer decir «las 02:00 locales» dos días al año. Hay test que afirma la hora local y test de regresión que falla si el cron vuelve a ser `0 2 * * *`.
  - El job de integridad de auditoría se ejecuta con el rol adecuado porque **con otro rol, RLS le oculta filas y da un falso "todo correcto"**. Razonamiento en un comentario, que es lo que alguien "simplifica" y rompe en silencio.
  - Los demás jobs de §8 que dependen de dominio todavía inexistente **no se programan**: quedan documentados en DOC_TECNICA §8.1.
- Commit/PR: `feat(jobs): disparo diario del backup cifrado y verificacion de integridad de auditoria`.

## [15/10/2026 12:00] FEAT · F1-12 (parcial) · Backup cifrado, rotación, verificación y Watchdog
- Qué:
  - `Api/Services/Backup.cs` y `Api/Services/ServicioBackup.cs`: copia de la base de datos y de los documentos en un contenedor propio, cifrada con AES-256-GCM, en `Backup__CarpetaLocal`.
  - Migración `20261015071955_PermisosLecturaBackup`: `GRANT SELECT` para `rent_backup` y **`ALTER DEFAULT PRIVILEGES FOR ROLE rent_migrador`**, que es la pieza que faltaba (los de `02_basedatos.sql` son `FOR ROLE rent_owner` y nunca se aplicaron, porque las tablas las crea `rent_migrador`).
  - `Watchdog/OperacionesWatchdog.cs` y un `Watchdog/Program.cs` real: restauración y actualización **con copia previa obligatoria**.
  - 26 tests en `backend/AtlasRent.Tests/Backup/`.
  - `db/scripts/01_roles.sql`: `ALTER ROLE rent_backup BYPASSRLS`.
- Por qué: F1-12 del plan. **Queda a medias y no se da por hecha**: la subida a Google Drive depende de **F0-04** y el disparo diario de **Hangfire**, que todavía no está en el repo.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` y `dotnet test -c Release` → **277/277 en verde**; `dotnet list package --vulnerable` sin vulnerabilidades.
- NO verificado: `pg_dump` de producción (aquí no hay cliente de PostgreSQL; los tests ejecutan el `pg_dump` de la imagen vía `docker exec` por socket local, sin contraseña en la línea de comandos); la aplicación del volcado con `psql` (runbook FP-03); el envío de correo de alerta; y el backup real sobre la base de desarrollo.
- Migraciones: sí. `Migrations/20261015071955_PermisosLecturaBackup`.
- Riesgo / pendiente:
  - **El bloqueador que se encontró y se arregló:** `pg_dump` extrae con `COPY … TO STDOUT`, y PostgreSQL rechaza esa sentencia sobre una tabla con RLS activada si el rol no la ignora (SQLSTATE 55000). Con `auditoria` y `documento_inmueble` con RLS, **el volcado fallaba**. Ninguna política lo resuelve: el bloqueo es de la sentencia `COPY`. Solo un superusuario puede conceder `BYPASSRLS`, y se añadió a `db/scripts/01_roles.sql`. **Aplicado también al contenedor de desarrollo.**
  - **El fallo peligroso es el contrario**: degradar ese error para que el backup "pase" produce un volcado **vacío y sin error**. Por eso el servicio comprueba `TieneFilas` y **descifra el archivo del disco para verificarlo** antes de rotar. Un backup que parece bueno y no tiene nada es peor que no tener backup.
  - Formato: `ARBK | v1 | nonce(12) | etiqueta GCM(16) | SHA-256 del claro(32) | cifrado`, en `atlasrent-AAAA-MM-DD.arbk`. El claro es un contenedor con `bd.sql` y `documentos/…`.
  - Rotación a 4 **solo tras verificar**; un fallo no rota. Dos ejecuciones el mismo día son idempotentes.
  - `IAlmacenRemoto` y `AlmacenRemotoDriveNoDisponible` lanzan `NotImplementedException` diciendo que dependen de F0-04. **Sin simulación**: la copia queda solo en local.
  - Riesgo asumido: un `rent_backup` comprometido lee todo, incluidas las tablas con RLS. Es inherente a respaldar la base; se compensa con que el backup va cifrado y con que ese rol no opera la aplicación. **Al restaurar, usar un superusuario, nunca `rent_backup`.**
  - `TODO(A#)`: job diario de integridad de auditoría (**debe correr con `app.rol = admin` o `gerente`**); envío de correo de alerta; firma del paquete de actualización; `Backup__ClaveCifrado` asumida en base64; una base legítimamente vacía sería rechazada por `TieneFilas`.
- Commit/PR: `feat(backup): copia cifrada, verificacion de integridad, rotacion y watchdog con copia previa`.

## [15/10/2026 11:05] FIX · Correcciones de la verificación independiente del bloque F1
- Qué:
  - **MFA: el token provisional y el código de recuperación se gastan con una escritura condicional** (`UPDATE … WHERE consumido_at IS NULL` / `usado_at IS NULL`) y **comprobando el número de filas afectadas**, dentro de la transacción. Antes se leía fuera de la transacción y luego se asignaba la propiedad en memoria: **dos peticiones simultáneas con el mismo token pasaban las dos y abrían dos sesiones**. Es un TOCTOU real, aunque el test existente solo lo comprobaba en serie.
  - **El backend sirve el frontend compilado**: `UseDefaultFiles` + `UseStaticFiles` + `MapFallbackToFile("index.html")`, registrados **solo si existe `wwwroot/index.html`**, para que en desarrollo (Vite aparte) y en pruebas la API arranque igual.
  - `SEGURIDAD.md`: los dos hallazgos nuevos se renumeran a **SEC-007** (secreto TOTP, cerrado) y **SEC-008** (cuerpo de idempotencia, abierto), porque **SEC-005 ya lo ocupaba** la inyección de fórmulas en exportaciones. Actualizadas todas las referencias en `DOC_TECNICA.md`, `CHANGELOG.md` y el código. `SEC-004` pasa a CERRADO (F1-09 y F1-10 hechas).
  - Se corrige la afirmación sobre el guard de D14: es **por línea**, no un análisis del código.
- Por qué: una revisión independiente encontró dos problemas reales que el trabajo previo daba por buenos.
- Verificado: `dotnet build -c Release -warnaserror` sin advertencias. La suite completa se ejecuta tras estos cambios.
- NO verificado: la carrera del MFA **sigue sin reproducirse con peticiones simultáneas reales**; la corrección es análisis estático del patrón y un test secuencial que ya existía. Candidato a `TODO(A#)` con un test de concurrencia.
- Migraciones: no.
- Riesgo / pendiente:
  - **SEC-008 sigue abierto** (cuerpo de `idempotencia` sin cifrar) y **la rotación de clave** sigue sin implementarse.
  - `cliente` no tiene RLS mientras `documento_inmueble` sí: es intencionado (la matriz permite a los tres roles ver clientes, y D16 pide el documento visible completo), pero conviene dejarlo escrito.
  - El bundle de `textos.ts` (≈650 kB) supera el aviso de 500 kB y no está atendido.
- Commit/PR: `fix(seguridad): gasto atomico de los tokens de MFA y el backend sirve el frontend compilado`.

## [15/10/2026 10:20] FEAT · F1-11 · Layout, navegación por permisos y pantalla de acceso
- Qué:
  - Backend: `IdentidadRespuesta` incluye **`permisosEfectivos`** (techo del rol recortado por los permisos por usuario). En `/auth/yo` se lee de `IPolicyService`, ya cargado por el filtro de autorización: **cero consultas extra**. En `MfaController` y `EmergenciaController` (marcados `[AllowAnonymous]`) sí hay una consulta, porque el filtro no corre.
  - Frontend: `src/permisos.ts` (copia literal de `Acciones.cs`), `src/navegacion/menu.ts`, `src/sesion/store.ts`, páginas `Acceso.tsx` y `SinPermiso.tsx`, y `App.tsx` con los tres estados (cargando / sin sesión / con sesión) y enrutador protegido.
  - `vite.config.ts` gana un **proxy de desarrollo** `/api` → `127.0.0.1:5100`, necesario para ver los estados de sesión.
  - 18 tests de Node (menú filtrado por permisos, rutas sin destino, preferencia de tema).
- Por qué: F1-11 del plan. El criterio de aceptación es visual, así que se comprobó abriendo la app de verdad.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` y `dotnet test -c Release` → **251/251**; `npm run lint`, `typecheck` (0 y 0), `test` **18/18** y `build` correcto. Revisé las capturas y **encontré un bug que el subagente no mencionó**: el Panel ofrecía el botón **"Registrar cobro" a un empleado**, que es una acción de gerente y admin (`cobro.registrar`). El menú se filtraba por permisos pero el botón del Panel estaba fijo. **Corregido** condicionándolo al permiso real; el subagente ni lo mencionó en su informe.
- NO verificado:
  - **El menú de dos roles no se ve distinto en pantalla**: con un solo módulo implementado (Panel), empleado y gerente muestran ambos "Panel". La diferencia está probada en los tests del menú y en la respuesta real de la API (empleado: 8 permisos; gerente: 18). El criterio de aceptación (claro/oscuro, preferencia recordada, sin parpadeo) **sí** está comprobado.
  - La captura `01-acceso-sin-sesion` es byte a byte idéntica a `07-acceso-oscuro-tras-cierre`: **la variante clara de la pantalla de acceso nunca se capturó**, ambas son oscuras. La revisión en claro y oscuro del Panel sí es real.
  - No se pudo crear una sesión de admin real porque el MFA es obligatorio y no hay pantalla de segundo factor, así que el menú completo del admin no se vio en navegador.
  - `dotnet ef database update` contra la base de desarrollo (requiere secretos de `.env.dev`).
- Migraciones: no.
- Riesgo / pendiente:
  - **No hay pantalla de segundo factor** (F1-06 dejó el backend). El login solo cubre usuario y contraseña y la app **lo dice explícitamente** en vez de fingir. Es lo que bloquea ver el menú completo del admin.
  - Los 11 módulos sin pantalla (clientes, inmuebles, contratos, facturas, cobros, fianzas, informes, auditoría, bloqueos, usuarios, configuración) quedan declarados en `menu.ts` con `TODO(A#)` y **sin ruta enlazada**: un enlace de menú que da 404 es peor que una entrada ausente.
  - Decisiones que conviene revisar: Cobros se filtra por `cobro.registrar` y Fianzas por `fianza.mover` porque la matriz no tiene acción de lectura para esos módulos; `/componentes` queda tras `configuracion.editar`; el selector de tema vive en la `TopBar`, así que **no se puede cambiar de tema sin sesión**.
  - El proxy de desarrollo de `vite.config.ts` apunta a `127.0.0.1:5100`.
  - Las capturas de la revisión visual quedaron en `.capturas-f1-11/`, **fuera del repositorio**: no se versionan.
- Commit/PR: `feat(frontend): layout, navegacion por permisos, pantalla de acceso y permisos efectivos`.

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

## [15/10/2026 08:40] FEAT · F1-09 · Clientes con AES-256-GCM, índice ciego y cierre de SEC-007
- Qué:
  - `Services/Cifrado.cs`: `ServicioCifrado` (AES-256-GCM con versión de clave y **nonce aleatorio por cifrado**), `DocumentoIdentidad.Normalizar` e `IndiceCiegoDocumento` (HMAC-SHA256 con clave propia).
  - Clientes, contactos y prospectos bajo `/api/v1/clientes` y `/api/v1/prospectos`, con `[Accion]`, paginación de §6, `Idempotency-Key` y auditoría en la misma transacción.
  - Número de cliente por `SEQUENCE` iniciada en `max+1`, sin duplicados bajo concurrencia.
  - **SEC-007 cerrado**: `usuario.mfa_secreto` queda cifrado en reposo con el mismo servicio.
  - Migración `20261015050454_ClientesYProspectos` con `CREATE SEQUENCE cliente_numero_seq` y `USAGE, SELECT, UPDATE` sobre la secuencia (`setval` la exige).
  - 26 tests nuevos en `backend/AtlasRent.Tests/Clientes/`.
- Por qué: F1-09 del plan. D16 exige el documento de identidad visible completo pero cifrado en la base de datos, y la app está en internet.
- Verificado (por el orquestador): `dotnet build -c Release -warnaserror` → 0 advertencias, 0 errores; `dotnet test -c Release` → **232/232 en verde**. Revisé que los tests **leen la fila cruda** y comprueban que el documento en claro no aparece; el del secreto MFA va más lejos: **descifra** el valor guardado con la clave real de la aplicación para demostrar que es un cifrado válido y no un texto cualquiera.
- NO verificado: `dotnet ef database update` contra la base de desarrollo (requiere secretos de `.env.dev`); la migración sí aplica desde cero en cada ejecución de la suite.
- Migraciones: sí. `Migrations/20261015050454_ClientesYProspectos`.
- Riesgo / pendiente:
  - **⚠️ SEC-008, nuevo y abierto: `idempotencia` guarda el cuerpo completo de la respuesta de una mutación en JSON sin cifrar.** Cualquier dato personal que una mutación devuelva queda en claro en esa tabla, aunque el dato principal esté cifrado. Para F1-09 se esquivó **no devolviendo el documento en las respuestas de alta y edición** (solo en `GET /clientes/{id}`), pero el problema es de fondo y volverá con cualquier campo personal nuevo. **Decisión pendiente: cifrar el cuerpo de `idempotencia`, o guardar una versión redactada.** Anotado en SEGURIDAD.md.
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
