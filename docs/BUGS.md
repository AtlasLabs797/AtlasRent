# BUGS.md — Registro de bugs

Cada bug detectado o corregido se registra aquí. Lo más reciente arriba. No se borran entradas.

**Gravedad:** Crítica (dinero, numeración, datos perdidos o expuestos) · Alta (función principal rota) · Media (hay forma de rodearlo) · Baja (cosmético).
**Estado:** ABIERTO · EN CURSO · CORREGIDO · NO SE CORRIGE (motivo)

## Plantilla
```
### BUG-000 · Título corto
- Fecha detectado: dd/mm/aaaa · Fecha corregido: dd/mm/aaaa
- Gravedad: · Estado: · Módulo:
- Detectado por: (persona / test / agente / usuario)
- Síntoma: qué se ve.
- Cómo reproducir: pasos mínimos.
- Causa raíz: por qué pasaba (no el síntoma).
- Solución: qué se cambió y por qué así.
- Test que lo cubre: ruta del test.
- Commit/PR:
- Datos afectados: ¿hay registros en BD que corregir? ¿cómo se corrigieron?
```

## Registro

### BUG-004 · Una línea de factura con identificador ya puesto se guardaba como modificación y devolvía 409
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: facturas / F3-02
- Detectado por: pruebas de F3-02 (`FacturaCalculoYLineasTests`)
- Síntoma: dar de alta la primera línea de una factura recién creada devolvía 409 con «El cambio choca con otra modificación» (un `DbUpdateConcurrencyException`), cuando la factura se acababa de insertar y nadie la había tocado.
- Cómo reproducir: `POST /api/v1/facturas` y acto seguido `POST /api/v1/facturas/{id}/lineas` con una línea nueva.
- Causa raíz: EF no deduce bien el estado de una entidad con clave ya asignada si entra por la colección de navegación de una entidad que ya está seguida: la marca **Modified** en lugar de **Added**, y el `UPDATE` que genera no encuentra la fila. `ServicioCotizaciones` no sufre esto porque añade sus líneas con `contexto.CotizacionLinea.AddRange(...)`, que marca Added explícitamente.
- Solución: la línea se da de alta con `contexto.FacturaLinea.Add(linea)` (el mismo camino que las cotizaciones) y el motivo queda escrito en el servicio, que es donde volvería a pasar si alguien construye una línea a mano.
- Test que lo cubre: `backend/AtlasRent.Tests/Facturas/FacturaCalculoYLineasTests.cs` (las tres pruebas dan de alta líneas).
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno; la migración no había salido del entorno de pruebas.

### BUG-006 · La prueba que borra el tipo de cambio del día se rompe en cuanto un documento congela ese cambio
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026 (corregido por el orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas / F3-02 → F3-06
- Detectado por: pruebas de F3-06 (`CobroMonedaTests`), al ejecutar juntas las pruebas de facturas y de cobros
- Síntoma: `FacturaEmisionTests.SinTipoDeCambioDelDiaLaEmisionFallaYNoGastaElNumero` falla con `23503: update or delete on table "tipo_cambio" violates foreign key constraint "fk_factura_tipo_cambio_tipo_cambio_id"`, en el `QuitarCambioDelDiaAsync` de la línea 296. La prueba de F3-02 es correcta: lo que falla es su helper.
- Cómo reproducir: `dotnet test -c Release --filter "FullyQualifiedName~AtlasRent.Tests.Cobros|FullyQualifiedName~AtlasRent.Tests.Facturas"`. Con las pruebas de cobros solas, F3-02 pasa; con las dos, falla según el orden.
- Causa raíz: `QuitarCambioDelDiaAsync` borra la fila de `tipo_cambio` del día **asumiendo que ningún documento la referencia**, y eso era cierto mientras solo emitiera F3-02. En cuanto una factura emitida o un cobro de DOP congela ese cambio (F3-02 con DOP, F3-05 desde el trabajo de facturas periódicas, F3-06 con DOP), la `fk_*_tipo_cambio_tipo_cambio_id` impide el borrado. El diseño del producto es correcto —el cambio congelado no se puede borrar, por eso `tipo_cambio` no concede `DELETE` a `rent_app`—, lo que está mal es la preparación de la prueba, que depende del estado que dejan otras.
- **Solución definitiva (orquestador):** el helper ya no borra nada. `ApartarCambioDelDiaAsync` mueve la fila del día a una fecha reservada cinco años atrás y `DevolverCambioApartadoAsync` la devuelve, en un `finally`, también si la prueba falla. Mover la fila no rompe ninguna clave foránea porque los documentos la referencian por identificador (`tipo_cambio_id`), no por fecha, y al devolverla la base compartida queda exactamente como estaba. La prueba sigue viendo «hoy sin cambio» sin tocar nada que otras clases dependan.
- Mitigación que quedó además en F3-06: las pruebas de DOP de cobros se deshacen a sí mismas con `AyudaCobros.SinRastroDeCambio` (`await using` al final): por la conexión de superusuario ponen a `NULL` el `tipo_cambio_id` de los documentos que apuntan a esa fila y borran la fila. Sigue siendo la obligación de cualquier prueba que cree documentos en DOP, porque si una de ellas se queda sin limpiar, la fila que F3-02 aparta ya no es la que hay al volver.
- **Variante descartada:** adelantar el reloj de pruebas para que cada clase tenga un día propio. Se probó (`ApiTestFactory` con reloj propio) y **no sirve**: el token de acceso se emite con el reloj de la aplicación y JwtBearer lo valida contra el reloj real del sistema, así que con el reloj adelantado toda llamada autenticada sale con 401. Se revirtió por completo; no queda rastro de esa fábrica en el repositorio.
- Riesgo que queda: la fecha reservada (cinco años antes del día de la prueba) tendría que chocar con otra fila de DOP de esa misma fecha, que es algo que ninguna prueba carga. Si alguna vez se carga, la operación dará error de unicidad en lugar de pisar el cambio de otro día.
- Test que lo cubre: `backend/AtlasRent.Tests/Facturas/FacturaEmisionTests.cs` (`SinTipoDeCambioDelDiaLaEmisionFallaYNoGastaElNumero`), ejecutada junto a `Cobros/CobroMonedaTests.cs`, que es la combinación que lo Hacía fallar.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno; es infraestructura de pruebas.

### BUG-005 · La serie histórica 9 quedó sembrada activa, aunque no debe emitir
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: configuración / F1-08 → F3-02
- Detectado por: revisión de F3-02 al leer la siembra de series de §3.7
- Síntoma: la serie 9 (histórica, de importación de Factusol) salía de la migración de F1-08 con `activa = true`, así que una factura podía emitir contra ella y gastar un número de un contador que no significa nada para una importación que conserva el número que ya tenía.
- Cómo reproducir: tras migrar, `SELECT numero, activa FROM serie`; la 9 aparece activa.
- Causa raíz: se sembró cuando todavía no había facturas que emitir, y el valor era provisional.
- Solución: la migración de F3-02 la desactiva con un `UPDATE` y su `Down()` la devuelve al estado anterior. **No se ha tocado** `suma_a_facturado`/`suma_a_saldos` de la 9: F1-08 la sembró en `false` con un `TODO(A#)` que sigue sin responder y es una pregunta de negocio para el responsable, no una decisión de esta tarea.
- Test que lo cubre: ninguno (es una fila de datos sembrada; el comportamiento de una serie inactiva lo cubre `SeriesContadorTests` de F1-08).
- Commit/PR: pendiente del orquestador.
- Datos afectados: en bases ya migradas, la 9 pasa a inactiva al aplicar la migración de F3-02.

### BUG-001 · El paquete de despliegue salía sin la interfaz del frontend
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: despliegue / F1-13
- Detectado por: revisión adversarial del bloque F1 (subagente verificador)
- Síntoma: `Program.cs` servía el frontend compilado desde `wwwroot/`, pero `npm run build` escribía en `frontend/dist`. No había nada que pusiera una cosa donde la otra la lee, así que el ZIP de despliegue de DOC_TECNICA §9.3 («backend self-contained + frontend compilado») se publicaba sin interfaz, y el servicio instalado en `C:\AtlasRent` respondía 404 en `/`.
- Cómo reproducir: `npm run build` y después `dotnet publish backend/AtlasRent.Api -c Release -o <destino>`; en `<destino>/wwwroot/` no hay nada.
- Causa raíz: nadie connectó el paso «compilar el frontend» con el paso «publicar la API». El comentario de `Program.cs` daba por hecho un empaquetado que todavía no existía (`scripts/install.ps1` sigue sin escribirse).
- Solución: target `AtlasRentPublicarFrontend` en `backend/AtlasRent.Api/AtlasRent.Api.csproj`, que copia `frontend/dist` a `wwwroot/` del paquete en `dotnet publish`. Va en `Publish` y no en `Build` para que compilar en desarrollo y ejecutar los tests de integración no dependan de que el frontend esté construido. Si se publica sin haberlo construido, avisa por consola en vez de fallar en silencio, y el paquete sale sin interfaz. Se añade además un paso en la CI que publica y comprueba que `wwwroot/index.html` existe, para que este bug no pueda volver a colarse.
- Test que lo cubre: no hay test unitario (el fallo es de empaquetado, no de ejecución). Lo cubren la comprobación de la CI y la verificación manual con y sin `frontend/dist`.
- Commit/PR: `7dc9eea`
- Datos afectados: ninguno. No hay despliegues reales; el repositorio no se ha publicado nunca.

### BUG-002 · Renombrar una copia de respaldo con la fecha de hoy impedía la copia del día
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: respaldos / F1-12
- Detectado por: revisión adversarial del bloque F1 (subagente verificador)
- Síntoma: la fecha de una copia solo vivía en el nombre del archivo. Quien tuviera escritura en `Backup__CarpetaLocal` —o un script de restauración descuidado— podía renombrar una copia vieja con la fecha de hoy. Esa copia **verificaba** (es auténtica y descifrable), así que el servicio se la tomaba por la copia vigente: no generaba la del día, el log decía que todo estaba bien y la rotación acababa borrando las copias buenas creyendo que eran viejas. Es el escenario clásico de subversión de copias.
- Cómo reproducir: generar la copia de un día; renombrarla con la fecha de otro día; ejecutar el respaldo en ese segundo día. Devuelve `YaExistia = true` sin generar nada.
- Causa raíz: la fecha era un dato del sistema de archivos, no del contenido. Lo que la etiqueta GCM protege es el contenido descifrado, y la fecha no estaba dentro.
- Solución: el contenedor pasa a `ARBK2` y lleva la fecha **dentro** (`ContenedorCopia.Empaquetar(fecha, entradas)` / `Desempaquetar`). `VerificarAsync` compara la fecha del nombre con la del contenido y rechaza el archivo si no coinciden; como el contenido va cifrado y firmado, alterar la fecha del nombre ya no engaña a nadie.
- Test que lo cubre: `backend/AtlasRent.Tests/Backup/RotacionCopiaTests.cs` → `UnaCopiaRenombradaConLaFechaDeHoyNoCuentaComoLaCopiaDeHoy`; `backend/AtlasRent.Tests/Backup/CifradoCopiaTests.cs` → `LaFechaDelContenedorEsLaDelContenidoYNoLaDelNombre`. Comprobado que el primero **no es vacuo**: al desactivar la validación falla, enseñando `Fecha = 2026-10-16` frente a `FechaDelNombre = 2026-10-17`.
- Commit/PR: `38ebc78`
- Datos afectados: ninguno todavía. **Aviso de operación**: el cambio de marca (`ARBK2`) hace que las copias escritas por un binario anterior dejen de abrirse. Al desplegar un binario nuevo hay que regenerar la copia del día.

### BUG-003 · El respaldo fallido solo dejaba una línea de log
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: Media · Estado: CORREGIDO · Módulo: respaldos / F1-12
- Detectado por: criterio de aceptación de F1-12 («fallo → alerta») revisado contra el código
- Síntoma: un respaldo fallido escribía con Serilog y lanzaba, pero no quedaba registro consultable. Una alerta en un log que nadie mira es la ausencia de alerta.
- Cómo reproducir: forzar un volcado vacío y comprobar que no queda nada registrado más allá del log.
- Causa raíz: nunca se implementó el registro de fallos; el código tenía un `TODO(A#)` en el punto exacto donde había que dispararlo.
- Solución: tabla `alerta` con `clave` única, entidad `Alerta` y `ServicioAlertas`, que hace `INSERT … ON CONFLICT DO UPDATE`: el mismo fallo repetido suma a `repeticiones` en vez de llenar la tabla de filas idénticas (ESPECIFICACION §3.16, «alertas sin duplicar»). `ServicioBackup` registra el fallo del día y el fallo de subida al almacén remoto. El motivo solo se traslada a la alerta si la excepción es del propio servicio, porque una inesperada puede llevar dentro la cadena de conexión.
- Test que lo cubre: `backend/AtlasRent.Tests/Backup/AlertaDeFalloTests.cs` (3 tests contra PostgreSQL real de Testcontainers) y `RotacionCopiaTests.cs` → `UnaCopiaQueFallaDejaAlertaConLaFechaDelDia`, `UnaCopiaQueSaleBienNoDejaNingunaAlerta`.
- Commit/PR: `38ebc78`, `5207e3a`
- Datos afectados: ninguno.
