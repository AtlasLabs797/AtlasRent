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

### BUG-059 · HuecosApiTests.SubempresasTraenPropietariaYFacturadora depende del tamaño de la base compartida
- Fecha detectado: 29/10/2026 · Fecha corregido: 29/10/2026
- Gravedad: **Baja** · Estado: CORREGIDO · Módulo: pruebas / catálogos
- Detectado por: suite completa tras integrar AUD-11 (1179/1180)
- Síntoma: la prueba no encontraba sus subempresas en la respuesta del catálogo.
- Cómo reproducir: ejecutar la suite completa; en la base compartida hay más de 200 subempresas y las de la prueba quedan fuera de la primera página (orden por nombre).
- Causa raíz: la prueba leía solo la primera página del catálogo (`pageSize` por defecto 200).
- Solución: la prueba recorre todas las páginas. El catálogo no cambia: 200 subempresas activas no es un caso real.
- Test que lo cubre: `Catalogos/HuecosApiTests` + `CatalogosApiTests` 23/23.
- Commit/PR: rama `tarea/AUD-01-auditoria`, commit `test(catalogos): recorrer todas las páginas…`.
- Datos afectados: Ninguno: es un fallo de prueba.

### BUG-058 · GET /auditoria da 500 cuando `datos` lleva arrays
- Fecha detectado: 29/10/2026 · Fecha corregido: 29/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: auditoría (lectura, AUD-11)
- Detectado por: suite completa tras integrar AUD-11
- Síntoma: la pantalla de auditoría no carga (500) en cuanto alguna fila de la página tiene un array en `datos`.
- Cómo reproducir: `GET /api/v1/auditoria` con filas de auditoría de facturas (llevan líneas).
- Causa raíz: el enmascarado de campos sensibles reasignaba cada nodo a su propio padre (`lista[i] = Enmascarar(lista[i])`), y System.Text.Json lanza «The node already has a parent». En la clase de test aislada no había arrays.
- Solución: recorrer en sitio y sustituir solo los valores sensibles.
- Test que lo cubre: `Bandeja/BandejaAlertasApiTests.EnmascararRecorreArraysYObjetosAnidadosSinRomper`.
- Commit/PR: rama `tarea/AUD-01-auditoria`, commit `fix(auditoria): enmascarar datos con arrays…`.
- Datos afectados: Ninguno: es una lectura.

### BUG-057 · Desactivar un inmueble no comprueba contratos ni reservas vivos
- Fecha detectado: 28/10/2026 · Fecha corregido: 29/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: inmuebles
- Detectado por: agente (AUD-11, al implementar la baja de unidad)
- Síntoma: `DELETE /api/v1/inmuebles/{id}` desactiva el inmueble aunque tenga unidades con contrato o reserva vivos; la baja de unidad (AUD-11) sí lo impide con 409.
- Cómo reproducir: inmueble con una unidad con contrato Activo → `DELETE /inmuebles/{id}` → 200.
- Causa raíz: `ServicioInmuebles.DesactivarAsync` solo cambia el indicador.
- Solución: `DesactivarAsync` aplica a todas las unidades del inmueble la misma comprobación que la baja de unidad (contrato no Finalizado/Liquidado o reserva Activa/EnConflicto → 409), extraída a `TieneContratosOReservasVivosAsync`.
- Test que lo cubre: `Inmuebles/UnidadGlobalTests.UnInmuebleConUnaReservaVivaNoSeDesactiva` (namespace Inmuebles 30/30).
- Commit/PR: rama `tarea/AUD-01-auditoria`, commit `fix(inmuebles): no desactivar un inmueble con contratos o reservas vivos`.
- Datos afectados: sin datos reales todavía (no hay producción); al migrar desde Factusol, comprobar que ningún inmueble inactivo tenga contratos vivos.

### BUG-056 · El recordatorio manual recomponía siempre el PDF al enviar
- Fecha detectado: 29/10/2026 · Fecha corregido: 29/10/2026
- Gravedad: **Baja** · Estado: CORREGIDO · Módulo: recordatorios
- Detectado por: suite completa (`RecordatorioContenidoTests.ElEnvioMandaLaCopiaCompuestaAlPreparar`)
- Síntoma: el adjunto enviado no era la copia preparada y revisada, sino otra generación del mismo PDF; el test fallaba de forma intermitente.
- Cómo reproducir: preparar un recordatorio manual y confirmarlo en otro segundo sin cambiar el saldo.
- Causa raíz: la corrección de BUG-040 recompone asunto, cuerpo y PDF en cada envío. El PDF lleva fecha de creación al segundo: si preparar y enviar caían en el mismo segundo los bytes coincidían y el test pasaba por casualidad.
- Solución: recomponer solo si el asunto o el cuerpo calculados con el saldo de ahora difieren de los preparados; si no, sale la copia preparada. Cumple BUG-040 y §3.14.
- Test que lo cubre: `Recordatorios/RecordatorioContenidoTests` (namespace Recordatorios 18/18).
- Commit/PR: rama `tarea/AUD-01-auditoria`, commit `fix(recordatorios): recomponer el aviso al enviar solo si cambió el saldo`.
- Datos afectados: Ninguno.

### BUG-055 · EnvioMasivoResumenTests.PrepararNoEnviaNada depende del orden de ejecución
- Fecha detectado: 28/10/2026 · Fecha corregido: 28/10/2026
- Gravedad: **Baja** · Estado: CORREGIDO · Módulo: pruebas / mensajería
- Detectado por: suite completa (tras integrar AUD-06/07)
- Síntoma: `EnvioMasivoResumenTests.PrepararNoEnviaNada` fallaba según el orden de ejecución de la suite.
- Cómo reproducir: ejecutar la suite completa; la prueba falla si antes corre otra que guarda plantillas en la base compartida.
- Causa raíz: la vista previa del envío masivo usa la plantilla guardada en la base compartida, y otra prueba la deja modificada. La prueba dependía de un estado que no creaba ella.
- Solución: la prueba prepara su propia plantilla y no depende de la que dejó otra.
- Test que lo cubre: `Mensajeria/EnvioMasivoResumenTests.PrepararNoEnviaNada` (Mensajeria 55/55 tras el arreglo).
- Commit/PR: commit 9e0ce5f.
- Datos afectados: Ninguno: es un fallo de prueba.

### BUG-054 · Regresiones de integración de las correcciones de AUD-03 a AUD-06
- Fecha detectado: 27/10/2026 · Fecha corregido: 28/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: API / facturación periódica / seguridad
- Detectado por: suite completa al integrar las ramas de AUD
- Síntoma: (1) 21 fallos en la suite: toda entrada de dinero de la interfaz devolvía 400. (2) El guard de cifras fiscales saltaba con `/ 100m` en `RentaPorTramos.cs` y con un nombre «Retencion…» en `OpcionesLimites`. (3) La facturación periódica reutilizaba el borrador de «rehacer» en otra moneda. (4) `/health/live` daba 500 sin base de datos.
- Cómo reproducir: integrar las ramas aud/sec018 (SEC-030), aud/bug022, aud/bug020 y el bloqueo de IP, y ejecutar la suite completa.
- Causa raíz: (1) el `ImporteJsonConverter` global de SEC-030 solo leía números JSON y el frontend envía el dinero como texto decimal. (2) Literales y nombres que el guard confunde con cifras fiscales. (3) BUG-022 reutiliza cualquier borrador huérfano del contrato y BUG-033 crea el borrador de rehacer en otra moneda; ninguna rama conocía la otra. (4) `IpBanMiddleware` solo capturaba `DbException` y EF envuelve el fallo de conexión en `InvalidOperationException`.
- Solución: (1) el conversor lee también texto decimal con cultura invariante (712b140). (2) Divisor de incrementos reescrito y retención de intentos de login renombrada a `purgar_intentos_login` / `Seguridad__Limites__DiasConservarIntentosLogin` (b4de4bb, 712b140). (3) La periódica solo reutiliza borradores de la misma moneda (b4de4bb). (4) El middleware captura también la excepción envuelta (912f111).
- Test que lo cubre: la suite completa (1083/1083); `Periodicas/PeriodicaRehacerMonedaTests` y `Periodicas/PeriodicaAtomicaTests`; `Arranque/ArranqueApiTests` y `Seguridad/IpBloqueadaTests` para `/health/live`. El arreglo (4) no añadió un test propio.
- Commit/PR: 712b140, b4de4bb y 912f111.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-053 · Suite de Backup en rojo con core.autocrlf=true
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Baja** · Estado: CORREGIDO · Módulo: backup / pruebas
- Detectado por: agente (AUD-03)
- Síntoma: en un checkout con `core.autocrlf=true` (los worktrees de esta máquina) fallan 8 tests de Backup: 7 de `RotacionCopiaTests` y `CifradoCopiaTests.ElVolcadoConUnaFilaSeConsideraConDatos`. En el checkout principal, con ficheros en LF, pasan.
- Cómo reproducir: crear un worktree o clon con `core.autocrlf=true` en Windows y ejecutar el namespace Backup de `backend\AtlasRent.Tests\bin\Release\net10.0\AtlasRent.Tests.exe`.
- Causa raíz: los literales de cadena multilínea llegan con `\r\n`, y `ServicioBackup.TieneFilas` y `EsCabeceraCopy` no toleran el `\r`. Por eso cualquier volcado de prueba parece sin filas. El mismo defecto haría que un volcado real con CRLF se considerara vacío.
- Solución: `ServicioBackup.TieneFilas` y `EsCabeceraCopy` toleran `\r\n`; `.gitattributes` fija `*.cs text eol=lf`.
- Test que lo cubre: Los 8 tests de Backup citados (verdes con CRLF).
- Commit/PR: rama aud/bug044, commit 5e4ef9d; merge b336c20.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-052 · Varios menores
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Baja** · Estado: **PARCIAL** (5 puntos de producto con TODO(A#)) · Módulo: varios
- Detectado por: auditoría (agentes)
- Síntoma: Lista: `CalendarioController.cs:33-37` desde/hasta sin `[Required]` (año 0001 con 200); los documentos de inmueble guardan el archivo antes de comprobar el inmueble y dejan huérfanos al abortar; `FijarVersionAsync` MAX+1 da 409 en concurrencia; `ServicioConfiguracion.CalcularAsync` (~786-815) es código muerto con ITBIS sobre subtotal (diverge de D27); `PeriodoFacturacion.Vencimiento` puede nacer vencida al facturar con retraso; `ServicioFacturas` `ConstruirLinea` (~1815) y `ServicioContratos` (`CambiarEstado:1265`, `FinalizarAsync:694`, `LiquidarAsync:838`) usan el reloj del sistema en vez de `TimeProvider`; `TipoCambioAplicado` sin control de sensatez (`ServicioCobros.cs:772`, decisión de producto); `modificadaTrasEnvio` trata Pendiente como salido (`ServicioCuentas.cs:426`); sustitución en cadena de variables de plantilla (`VariablesMensaje.cs:76`); propuestas duplicadas sin idempotencia; justificación de propuesta sin reidentificar; `config/variables-entorno.example` sin `Jobs__ServidorActivo`; `rent_app` con UPDATE sobre tablas que deben ser inmutables (`contrato_plantilla_congelada`, `contrato_inventario_item`, `contrato_version`); contrato Aprobado sin salida; `AplicarCambios` permite cambiar la moneda de un contrato activo; TODOs de F3 obsoletos en `ServicioAvisosOperacion.cs:230-249`; `FiltroAccionGlobal.cs:31` con `FirstOrDefault` elegiría el `[Accion]` de clase sobre el de método.
- Cómo reproducir: Ver cada punto en el archivo citado.
- Causa raíz: Cada punto tiene su causa en el archivo:línea indicado en el síntoma.
- Solución: Corregido: `[Required]` en `CalendarioController`; documento de inmueble comprobado antes de guardar el archivo y versión con cerrojo; eliminado `ServicioConfiguracion.CalcularAsync` (código muerto divergente); reloj inyectado en `ServicioFacturas.ConstruirLinea` y `ServicioContratos`; variables de plantilla en una sola pasada; `Jobs__ServidorActivo` en el example de variables; migración `20261027130734_RevocarUpdateCopiasCongeladas`; moneda de un contrato activo no editable (409); `modificadasTrasEnvio` no cuenta Pendiente; `FiltroAccionGlobal` prioriza el método. Quedan sin cerrar (decisión de producto, con `TODO(A#)`): `TipoCambioAplicado` sin control de sensatez, vencimiento «siguiente ciclo» que puede nacer vencido, contrato Aprobado sin salida. Sin tocar: propuestas duplicadas sin idempotencia y justificación de propuesta sin reidentificar.
- Test que lo cubre: Pruebas repartidas por módulo (calendario, inmuebles, mensajería, contratos, seguridad de copias congeladas); sin test para los puntos que quedan abiertos.
- Commit/PR: commits 4d44b26, 4c56b29, e05251c, 4ba2129, fcb0e39, 3c384c6 y 9f005a2; merges b336c20 y 9c54945.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-051 · Tests que no prueban lo prometido
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas
- Detectado por: auditoría (agentes)
- Síntoma: Hay verdes que no cubren su criterio: (a) `CobroSaldosPropertyTests` sin anulaciones, otras monedas, retenciones ni concurrencia, con invariantes calculadas con `CalculoCobro` del propio código; (b) `FacturaPdfTests.cs:230` afirma sobre el modelo, no sobre el PDF, y recibo/estado/cotización/contrato solo tienen lint; (c) `MatrizPermisosTests` prueba lo sembrado en BD, no endpoints (el criterio de F1-07 pide API) y no hay test por reflexión de los controladores reales; (d) `RenovacionContratosTests` calcula lo esperado con la misma fórmula; (e) `ReservaAnticipoYAlertaTests` (F2-03) no se actualizó tras F3; (f) `CifradoCopiaTests.cs:193` tiene el nombre o el comentario al revés.
- Cómo reproducir: Leer cada test citado frente al criterio de aceptación de su tarea.
- Causa raíz: Tests escritos con la misma fórmula que el código o sin ejercitar la capa que dicen probar.
- Solución: (a) Property test ampliado: USD/DOP, anulaciones de cobros y facturas, fase concurrente, invariantes recalculadas desde las filas sin `CalculoCobro`. (b) Extractor de texto de PDF (`Infra/TextoPdf.cs`) y pruebas sobre el texto real de recibo, estado de cuenta, cotización y contrato. (c) Test por reflexión de los controladores y matriz de endpoints generada desde `MatrizPermisos`; `FiltroAccionGlobal` prioriza la acción del método. (d) `RenovacionContratosTests` con fechas literales. (e) Prueba de que el anticipo no aparece en saldos, cuentas ni informes. (f) `CifradoCopiaTests` renombrado.
- Test que lo cubre: `Cobros/CobroSaldosPropertyTests`, `Facturas/PdfTextoDeUsuarioTests`, `Permisos/ControladoresDeclaranAccionTests`, `Permisos/MatrizEndpointsApiTests`, `RenovacionContratosTests`, `ReservaAnticipoYAlertaTests.ElAnticipoNoApareceEnSaldosCuentasNiInformes`.
- Commit/PR: commits 95ae9ae, d61d576, 1d60ec1, 927cd78 y 46dd20d; merges 6d5dfff y 9c54945.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-050 · Liquidar no cierra fianza ni saldos
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: contratos
- Detectado por: auditoría (agentes)
- Síntoma: §3.5 dice que la liquidación cierra inventario, fianza y saldos; hoy no lo hace.
- Cómo reproducir: Liquidar un contrato con fianza y saldos abiertos.
- Causa raíz: `ServicioContratos.cs:803-829`, con un `TODO(A#)` de F3-01 obsoleto.
- Solución: No se liquida con la fianza sin resolver ni con facturas pendientes (409 que dice qué falta). Sin movimientos automáticos.
- Test que lo cubre: `Liquidacion/LiquidarCierraFianzaYSaldosTests` (2).
- Commit/PR: rama aud/bug023, commit fa7c9f0; merge edddf51.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-049 · Disponibilidad ignora el estado manual de la unidad
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: reservas / contratos
- Detectado por: auditoría (agentes)
- Síntoma: Se reservan o activan unidades que el calendario marca como no disponibles.
- Cómo reproducir: Poner una unidad en estado manual no disponible, o inactiva, o sin la modalidad, y reservarla/activar un contrato.
- Causa raíz: `ServicioReservas.cs:481-499` y `ServicioContratos.ActivarAsync` no miran `EstadoManual`, `Unidad.Activo` ni `Unidad.Modalidades`.
- Solución: Reservar y activar un contrato exigen unidad activa, `EstadoManual` Disponible y modalidad admitida.
- Test que lo cubre: Pruebas en `Reservas/` y `Contratos/`.
- Commit/PR: rama aud/bug026, commit c3f9ade; merge 3391e71.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-048 · Reserva con factura se puede seguir editando/cancelando y dos POST concurrentes crean dos facturas
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: reservas / facturas
- Detectado por: auditoría (agentes)
- Síntoma: Se edita o cancela una reserva ya facturada; dos peticiones concurrentes facturan dos veces.
- Cómo reproducir: Facturar una reserva y editarla; lanzar dos `FacturarReserva` a la vez.
- Causa raíz: `ServicioReservas.cs` no mira `Factura`; `FacturarReservaAsync` (`ServicioFacturacionPeriodica.cs:~228-240`) comprueba y luego crea por texto de concepto, sin índice único.
- Solución: Con factura vigente no se cambian días, importe ni moneda y no se cancela (409). Facturar la reserva con violación de `ix_factura_periodo_unico` da 409.
- Test que lo cubre: `Periodicas/FacturaReservaBloqueoTests` (3).
- Commit/PR: rama aud/bug026, commit c3f9ade; merge 3391e71.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-047 · La CI ejecuta `dotnet test`, que en este repo no ejecuta ninguna prueba
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: CI
- Detectado por: auditoría (agentes)
- Síntoma: «CI verde» no prueba nada (AGENTS §8). Debe ejecutar `AtlasRent.Tests.exe` (o `dotnet run --project` del proyecto de tests) en Linux.
- Cómo reproducir: Mirar `.github/workflows/ci.yml:49` y el número de pruebas ejecutadas por la CI.
- Causa raíz: `.github/workflows/ci.yml:49` usa `dotnet test` con xUnit v3 de runner en proceso.
- Solución: `ci.yml` ejecuta `dotnet backend/AtlasRent.Tests/bin/Release/net10.0/AtlasRent.Tests.dll` y falla con 0 pruebas o con algún fallo. Aviso de AGENTS §8 actualizado con el comando de Linux.
- Test que lo cubre: El propio paso de CI (falla con 0 pruebas); no hay test automático del workflow.
- Commit/PR: rama aud/bug047, commits 43082ef y 16cc099; merge 9c54945.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-046 · Se puede cambiar Moneda/Prefijo/Tipo de una serie con números emitidos
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: configuración / series
- Detectado por: auditoría (agentes)
- Síntoma: (Plausible.) Cambia el significado de números ya emitidos.
- Cómo reproducir: Editar una serie con facturas emitidas.
- Causa raíz: `ServicioConfiguracion.ActualizarSerieAsync` (~131-170) no comprueba si hay números emitidos.
- Solución: Moneda, prefijo y tipo de una serie con números emitidos no se pueden cambiar (409).
- Test que lo cubre: `Comprobantes/NumeradorConfiguracionTests`.
- Commit/PR: commit 83220d8; merge b336c20.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-045 · Integridad y backup: cadena rota sin alerta, subida no reintentada y backup en memoria
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: backup / auditoría
- Detectado por: auditoría (agentes)
- Síntoma: Una cadena de auditoría rota solo deja un log; si la copia del día ya existe no se reintenta la subida; el backup entero va en memoria (límite 2 GB); la rotación cuenta copias inválidas entre las 4.
- Cómo reproducir: Romper un hash de `auditoria`; o fallar la subida y relanzar el backup del día.
- Causa raíz: `Jobs/TrabajoIntegridadAuditoria.cs:54` (solo `Log.Error`); `ServicioBackup.cs:235` vuelve antes de `SubirAsync`; uso de `byte[]`/`MemoryStream`.
- Solución: Alerta `integridad-auditoria` (sin duplicar) si la cadena está rota. Reintento de subida con marca `.arbk.subida`. La rotación conserva N copias válidas. Límite `OpcionesBackup.LimiteBytesContenido` (1 GiB) con alerta; no se pasó a streams porque cambiaría el formato cifrado.
- Test que lo cubre: `Jobs/TrabajoIntegridadAuditoriaTests`, `Backup/RotacionCopiaTests` (3 nuevos).
- Commit/PR: rama aud/bug044, commits 6521843 y 1bf888d; merge b336c20.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-044 · Carreras en sesión: 500 en lugar de 401 y comprobación de inactividad inalcanzable
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: sesiones
- Detectado por: auditoría (agentes)
- Síntoma: (Plausible, alta.) Varias peticiones simultáneas dan 500 por `DbUpdateConcurrencyException`; el perdedor de un refresco concurrente recibe 500 en vez de 401.
- Cómo reproducir: Lanzar peticiones concurrentes con la misma sesión o dos refrescos a la vez.
- Causa raíz: `ServicioSesiones.cs:99,234`: `GetOrCreateAsync` no serializa y se guarda `UltimaActividad` con `xmin`. La comprobación de inactividad en `ValidarAsync:127` es inalcanzable porque `LeerAsync` ya pone `UltimaActividad=ahora`.
- Solución: Actividad con `ExecuteUpdate` condicional; la inactividad se comprueba antes de anotarla; un refresco concurrente devuelve 200 o 401, nunca 500.
- Test que lo cubre: `Auth/SesionesConcurrenciaTests` (2), `Auth/InactividadTests`.
- Commit/PR: rama aud/bug044, commit b465615; merge b336c20.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-043 · Renovación encadena AddMonths y deriva a fin de mes
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: contratos / renovaciones
- Detectado por: auditoría (agentes)
- Síntoma: 31/01 pasa a 28/02 y luego a 28/03.
- Cómo reproducir: Renovar dos veces un contrato que empieza el 31 de enero.
- Causa raíz: `ServicioRenovaciones.cs:204-205`. Los tests usan la misma fórmula.
- Solución: El fin de bloque se calcula como `inicio.AddMonths(duración inicial + (n-1)·renovación)`, sin encadenar.
- Test que lo cubre: `LaRenovacionMensualNoDerivaAFinDeMes` (fallaba).
- Commit/PR: rama aud/bug028, commit 0231914; merge d1a99f2.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-042 · Bandeja: POST /contratos/solicitudes/{id}/aprobacion recibe un ContratoId
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: contratos / bandeja
- Detectado por: auditoría (agentes)
- Síntoma: Se aprueba la solicitud viva, no la mostrada.
- Cómo reproducir: Aprobar desde la bandeja una solicitud cuando el contrato tiene otra más reciente.
- Causa raíz: `ContratosController.cs:293-310` recibe en realidad `ContratoId`; `ServicioContratos.cs:352`.
- Solución: La ruta de la bandeja recibe el `SolicitudId`. Aprobar o rechazar una solicitud sustituida da 409.
- Test que lo cubre: `Bandeja/BandejaAprobacionesTests` (2 nuevos, 5 ajustados).
- Commit/PR: rama aud/bug023, commit fa7c9f0; merge edddf51.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-041 · Cambiar una reserva de mensual a corta (o al revés) deja un candado huérfano
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: reservas
- Detectado por: auditoría (agentes)
- Síntoma: La unidad queda bloqueada por un candado que ya no corresponde a ninguna reserva.
- Cómo reproducir: Editar una reserva cambiando su tipo.
- Causa raíz: `ServicioReservas.cs:531` y `Origen()` en `:633`.
- Solución: Al cambiar de tipo se suelta el candado del origen anterior.
- Test que lo cubre: `DeMensualACortaSueltaElCandadoDelRango`, `DeCortaAMensualSueltaLosDiasDelBloqueo`.
- Commit/PR: rama aud/bug026, commit c3f9ade; merge 3391e71.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-040 · Recordatorio manual sale con importe y PDF desfasados
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: recordatorios
- Detectado por: auditoría (agentes)
- Síntoma: El importe citado puede no ser el actual y solo cubre las facturas «en plazo».
- Cómo reproducir: Preparar un recordatorio, cambiar el estado de las facturas y enviarlo.
- Causa raíz: `PrepararAsync` congela los datos y `EnviarAsync` solo comprueba si queda algo pendiente.
- Solución: Al confirmar se regeneran asunto, cuerpo y PDF con el saldo actual. El importe citado es el saldo vencido total del cliente y la unidad.
- Test que lo cubre: `Recordatorios/RecordatorioActualizadoTests` (2, fallaban).
- Commit/PR: rama aud/bug037, commit cdfd917; merge d2c0dfd.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-039 · Aprobar una propuesta cuyo envío falló u omitió la deja Aprobada
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: IA / propuestas
- Detectado por: auditoría (agentes)
- Síntoma: La propuesta figura como aprobada aunque el recordatorio no se enviara.
- Cómo reproducir: Aprobar una propuesta de recordatorio cuyo envío falla o se omite.
- Causa raíz: `ServicioPropuestasAgente.cs:235` descarta el resultado de `recordatorios.EnviarAsync`; `:237` marca Aprobada.
- Solución: Si el aviso no queda Enviado, la propuesta se resuelve `Caducada` con la causa en lugar de Aprobada.
- Test que lo cubre: `IA/PropuestaAprobacionTests` (2, fallaban).
- Commit/PR: rama aud/bug037, commit cdfd917; merge d2c0dfd.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-038 · Correo duplicado por reintento automático de Hangfire
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: mensajería
- Detectado por: auditoría (agentes)
- Síntoma: El mismo correo puede salir varias veces; `PermisoDenegadoException` reintenta 10 veces.
- Cómo reproducir: Hacer fallar el guardado tras el envío SMTP, o lanzar dos jobs simultáneos para la misma fila.
- Causa raíz: `ServicioEnvios.EnviarPendienteAsync` envía por SMTP y después guarda; si el guardado falla, el reintento (10 por defecto) reenvía.
- Solución: Nuevo estado `Enviando` (=6). El envío reclama la fila con UPDATE condicional antes de hablar con SMTP; `[AutomaticRetry(3)]`; `PermisoDenegadoException` deja la fila en Fallido sin reintentos.
- Test que lo cubre: `Mensajeria/EnvioRobustezTests` (3).
- Commit/PR: rama aud/bug037, commit cdfd917; merge d2c0dfd.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-037 · Envío masivo sin outbox: filas Pendiente huérfanas
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: mensajería
- Detectado por: auditoría (agentes)
- Síntoma: Si el proceso cae entre el Commit y el encolado, quedan filas Pendiente irrecuperables.
- Cómo reproducir: Matar el proceso tras el Commit de un envío masivo.
- Causa raíz: `ServicioEnviosMasivos.cs:203` y `:325` encolan en Hangfire después del Commit; la idempotencia devuelve la respuesta guardada y Reintentar solo coge Fallido.
- Solución: Job `barrido-envios` cada 10 minutos: reencola los Pendiente de lotes con más de 30 minutos y alerta de los Enviando con más de 60.
- Test que lo cubre: `Mensajeria/EnvioRobustezTests.ElBarridoReencolaLoPendienteSinTrabajoYSaleUnaSolaVez`.
- Commit/PR: rama aud/bug037, commit cdfd917; merge d2c0dfd.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-036 · comprobante_retencion queda obsoleto al modificar la factura
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: retenciones / facturas
- Detectado por: auditoría (agentes)
- Síntoma: El comprobante de retención conserva los importes anteriores a la modificación.
- Cómo reproducir: Emitir una factura con retención y modificarla.
- Causa raíz: `ServicioRetenciones.ReservarEnEmisionAsync` copia los importes y `ServicioFacturas.ModificarAsync` no los actualiza.
- Solución: `ServicioRetenciones.ActualizarEnModificacionAsync`: el comprobante Pendiente se actualiza con la factura; con uno Recibido la modificación se bloquea (409).
- Test que lo cubre: `Retenciones/RetencionModificacionTests` (2).
- Commit/PR: rama aud/bug020, commit cf7736d; merge 8ce1c8f.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-035 · Ajuste por diferencia de cambio sin bloqueo y sin exigir cobro previo en otra moneda
- Fecha detectado: 26/10/2026 · Fecha corregido: 26/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: cobros
- Detectado por: auditoría (agentes)
- Síntoma: Dos ajustes concurrentes se duplican y cualquier factura con pendiente ≤ tolerancia se cierra sin pago.
- Cómo reproducir: Dos ajustes simultáneos sobre la misma factura; o un ajuste sobre una factura sin cobros.
- Causa raíz: `ServicioCobros.cs:~500-590` y `:547`.
- Solución: El ajuste por diferencia de cambio toma el cerrojo de cobros y solo se admite con un cobro vigente en otra moneda.
- Test que lo cubre: `DosAjustesSimultaneosNoSeDuplican`, `ElAjusteExigeUnCobroVigenteEnOtraMoneda`.
- Commit/PR: rama aud/bug019, commit 06a9632; merge 6d5dfff.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-034 · Fianza: anular el cobro de una fianza devuelta deja devuelto > cobrado y la liquidación se clasifica mal
- Fecha detectado: 26/10/2026 · Fecha corregido: 26/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: fianzas
- Detectado por: auditoría (agentes)
- Síntoma: Devuelto mayor que cobrado sin aviso; una devolución con total == saldo e `ImporteDescontado > 0` queda «Devuelta» en vez de «Retenida parcial».
- Cómo reproducir: Cobrar una fianza, devolverla y anular su cobro.
- Causa raíz: `CalculoFianza.Saldo` hace clamp; `ServicioCobros.AnularAsync` no mira las asignaciones a fianza; `ServicioFianzas.cs` (rama completa) no distingue el descuento.
- Solución: No se anula un cobro de fianza si devuelto + descontado superaría lo cobrado (409). `CalculoFianza.Saldo` sin clamp. Devolver todo con descuento deja la fianza en `RetenidaParcial`.
- Test que lo cubre: `FianzaSaldoPropertyTests` (3), `FianzaDevolucionTests`.
- Commit/PR: rama aud/bug030, commit f042273; merge a2f3d6c.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-033 · «Rehacer en otra moneda» no hereda periodo_inicio
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: facturas / facturación periódica
- Detectado por: auditoría (agentes)
- Síntoma: La periódica vuelve a facturar el mismo mes.
- Cómo reproducir: Rehacer en otra moneda una factura periódica y volver a lanzar la periódica.
- Causa raíz: `ServicioFacturas.cs:1225` no copia `periodo_inicio`.
- Solución: Rehacer hereda `PeriodoInicio` y `PeriodoFin`. Regresión con BUG-022 (reutilizaba el borrador de otra moneda) corregida en b4de4bb.
- Test que lo cubre: `Periodicas/PeriodicaRehacerMonedaTests`.
- Commit/PR: rama aud/bug020, commits cf7736d y b4de4bb; merge 8ce1c8f.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-032 · Anular un cobro cuyo crédito ya se gastó lo oculta con clamp a 0
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: cobros / facturas
- Detectado por: auditoría (agentes)
- Síntoma: Se anula el cobro y el crédito gastado desaparece sin rastro. Debe bloquearse la anulación (o exigir anular antes el cobro que gastó el crédito).
- Cómo reproducir: Cobro con exceso que genera crédito; gastar el crédito en otro cobro; anular el primero.
- Causa raíz: `ServicioCobros.cs:455` y `ServicioFacturas.cs:1402` usan `Math.Max(0, …)`.
- Solución: Anular un cobro (o la factura que lo deja sin asignaciones) cuyo crédito ya se gastó da 409 indicando qué cobro anular antes. Método común `ServicioCobros.ExigirCreditoSinGastarAsync`; sin `Math.Max(0,…)`.
- Test que lo cubre: `NoSeAnulaUnCobroCuyoCreditoYaFinancioOtroCobro`, `CicloFactura/FacturaAnulacionCreditoGastadoTests`.
- Commit/PR: rama aud/bug019, commits 06a9632 y 6cdc13d; merge 6d5dfff.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-031 · Cobro con FacturasSeleccionadas en otra moneda mezcla importes
- Fecha detectado: 26/10/2026 · Fecha corregido: 26/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: cobros
- Detectado por: auditoría (agentes)
- Síntoma: Excedente negativo y 409 en Registrar.
- Cómo reproducir: Factura seleccionada USD 1000 y cobro DOP 30000 a 60: excedente −30000.
- Causa raíz: `ServicioCobros.cs:1038`: `Math.Min(pendiente, restante)` compara la moneda de la factura con la moneda recibida.
- Solución: Recorte en moneda recibida igual que la FIFO. Se corrigió además un 409 «entity already tracked» que daba todo cobro con `FacturasSeleccionadas`.
- Test que lo cubre: `UnaFacturaSeleccionadaEnDolaresSeCobraParcialmenteConPesos` y `...CobradaDeMasConPesosDejaExcedenteEnPesos`.
- Commit/PR: rama aud/bug019, commit 06a9632; merge 6d5dfff.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-030 · Total de la cotización distinto de la suma de líneas redondeadas
- Fecha detectado: 26/10/2026 · Fecha corregido: 26/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: cotizaciones
- Detectado por: auditoría (agentes)
- Síntoma: El total de la cotización no coincide con la suma de sus líneas.
- Cómo reproducir: 2 × (1,5 × 0,07): las líneas suman 0,22 y el total sale 0,21.
- Causa raíz: `ServicioCotizaciones.cs:406` suma sin redondear por línea; `:421-431` redondea cada línea.
- Solución: El total de la cotización es la suma de líneas ya redondeadas.
- Test que lo cubre: `CotizacionValidacionTests.ElTotalEsLaSumaDeLasLineasYaRedondeadas` (fallaba).
- Commit/PR: rama aud/bug030, commit f042273; merge a2f3d6c.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-029 · Prorrateo pierde un céntimo en empates exactos
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: dominio / prorrateo
- Detectado por: auditoría (agentes)
- Síntoma: El prorrateo da un céntimo menos cuando el resultado cae justo en el medio.
- Cómo reproducir: 12345.67 / 30 × 15 da 6172.83; lo correcto es 6172.84.
- Causa raíz: `Dominio/Prorrateo.cs:22`: `renta/dias*facturables` divide antes de multiplicar. Hay que multiplicar primero.
- Solución: Prorrateo como `renta * días / díasMes` (multiplicar antes de dividir).
- Test que lo cubre: `Dominio/FacturacionPorTramosTests.ElProrrateoMultiplicaAntesDeDividir` (12345.67 da 6172.84).
- Commit/PR: rama aud/bug022, commit a671daa; merge 25b4aaf.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-028 · Avisos de baja, liberación y reserva viven un solo día
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: avisos de operación
- Detectado por: auditoría (agentes)
- Síntoma: Una baja con menos de 7 días nunca avisa; el aviso de «liberación en 30 días» mira contratos Activo que se renuevan solos; el aviso de reserva no sigue la spec.
- Cómo reproducir: Programar una baja a menos de 7 días; el aviso no sale.
- Causa raíz: `ServicioAvisosOperacion.cs:88-92,108-112` usan igualdad exacta (`== hoy+7`, `== hoy+30`) y `:170-181` los cierra al día siguiente. `TrabajoAlertaReserva.cs:53-69` avisa de entrada == hoy+7, cuando §3.4 pide «mensual lleva 7 días sin convertirse (una vez, persistente)».
- Solución: Avisos por ventana `[hoy, hoy+N]`, persistentes hasta que pasa la fecha o desaparece la causa. La liberación solo de `BajaProgramada`. Reserva mensual con 7 días sin convertir (clave `reserva-sin-convertir:{id}`).
- Test que lo cubre: `Bandeja/AlertasSinDuplicarTests` (5 nuevos), `Reservas/ReservaAnticipoYAlertaTests` (2).
- Commit/PR: rama aud/bug028, commit 0231914; merge d1a99f2.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-027 · La reserva corta exige horas rígidas
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: reservas
- Detectado por: auditoría (agentes)
- Síntoma: Solo se aceptan 4 h exactas u 08:00-16:00; la spec dice D11 «sin franjas fijas» y §3.4 «hora orientativa en texto libre opcional».
- Cómo reproducir: Crear una reserva corta con otra franja horaria.
- Causa raíz: `ServicioReservas.cs:454-479` y `ReservaDtos.cs:73-76` (campos obligatorios). Está marcado «DECISIÓN DEL ORQUESTADOR» sin TODO.
- Solución: Hora orientativa en texto libre opcional (`varchar(60)`, `[UnaLinea]`); la modalidad decide medio día o día completo. API: `ReservaDiaPeticion = {fecha, horaOrientativa?}`.
- Test que lo cubre: `Reservas/ReservaAuditoriaReglasTests` (3).
- Commit/PR: rama aud/bug026, commit c3f9ade; merge 3391e71.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-026 · La reserva nunca pasa a Convertida/Completada y queda en conflicto con su propio contrato
- Fecha detectado: 26/10/2026 · Fecha corregido: 28/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: reservas / contratos
- Detectado por: auditoría (agentes)
- Síntoma: Al activar el contrato del mismo cliente, su reserva queda `EnConflicto`. El conflicto por fecha de §3.4 (llega la entrada y el contrato sigue en borrador) no existe.
- Cómo reproducir: Reserva de un cliente y contrato del mismo cliente en la misma unidad; activar el contrato.
- Causa raíz: `ServicioContratos.cs:892-936`: el contrato no tiene `ReservaId`; `ContratoOcupacionTests.cs:169-216` lo prueba con el mismo cliente. `EstadoReserva.cs:30-34` contradice a `ServicioContratos.cs:884-890`.
- Solución: `contrato.reserva_id` (migración `20261027032759_ReservaHoraOrientativaYContratoReserva`). Activar el contrato convierte la reserva (`Convertida`) sin conflicto y la corta pasada pasa a `Completada`. Una mensual cuya entrada llega sin contrato activado (con contrato sin activar o sin ningún contrato) pasa a `EnConflicto`, sigue bloqueando y avisa.
- Test que lo cubre: `Contratos/ContratoDesdeReservaTests` (5).
- Commit/PR: rama aud/bug026, commits c3f9ade y e3f774e; merge 3391e71.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-025 · Finalizar a mano libera la unidad antes de la fecha efectiva y borra el pasado del calendario
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: contratos / calendario
- Detectado por: auditoría (agentes)
- Síntoma: La unidad queda libre antes de la fecha efectiva y el rango pasado desaparece del calendario.
- Cómo reproducir: Finalizar a mano un contrato con baja futura.
- Causa raíz: `ServicioContratos.cs:692-709` no exige `FechaBajaProgramada <= hoy`; `LiberarRangoAsync` pone `bloquea=false` en todo el rango en vez de recortar `fecha_fin`. `CalendarioDisponibilidadTests.cs:278-293` consagra el defecto.
- Solución: Finalizar exige baja ≤ hoy (409). `ServicioDisponibilidad.RecortarRangoAsync` conserva el pasado y libera desde la fecha de fin. `Finalizar` y `Liquidar` usan `TimeProvider`.
- Test que lo cubre: `Liquidacion/FinalizarEnSuFechaTests` (2); `CalendarioDisponibilidadTests` reescrito.
- Commit/PR: rama aud/bug023, commit fa7c9f0; merge edddf51.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-024 · La facturación periódica ignora la baja y la baja no puede ser anterior al fin del bloque
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: contratos / facturación periódica
- Detectado por: auditoría (agentes)
- Síntoma: El tramo entre el fin del bloque y la baja nunca se factura, y se factura un día de más.
- Cómo reproducir: Contrato con baja programada: la periódica sigue hasta `FechaFinVigente`; la renovación no renueva `BajaProgramada`.
- Causa raíz: `ServicioContratos.cs:616` (regla que no está en la spec, sin TODO); `ServicioRenovaciones.cs:68`; `ServicioFacturacionPeriodica.cs:322,350` factura hasta `FechaFinVigente` e ignora `FechaBajaProgramada`/`FechaFinReal`. Además `Dominio/PeriodoFacturacion.cs:45-50` (`DiasFacturables`) trata el fin como inclusivo cuando `FechaFinVigente` es exclusivo `[)`.
- Decisión del responsable (26/10/2026): la baja se puede programar en **cualquier fecha**, también antes del fin del bloque en curso (spec §3.5: preaviso recomendado, sin penalización). Se factura hasta la fecha efectiva de baja, prorrateando el último mes. Los **incrementos de renta se aplican en su fecha**, no en la renovación: la factura del periodo que contiene la fecha usa la renta nueva, prorrateada si cae a mitad de mes.
- Solución: La baja se programa en cualquier fecha (no anterior a hoy ni al inicio). Se factura hasta `FechaFinReal ?? FechaBajaProgramada ?? FechaFinVigente`, prorrateando el último tramo; fin exclusivo `[)` en `DiasFacturables`; los contratos Finalizados facturan su último tramo; los incrementos se aplican en su fecha (`Dominio/RentaPorTramos.cs`, dos líneas prorrateadas).
- Test que lo cubre: `Periodicas/PeriodicaBajaEIncrementoTests`, `Dominio/FacturacionPorTramosTests`.
- Commit/PR: rama aud/bug022, commit a671daa; merge 25b4aaf.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-023 · Al editar un borrador de contrato FechaFinVigente solo crece
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Crítica** · Estado: CORREGIDO · Módulo: contratos
- Detectado por: auditoría (agentes)
- Síntoma: Se facturan meses de más y la renovación, la baja y la alerta usan una fecha falsa.
- Cómo reproducir: Borrador de 12 meses, editar a 6: el fin vigente sigue a 12 meses. La huella no lo incluye.
- Causa raíz: `ServicioContratos.cs:1198-1201`.
- Solución: En Borrador y PendienteAprobacion, `FechaFinVigente = FechaFinInicial` al editar.
- Test que lo cubre: `Contratos/ContratoBorradorFinTests` (2).
- Commit/PR: rama aud/bug023, commit fa7c9f0; merge edddf51.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-022 · Facturación periódica no atómica: un borrador huérfano bloquea el contrato
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: facturación periódica
- Detectado por: auditoría (agentes)
- Síntoma: Un fallo a mitad de lote deja un borrador huérfano que bloquea ese contrato para siempre y aborta el resto del lote. Una doble ejecución concurrente da un 409 parcial con los mismos efectos.
- Cómo reproducir: Provocar un fallo en `AgregarLinea` o `Emitir` de un contrato en medio de un lote.
- Causa raíz: `ServicioFacturacionPeriodica.cs:134` (bucle sin try) y `:337-341` (las existentes cuentan Borrador). Crear/AgregarLinea/Emitir van en transacciones separadas y `CerrarEjecucionAsync` no se ejecuta. El comentario del servicio es falso.
- Solución: Cada contrato se factura en su propia transacción. Un fallo deja la alerta `factura-periodica-fallo:{contrato}:{yyyy-MM}`, la respuesta lista `fallidas`/`fallidos`, el lote sigue y la ejecución se cierra siempre. Un borrador huérfano de la misma moneda se reutiliza; la doble ejecución concurrente no duplica. Regresión al integrar con BUG-033 (reutilizaba el borrador de otra moneda) corregida en b4de4bb.
- Test que lo cubre: `Periodicas/PeriodicaAtomicaTests` (3).
- Commit/PR: rama aud/bug022, commits a671daa y b4de4bb; merge 25b4aaf.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-021 · Modificar una factura emitida con cobros no revisa lo cobrado
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Crítica** · Estado: CORREGIDO · Módulo: facturas
- Detectado por: auditoría (agentes)
- Síntoma: La factura baja de importe por debajo de lo cobrado y el exceso no se refleja en ningún sitio.
- Cómo reproducir: Factura en serie sin comprobante, cobrado 1000; modificarla a 600: 400 de sobrecobro sin reflejo.
- Causa raíz: `ServicioFacturas.ModificarAsync` (652-720) no compara el nuevo total con lo cobrado.
- Solución: `ModificarAsync` rechaza (409) bajar el neto por debajo de lo cobrado más lo ajustado.
- Test que lo cubre: `CicloFactura/FacturaModificacionCobradoTests`.
- Commit/PR: rama aud/bug020, commit cf7736d; merge 8ce1c8f.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-020 · Anular una factura de un cobro multi-factura hace desaparecer el dinero de esa parte
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Crítica** · Estado: CORREGIDO · Módulo: facturas / cobros
- Detectado por: auditoría (agentes)
- Síntoma: El importe asignado a la factura anulada queda sin destino ni rastro.
- Cómo reproducir: Cobro de 1000 = factura A 500 + factura B 500; anular A: 500 quedan sin rastro.
- Causa raíz: `ServicioFacturas.AnularCobrosDeFacturaAsync` (~1342-1415): la asignación pasa a `Vigente=false` y, si el cobro conserva otras asignaciones, no cambia nada más.
- Decisión del responsable (26/10/2026): **nada de saldo a favor**, algo simple. Al anular la factura A se anula solo la asignación de A y su importe queda **marcado como devuelto** en el cobro (importe devuelto, con fecha, usuario y motivo = la anulación de A). La invariante pasa a ser: recibido = asignado vigente + excedente + devuelto.
- Solución: Columna `cobro.importe_devuelto` (migración `20261026205927_CobroImporteDevuelto`, CHECK `ck_cobro_devuelto_acotado`). Al anular una factura se anula solo su asignación y su importe, en la moneda recibida, queda como devuelto en el cobro; sin saldo a favor. Invariante: recibido = asignado vigente + excedente + devuelto.
- Test que lo cubre: `CicloFactura/FacturaAnulacionDevueltoTests` (3).
- Commit/PR: rama aud/bug020, commit cf7736d; merge 8ce1c8f.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-019 · Dos cobros simultáneos sobre la misma factura se aceptan
- Fecha detectado: 26/10/2026 · Fecha corregido: 27/10/2026
- Gravedad: **Crítica** · Estado: CORREGIDO · Módulo: cobros
- Detectado por: auditoría (agentes)
- Síntoma: Doble cobro invisible: la factura queda «Pagada» con el doble cobrado y el sobrecobro no se ve por el clamp a 0.
- Cómo reproducir: Factura USD 1000 y dos cobros de 1000 concurrentes: cobrado 2000.
- Causa raíz: `ServicioCobros.cs:320-390` (`RegistrarAsync`) y `:704-870` (`PlanificarAsync`) leen el pendiente sin bloqueo; no hay `FOR UPDATE` ni advisory lock fuera de auditoría y `xmin` solo existe en `saldo_a_favor`. Mismo problema en el ajuste por diferencia de cambio (`ServicioCobros.cs:~500-590`). Arreglo propuesto: lock por contrato (`pg_advisory_xact_lock`) o `SELECT … FOR UPDATE` de las facturas al planificar.
- Solución: `CerrojoCobros.TomarAsync` toma `pg_advisory_xact_lock(int4 'COBR', hash del contrato o del cliente)` al registrar cobros, registrar ajustes, anular cobros y (6cdc13d) al anular o rehacer una factura. El pendiente se lee ya con el cerrojo tomado.
- Test que lo cubre: `Cobros/CobroAuditoriaCobrosTests.DosCobrosSimultaneosNuncaAsignanMasQueElNetoDeLaFactura` (fallaba: asignado 2000).
- Commit/PR: rama aud/bug019, commits 06a9632 y 6cdc13d; merge 6d5dfff.
- Datos afectados: Ninguno: no hay datos de producción.

### BUG-018 · La CI terminaba en rojo sin ejecutar ningún job, por un `hashFiles` en un `if` de nivel de job
- Fecha detectado: 15/10/2026 · Fecha corregido: 19/10/2026 (orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: CI (`.github/workflows/ci.yml`)
- Detectado por: agente, revisando el estado del repositorio en F0-05. **El defecto estaba documentado en `tarea/F0-preparacion` desde el 15/10/2026 y nunca se integró; se descubre al unificar esa rama.**
- Síntoma: los disparos de `CI` terminaban en `failure` con `total_count: 0` jobs y `created_at` = `updated_at`. Ni siquiera se creaba el job `secretos`, que no usa ninguna expresión.
- Cómo reproducir: hacer push de cualquier commit a una rama con el workflow anterior y abrir la pestaña Actions.
- Causa raíz: el `if` de nivel de job con `hashFiles(...)` en los jobs `backend` y `frontend`. En `jobs.<job_id>.if` GitHub solo admite las funciones `always`, `cancelled`, `success` y `failure`; `hashFiles` está permitido en `steps.if` pero **no** en el `if` de nivel de job ([contexts](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts)). Cuando la expresión no se puede evaluar, GitHub invalida el workflow entero. Se descartó que fuera un problema de codificación (sin BOM ni tabuladores), de YAML (`yaml.safe_load` lo parsea sin errores) o de estructura: validado contra el esquema oficial, el archivo antiguo da 0 errores igual que el nuevo, así que el rechazo no era de esquema sino de evaluación en ejecución. Tampoco es un problema de permisos ni de saldo de Actions.
- Solución: se eliminó el `if` de nivel de job. Los tres jobs se crean siempre; cada uno empieza con un paso de detección que escribe `existe=true|false` en `$GITHUB_OUTPUT`, y los pasos de build y test se condicionan con `if: steps.detecta.outputs.existe == 'true'`. El detector de la solución busca el `.sln`/`.slnx` en todo el subárbol y no solo en la raíz, porque los proyectos van en `backend/`; el de `frontend` fija `working-directory: .` para que no herede el `frontend` del job.
- Test que lo cubre: no hay suite todavía. Comprobación manual: que el disparo cree jobs. Verificado en local con tres pruebas: validación contra el esquema oficial `json.schemastore.org/github-workflow.json` (0 errores en el archivo nuevo), simulación de los dos scripts bash en seis escenarios (repo vacío, `.sln` en la raíz, `.sln` anidada en `backend/`, `frontend` presente desde la raíz, `cwd=frontend` con y sin `package.json`), y contraste con la tabla de contextos de GitHub.
- Commit/PR: `tarea/F0-preparacion` (documentado) y `V-01.00` (integrado).
- Datos afectados: ninguno.
- **Riesgo que queda (dicho, no arreglado):** al integrar este arreglo se han conservado a mano las dos comprobaciones que F6-ia había añadido después al `ci.yml` y que la versión de F0 no tenía —el `setup-node` 24.15.0 en el job `backend` y la verificación de que el paquete publicado lleva `wwwroot/index.html`. Un merge directo de `tarea/F0-preparacion` habría revertido las dos. Si vuelve a tocarse este fichero, comprobar las tres cosas juntas: paso `detecta`, Node 24.15.0 en `backend`, y `test -f /tmp/paquete/wwwroot/index.html`.

### BUG-017 · El dashboard ignoraba el filtro de dueña en silencio, por un `propietarioId` mal escrito
- Fecha detectado: 18/10/2026 · Fecha corregido: 18/10/2026 (orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: F5-01 · informes / dashboard
- Detectado por: la **verificación de aceptación** del bloque, al comprobar que el contrato del frontend coincide con el de la API.
- Síntoma: `GET /api/v1/informes/dashboard?propietariaId={id}` **no filtraba por dueña**: devolvía el dashboard entero, con los ocho bloques y los indicadores sin tocar, y **sin ningún error**. El mismo filtro funcionaba en `/informes/resumen` y en `/informes/exportar`. El frontend, que manda `propietariaId` en los tres, llevaba un filtro vivo y dos muertos sin que nadie lo notara.
- Cómo reproducir: (1) dos unidades de dueñas distintas con una factura cada una; (2) `GET /api/v1/informes/dashboard?clienteId={c1}&propietariaId={dueña de la otra}` → sale la factura de `c1`, cuando no puede quedar ninguna. Con `propietarioId` —sin la «a»— el mismo filtro sí acota, que es la prueba de que el parámetro se estaba declarando con otro nombre.
- Causa raíz: **el parámetro del dashboard se llamaba `propietarioId`** (`InformesController.cs:85`) mientras que el de `Resumen` y el de `Exportar` se llaman `propietariaId`. ASP.NET **no rechaza los parámetros que la acción no declara**: los ignora. Por eso el síntoma no era un 400 sino un informe completo y plausible, y por eso ninguna prueba lo cazó: los once filtros se probaban **contra el `resumen`**, que estaba bien escrito. El fallo estaba en la firma de una acción, no en la lógica, y la lógica es la misma para los tres endpoints.
- Solución: el nombre del parámetro en el dashboard, `propietarioId` → `propietariaId`. **Una línea**, sin tocar nada más: el valor ya se pasaba a `FiltroInformes` en la posición correcta y el servicio siempre aplicó el filtro bien.
- Test que lo cubre: dos, en `backend/AtlasRent.Tests/Informes/InformeResumenTests.cs`. `LosTresEndpointsDeclaranLosMismosOnceFiltros` afirma por **reflexión** que `Resumen`, `Dashboard` y `Exportar` declaran los once filtros de §3.15 con los mismos nombres: es lo que caza una errata de este tipo aunque el parámetro sea otro, y no depende de datos. `ElDashboardAplicaElFiltroDePropietaria` va por HTTP con dos facturas en unidades de dueñas distintas y afirma que filtrar por el cliente de una **y** la dueña de la otra no deja nada, mientras que sin el filtro de dueña sí sale la del cliente. **Comprobado que ambos fallan con el bug presente** (2 errores) y pasan con el arreglo.
- Commit/PR: `tarea/F5-01-informes-dashboard`.
- Datos afectados: ninguno. Es un endpoint de lectura recién construido; no se ha puesto en producción ni ha lingered en un backup.
- **Riesgo que queda (dicho, no arreglado):** esta clase de fallo —una acción que acepta menos parámetros de los que el resto, y ASP.NET los ignora en silencio— **no la cubre ningún mecanismo del repo** salvo las pruebas que se escriban para ese endpoint concreto. Las dos de aquí la cierran para F5, pero cualquier endpoint futuro con once parámetros escritos a mano repite laoccasion. La regla que sale de esto es que **los tres endpoints con el mismo conjunto de filtros deben|Probarse contra el mismo conjunto**, no uno de ellos.

### BUG-016 · El filtro por propietaria de la vista única no heredaba la del inmueble y devolvía cero filas
- Fecha detectado: 18/10/2026 · Fecha corregido: 18/10/2026 (orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: F5-01 · vista única / informes
- Detectado por: un **test** nuevo de F5-01 (`LaUnidadSinPropietariaPropiaSaleAlFiltrarPorLaDelInmueble`), escrito para cubrir el filtro `propietariaId` que el propio bloque añadió y que no tenía ninguna prueba.
- Síntoma: filtrar la vista única y el informe por una propietaria **no devolvía ninguna fila** de las unidades que no tienen `propietaria_id` propio, es decir de todos los alquileres de un edificio entero. Las unidades con dueña propia sí salían: el filtro parecía funcionar en los datos de siempre y fallaba justo en el caso que §3.1 dice que tiene que funcionar.
- Cómo reproducir: (1) crear una subempresa propietaria; (2) crear un inmueble **sin** dueña y una unidad dentro; (3) poner la dueña en el inmueble; (4) `GET /api/v1/cuentas?propietariaId={id}` — la fila de esa unidad no aparece, aunque la vista sin filtro sí la enseña. Con `propietaria_id` puesto en la unidad, el mismo filtro la devuelve.
- Causa raíz: la rama que implementa la herencia (§3.1) correlacionaba el inmueble con **el identificador de la unidad**: `contexto.Inmueble.Any(i => i.Id == f.UnidadId && i.PropietariaId == propietaria)`. `f.UnidadId` es la llave de `unidad`, no la de `inmueble`, así que el `EXISTS` comparaba el id de un inmueble con el id de una unidad y no encontraba nunca nada. La condición correcta es `i.Id == f.Unidad!.InmuebleId`, que es la que ya usaba el filtro equivalente del dashboard y por eso ese no estaba afectado. No era un fallo de la regla sino de la llave con la que se cumplía, y por eso ninguna prueba existente lo cazaba: el filtro no tenía ninguna.
- Solución: una condición, en `ServicioCuentas.FacturasFiltradas`: `i.Id == f.Unidad!.InmuebleId`. Sin más cambios de comportamiento: la rama de la unidad con dueña propia y la del resto del filtro quedan igual.
- Test que lo cubre: `backend/AtlasRent.Tests/Informes/InformeResumenTests.cs` — `LaUnidadSinPropietariaPropiaSaleAlFiltrarPorLaDelInmueble`, escrita **antes** de corregir y marcada en `Skip` con el motivo; el `Skip` se quitó al corregir. La cubre además `ElFiltroDePropietariaAcotaLaVistaYElInforme` (el filtro acota) y `ElFiltroDeFacturadoraAcotaLaVistaYElInforme` (el otro filtro nuevo, que nunca estuvo afectado).
- Commit/PR: `tarea/F5-01-informes-dashboard`.
- Datos afectados: ninguno. El defecto era de lectura de una vista que no existía antes de F5-01, así que no hay facturas ni saldos que corregir en ninguna instalación.
- **Riesgo que queda (dicho, no arreglado):** el filtro `propietariaId` **excluye las fianzas**, igual que el resto de filtros que una fianza no puede expresar (§3.13, `DOC_TECNICA.md` §6.7). Con los cinco filtros nuevos hay cinco caminos por los que las fianzas desaparecen del informe, y eso está escrito y es coherente, pero conviene tenerlo presente al comparar el informe con el pie de la vista cuando hay fianzas en el periodo.

### BUG-015 · Cinco pruebas desfasadas por F4-05: cuatro con la cuenta de jobs y una afirmando el valor de una fila de configuración compartida
- Fecha detectado: 18/10/2026 · Fecha corregido: 18/10/2026 (worker)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas / F4-05 (jobs) y pruebas / configuración (F4-05)
- Detectado por: la **suite completa** tras aterrizar F4-05 (766 pruebas, 5 errores). Cuatro en `Jobs/ProgramacionJobsTests.cs` y una en `Configuracion/ConfiguracionCrudTests.cs`.
- Síntoma: (a) `RegistrarDejaCadaTrabajoConSuCronYSuMetodo` esperaba **12** rutas registradas y hay **13**; (b) `LosTrabajosDeLaSeccion8QueTodaviaNoExisteNoSePrograman` esperaba **12** tipos y hay **13**; (c) `ElFinVaDetrasDelTrabajoDeMadrugadaAnterior` fallaba con «Expected fin.HoraLocal to be greater than 9 **because el fin va detrás de recordatorios-pago**, but found 7»; (d) `LosAvisosDeOperacionVanLaUltimaDeLaMadrugada` con «Expected avisos.HoraLocal to be greater than 9 …, but found 8»; (e) `LasToleranciasYElModoDeRecordatoriosSonConfigurables` con «Expected … modoRecordatorios to be the same string … `"Manual"` (expected) / `"Apagado"` (actual)».
- Cómo reproducir: (1) ejecutar la suite completa con F4-05 dentro; (2) para (e), ejecutar `--filter "FullyQualifiedName~AtlasRent.Tests.Configuracion|FullyQualifiedName~AtlasRent.Tests.Recordatorios"` con `Recordatorios/` antes en la misma base, que es lo que hace la suite por orden de colección.
- Causa raíz: **dos causas distintas y solo una es la misma de siempre.** (1)-(d) Son **expectativas desfasadas por una tarea nueva**: F4-05 añadió `recordatorios-pago` a `ProgramacionJobs.Todos` y alguien actualizó los tests de forma mecánica, sumando números y cambiando el texto del motivo sin volver a mirar la cuenta. Los dos fallos (c) y (d) no son solo un número desfasado: el motivo escrito era **falso** —«el fin va detrás de recordatorios-pago» cuando el fin corre a las 07:00 y los recordatorios a las 09:00—, y el nombre de (d) («la última de la madrugada») había dejado de ser cierto sin que nadie lo decidiera ni lo escribiera. La actualización automática de una expectativa es el caso inverso del que BUG-013 registra: allí la afirmación dependía del estado que dejaron otras pruebas; aquí depende del estado que dejó **la última tarea**, que es el mismo modo de fallo con la fuente cambiada. (e) Es **una aparición más de la clase de BUG-013**: la prueba afirmaba el **valor absoluto** de `modoRecordatorios` (y de las tolerancias) en una fila única de `ajuste_general` que es de toda la instalación y que escriben otras pruebas (`Cobros/AyudaCobros.FijarToleranciaAsync` y `Recordatorios/AyudaRecordatorios.PonerModoAsync`); el fallo observado además ocurrió en la respuesta del propio `PUT`, no en el `GET`: otra prueba escribió `Apagado` entre medias. La afirmación no dependía de la lógica que la prueba dice comprobar sino del orden de ejecución.
- Solución: **`backend/AtlasRent.Tests/Jobs/ProgramacionJobsTests.cs`**, cuatro cambios, cada uno sobre la invariante que sigue siendo cierta y no sobre el número que había: (1) `RegistrarDejaCadaTrabajoConSuCronYSuMetodo` suma `RutaRecordatorios` a la lista de rutas registradas; (2) `LosTrabajosDeLaSeccion8QueTodaviaNoExisteNoSePrograman` **se renombra** a `LoProgramadoEsExactoLoQueDiceLaSeccion8`: la lista esperada suma `TrabajoRecordatorios` y el test pasa a afirmar además, contra la clase que sí existe, que **`TrabajoEnvio` no está** en `ProgramacionJobs.Todos` —los envíos son una cola que se llena al confirmar un lote (§8.0.2)—, que es la forma de comprobar que lo que §8 deja como *previsto* (alertas de vencimiento de factura, outbox, limpieza, actualizaciones) no entra sin depender solo de una lista que escribió el propio código; (3) `ElFinVaDetrasDelTrabajoDeMadrugadaAnterior` excluye `recordatorios-pago` de la lista de «anteriores» —la invariante que afirma es que el fin va detrás de **los trabajos anteriores a él**, no detrás de todos— y añade la comparación explícita con los avisos de operación, que sí van detrás; (4) `LosAvisosDeOperacionVanLaUltimaDeLaMadrugada` excluye también a los recordatorios del bucle y afirma lo contrario: que van **por delante** de ellos, con el motivo de §8.0.3. **El nombre de este último se conserva**: las 09:00 ya no son madrugada, así que los avisos de operación siguen siendo el final de la madrugada; lo que dejó de ser cierto es «el último del día», que no es lo que el nombre dice. **`backend/AtlasRent.Tests/Configuracion/ConfiguracionCrudTests.cs`**: `LasToleranciasYElModoDeRecordatoriosSonConfigurables` deja de leer el estado previo (que no es suyo) y afirma solo lo que sí es suyo: que el `PUT /api/v1/config/ajustes` guarda `toleranciaCambioUsd = 2.35` (redondeo a 2 decimales), `toleranciaCambioDop = 60` y `modoRecordatorios = "Manual"`, y que el `GET` posterior devuelve lo mismo. **Sin tocar el producto**: no se ha modificado nada en `backend/AtlasRent.Api/`.
- Test que lo cubre: los cinco, en `backend/AtlasRent.Tests/Jobs/ProgramacionJobsTests.cs` (`RegistrarDejaCadaTrabajoConSuCronYSuMetodo`, `LoProgramadoEsExactoLoQueDiceLaSeccion8`, `ElFinVaDetrasDelTrabajoDeMadrugadaAnterior`, `LosAvisosDeOperacionVanLaUltimaDeLaMadrugada`) y `backend/AtlasRent.Tests/Configuracion/ConfiguracionCrudTests.cs` (`LasToleranciasYElModoDeRecordatoriosSonConfigurables`). Ninguno es nuevo: se corrigen los que ya afirmaban el asunto.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno en BD; no hay datos que corregir ni migración.
- **Riesgo que queda (dicho, no arreglado):** (a) El `PUT`/`GET` de la prueba de configuración sigue **_potencialmente_ en carrera** con `PonerModoAsync` y `FijarToleranciaAsync`, que escriben la misma fila desde otras colecciones y se ejecutan en paralelo: entre nuestro `SaveChanges` y el `GET` final cabe otro escritor, y entonces la prueba puede fallar sin que el producto tenga nada que ver. Lo que se ha eliminado es la afirmación sobre el estado que hubiera antes, que fallaba **siempre** con la suite completa; esta ventana es de hecho poco probable y no hay arreglo de pruebas que la cierre sin serializar el acceso a `ajuste_general`. (b) La afirmación que se ha quitado —«las tolerancias y el modo nacen sin valor»— **no queda cubierta en ningún sitio**: es verdad de la migración, no de la API, y comprobarlo necesita una base recién sembrada, no la compartida. Si se quiere, es una prueba de migración. (c) El resto del bloque F4 sigue con la misma clase de problema sin tocar: los cinco fallos eran de pruebas, pero la causa de fondo —afirmar sobre lo que dejaron otros en una base compartida— sigue abierta en lo que BUG-013 ya dejó anotado. (d) `ProgramacionJobs.Todos` **no tiene colisión ni orden raro**: 02, 03, 04, 05×2, 06×5, 07, 08 y 09, con las cuatro compartidas justificadas una a una en el propio código y en §8. Con F4-05 la fila de las 09:00 no comparte hora con nadie y queda detrás de todo, que es lo que §8.0.3 quiere. No hay nada que decidir aquí.

### BUG-014 · Un empleado podía descargar un estado de cuenta entero con `factura.ver`
- Fecha detectado: 18/10/2026 · Fecha corregido: 18/10/2026 (worker)
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: F4-02 · envíos / permisos
- Detectado por: revisión de la regla de AGENTS.md §6 («el empleado nunca recibe totales agregados por ningún endpoint, tampoco por uno de detalle») contra el endpoint de F4-02 `GET /api/v1/envios/{id}/pdf`.
- Síntoma: el empleado recibía **200 y el PDF entero** de un envío de tipo `EstadoCuenta` —filas, totales por moneda y saldo pendiente del cliente— con su permiso normal de lectura, sin haber pasado nunca por `informe.ver`.
- Cómo reproducir: (1) un gerente o administrador manda el estado de cuenta de un cliente (`POST /api/v1/envios` con `tipoDocumento = "EstadoCuenta"`), o lo manda un recordatorio automático, que lleva ese mismo PDF adjunto; (2) el empleado entra en `GET /api/v1/envios/{id}/pdf` con ese identificador; (3) descarga el informe completo. Tres clics, ninguno fuera de los permisos que §2 le da.
- Causa raíz: el endpoint se cerraba con `[Accion(Acciones.FacturaVer)]` y `CopiaAsync` **no comprobaba nada**: el permiso se Miranda solo por el tipo de envío, no por lo que lleva el adjunto. Y el comentario que lo justificaba («quien puede ver una factura puede ver la copia que se le mandó») era una regla sobre documentos individuales aplicada a algo que no es un documento individual. El mismo hueco estaba en el otro lado: `EnviarAsync` y `EnviarPendienteAsync` validaban el permiso por **tipo de documento** (`documento.enviar` / `cotizacion.enviar`), de modo que `documento.enviar` —que es un permiso de envío— acabó siendo de hecho la puerta de los totales agregados.
- Solución: un único mapa, `ServicioEnvios.AccionesDeContenido(tipo)`, con el criterio de §2 y AGENTS.md §6 escrito dentro — **lo que es un documento individual se lee con la acción de lectura de su propio recurso; lo que lleva totales agregados exige `informe.ver`** — y el mapa se usa en los **tres** caminos (mandar, mandar una fila encolada y descargar la copia). `CopiaAsync` ahora recibe el usuario de la petición y comprueba; el endpoint le pasa el suyo y convierte el `PermisoDenegadoException` del servicio en 403. El atributo `[Accion]` **se queda** como puerta de pelo grueso, con el comentario corregido: un atributo solo ve el verbo y la ruta, no puede mirar el cuerpo ni el adjunto, y por eso la comprobación fina no puede vivir ahí. Sin cambios en F4-02/F4-03 por lo demás: la copia sigue siendo la guardada, el listado igual y los reintentos igual.
- Test que lo cubre: `backend/AtlasRent.Tests/Mensajeria/EnvioCopiaPermisosTests.cs` — `ElEmpleadoNoDescargaLaCopiaDeUnEstadoDeCuenta` (el defecto), `ElEmpleadoTampocoEnviaUnEstadoDeCuenta`, `ElEmpleadoSiguePudiendoDescargarLaCopiaDeUnaFactura` (no rompe el caso legítimo) y `GerenteYAdministradorSiDescarganLaCopiaDelEstadoDeCuenta`. **Comprobado que el primero falla sin el arreglo**: con el estado de cuenta mapeado a `factura.ver` —el comportamiento anterior— el empleado recibe 200; con `informe.ver` recibe 403.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno en BD; no hay datos que corregir ni migración. Los envíos mal descargados no dejan rastro que seguir y el `pdf_copia` sigue siendo lo que se mandó, así que no hay que regenerar nada. **Sí queda un residuo**: los denegados por esta puerta se auditan como `permiso.denegado.informe.ver` en `envio/<id>`, así que en una instalación donde ya pasó se puede ver a quién se le dio y cuántas veces.
- **Riesgo que queda (dicho, no arreglado):** el hueco era del bloque entero y este arreglo cierra los dos caminos que el encargo nombraba. Siguen abiertos, sin tocar: (a) el **listado** `GET /api/v1/envios` sigue con `factura.ver`, así que un empleado ve que se mandó un estado de cuenta y a quién — metadato, ningún importe, pero es un hecho que la matriz no le concede; (b) el **envío masivo** (`/envios/masivo/*`, `mensajeria.masivo`) y el **listado de recordatorios** siguen aplicando el permiso por tipo/cantidad y no por contenido del adjunto. Ninguno de los dos entrega un total agregado hoy: lo que se descarga es por `mensajeria.masivo` (gerente y admin, los dos con `informe.ver`) o por la copia, que ya está cerrada. Lo que sí tienen es que el permiso depende de un atributo que no mira el adjunto, y esa es exactamente la clase de fallo que acaba de aparecer una vez.

### BUG-013 · Cuatro pruebas afirmaban sobre un elemento concreto de un listado sin filtro, y solo veían la primera página
- Fecha detectado: 18/10/2026 · Fecha corregido: 18/10/2026 (worker)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas / pasada de BUG-011 previa a F4
- Detectado por: auditoría de los cuatro sitios citados en el encargo de BUG-011, confirmada leyendo el código y los tres endpoints afectados.
- Síntoma: fallos **dependientes del volumen y del orden de ejecución**, nunca de la lógica que la prueba dice comprobar. Tres caras distintas: (a) `AyudaPlantilla.LiberarTipoAsync` leía `GET /api/v1/plantillas-anexo?todos=true` con el `pageSize` por defecto (25) y solo desactivaba lo que salía en la primera página; como el catálogo es de uno activo por tipo, el anexo activo de otra prueba se quedaba vivo y la prueba que venía a usarlo recibía un **409 «el catálogo es de uno activo por tipo»** que no dice nada de paginación —y lo que se rompía era la prueba siguiente, no la que había dejado la fila—. (b) Los ayudantes `BandejaAsync` / `FilaPropia` de `BandejaAprobacionesTests` (9 pruebas) leían `GET /api/v1/contratos/solicitudes` sin filtro, y el orden por defecto es `solicitadoAt` **ascendente** (`ServicioAprobaciones.cs:88`), así que las solicitudes más antiguas —las de las demás pruebas de la colección— se comen la primera página y la fila de la prueba se queda fuera: `FilaPropia` falla con «una solicitud viva por contrato». (c) `AnexoCatalogoTests.ElCatalogoSoloEnseñaLosAnexosActivos` afirmaba `Contain`/`NotContain` del anexo del test sobre la primera página del catálogo. (d) `TiposImpuestoTests` hacía `.Single(t => t.Id == id)` sobre la primera página de `GET /api/v1/config/tipos-itbis`.
- Cómo reproducir: dejar más filas de las que caben en una página (25 por defecto) en la tabla correspondiente antes de ejecutar el filtro. Para (b) basta con que otras pruebas dejen contratos en `PendienteAprobacion` más antiguos que el de la prueba; el bloque F4 va a añadir muchos, que es justo lo que dispara el fallo. Con la base por debajo del umbral todo pasa, y por eso el defecto se cuela.
- Causa raíz: **una sola base de datos compartida por la colección** + `pageSize` por defecto (25, `ConsultaLista.PageSizePorDefecto`) + orden por defecto ascendente en el peor sentido. Los cuatro endpoints afectados (`plantillas-anexo`, `contratos/solicitudes`, `config/tipos-itbis`) **no aceptan ningún filtro discriminante**: solo `page`, `pageSize`, `sortBy` y `sortDir` (`todos` en el primero). No había forma de que la afirmación fuera sobre un conjunto que la prueba controla, y ordenarse por `createdAt`/`solicitadoAt` en descendente tampoco lo arregla: seguiría dependiendo de qué otra prueba dejó su fila más tarde.
- Solución: recorrido de páginas en los cuatro sitios, que es el criterio que ya aplican `AyudaComprobante.SerieAsync` (`Comprobantes/AyudaComprobante.cs:109-129`) y el `pageSize=200` que obliga `AyudaCuentas.VistaAsync`. Ninguna afirmación se ha debilitado. (1) `AyudaPlantilla`: nuevo `CatalogoAsync(sesion, todos, ct)`, que recorre todas las páginas con `pageSize=200` y corta en `totalPages`; `LiberarTipoAsync` lo usa y **reúne primero todos los identificadores y después desactiva**, porque si desactivara mientras recorre, el catálogo menguaría durante el salto de páginas y se saltaría filas. (2) `AnexoCatalogoTests`: las tres lecturas del catálogo pasan por `CatalogoAsync`; los `Contain` / `NotContain` se aplican al conjunto entero (mismo predicado y mismo mensaje). (3) `BandejaAprobacionesTests.BandejaAsync`: recorre páginas con `sortBy=solicitadoAt` y devuelve **toda** la bandeja, así que las 9 pruebas que la usan —incluido el `NotContain` de la caducada y la resuelta, que ahora sí mira la bandeja entera— dejan de depender del volumen; `FilaPropia` y las aserciones no se tocan. (4) `TiposImpuestoTests.PorcentajeDeLaRespuestaAsync` pasa a recibir la sesión y recorre páginas hasta `totalPages`, con `ContainSingle(t => t.Id == id).Which` en lugar de `.Single(...)`: exactamente la misma exigencia (que el identificador aparezca una vez y con ese porcentaje), ya sin quedar ceñida a la primera página.
- Test que lo cubre: los propios cuatro, ejecutados con su filtro. Lo que **no** cubren: que la primera página ya no baste cuando hay muchas filas, porque en la base de pruebas el volumen sigue por debajo del umbral; lo que se ha corregido es la dependencia, y eso se ve leyendo los ayudantes, no en un fallo que ya no se puede provocar.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno; es infraestructura de pruebas.
- **Actualización del riesgo (18/10/2026, orquestador):** de los sitios que se listaban como riesgo abierto, dos los cerró después el trabajo de **F4-01b**: `Configuracion/SubempresaRncCifradoTests.cs` ahora busca en el listado recorriendo páginas (`BuscarEnElListadoAsync`) y `Facturas/FacturaPdfTests.cs` recorre páginas con el mismo criterio (`ListarAsync`). **Siguen abiertos**: (a) `AyudaComprobante`, que pide `pageSize=200` **sin filtrar nada** y busca por identificador; (b) las pruebas que `NotContain` sobre la primera página (`Clientes/ClientePermisosTests.cs:137`, `Cotizaciones/CotizacionValidacionTests.cs:202`). Y el riesgo estructural no es de estas pruebas sino del endpoint: **`contratos/solicitudes`, `plantillas-anexo` y `config/tipos-itbis` no ofrecen filtro**, así que toda prueba nueva que afirme sobre un elemento propio tiene que recorrer páginas, y el coste crece con la base. Añadir un filtro discriminante a esos listados (o una guarda que rechace el patrón en el proyecto de pruebas) es la solución de fondo, y es una decisión de producto.

### BUG-011 · Una prueba de reservas solo pasaba mientras la base compartida tuviera menos reservas que una página
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026 (orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas / F2-03 (detectado al cerrar F3)
- Detectado por: la **suite completa** del bloque F3: `Reservas.ReservaMensualTests.CancelarLiberaElBloqueo` falló con la lista de reservas «no contiene la que el test acaba de cancelar». En solitario pasa, y con la carpeta `Reservas` pasa (21/21).
- Síntoma: el fallo aparece solo cuando se ejecuta todo. Con la línea base (477 pruebas) no pasaba; con F3 (699 pruebas) sí.
- Causa raíz: `ListarIdsAsync` pedía `GET /api/v1/reservas?incluirInactivas=true` **sin filtro y sin paginar**, y leía la primera página. La base es única y compartida por la colección de pruebas, así que la afirmación dependía de cuántas reservas habían creado los demás tests: mientras la primera página alcanzaba, la cancelada estaba dentro; en cuanto el bloque F3 creó más de las que caben, quedó fuera. No era el producto: la reserva se cancelaba bien.
- Solución: el helper filtra por `clienteId`, que es el cliente que crea el propio test. La afirmación pasa a ser sobre sus reservas y susyas, que es lo que dice comprobar, y deja de depender del tamaño de la base.
- Test que lo cubre: el mismo. Lo que **no** cubre: otras pruebas del repositorio con el mismo patrón (listar sin filtro sobre una base compartida y afirmar sobre un elemento concreto). Es una búsqueda que merece una pasada propia antes de F4.
- Riesgo que queda: el fallo era dependiente del orden de ejecución y del volumen; el arreglo lo neutraliza **para esta prueba**, no para la clase de problema.
- Commit/PR: pendiente del orquestador.

### BUG-012 · `UmbralAlerta` del numerador fiscal se podía omitir en la petición y llegaba como 0
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026 (orquestador)
- Gravedad: **Baja** · Estado: CORREGIDO · Módulo: configuración / F3-03
- Detectado por: la guarda ampliada de la auditoría del bloque (BUG-010): `CifrasFiscalesEnCodigoTests` ahora comprueba que los campos de configuración fiscal no tengan valor por defecto, y este campo era el único de sus seis sin `required`.
- Síntoma: una petición `PUT` del numerador que omitiera `umbralAlerta` se deserializaba con `0` y el servicio respondía **409 de negocio** («umbral que no vale») en lugar del **400** de petición incompleta.
- Causa raíz: `[Required]` no hace nada sobre un `int` no anulable en la deserialización de System.Text.Json; hace falta `required`. Los cinco campos hermanos (`RangoDesde`, `RangoHasta`, `FechaVencimiento`, `SiguienteNumero`, `TipoPrefijo`) sí lo tenían.
- Solución: `public required int UmbralAlerta`, con el porqué escrito en el propio DTO. Sin corrupción de datos posible: un umbral de 0 lo rechazaba el servicio de todas formas.
- Test que lo cubre: la guarda ampliada de BUG-010, que falla si vuelve a desaparecer el `required`.
- Commit/PR: pendiente del orquestador.

### BUG-010 · Dos pruebas de F3 no comprobaban lo que su nombre y su criterio de aceptación prometían
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026 (worker)
- Gravedad: **Media** · Estado: CORREGIDO (con un hallazgo de producto abierto, ver «Riesgo») · Módulo: pruebas / F3-06 + F3-03
- Detectado por: auditoría independiente del bloque F3 (solo lectura).
- Síntoma (a): `CobroSaldosPropertyTests.DoscientosCasosAleatoriosNoDejanNingunSaldoDescuadrado`, que es el criterio de aceptación «200 casos aleatorios: ningún saldo descuadra» de `docs/PLAN.md`, comprobaba `Σtotal − Σretenciones − Σcobrado == Σpendientes`. Como `Dominio/CalculoCobro` acota `Pendiente` a cero, esa igualdad **solo es satisfacible mientras ninguna factura quede cobrada de más**, y no puede darse por la vía normal: `ServicioCobros` recorta cada línea con `Math.Min(pendiente, …)` y manda el sobrante a `excedente`, y de ahí a `saldo_a_favor`. Ni `excedente` ni el crédito entraban en la fórmula, así que **la prueba pasaba igual con la rama de sobrepago rota**, que es la que mueve los totales de §3.13 y de la vista de cuentas de F3-10. El generador además no producía sobrepagos a propósito: la rama se recorría por azar y, en la práctica, casi nunca.
- Síntoma (b): `CifrasFiscalesEnCodigoTests.ElCodigoDeLaAplicacionNoTieneCifrasFiscales` filtraba **por línea**: si la línea no contenía `itbis|retenci|porcentaj`, se la saltaba entera. Con ello quedaban fuera justo los campos que `DOC_TECNICA.md` llama cifras fiscales y afirma que no están en el código: `RangoDesde`, `RangoHasta`, `UmbralAlerta`, `FechaVencimiento` y `ToleranciaCambioUsd`/`ToleranciaCambioDop`. El defecto era del mecanismo, no del código: hoy no hay ninguna violación (los DTO los declaran `required` y sin valor por defecto), pero la guarda no lo estaba viendo.
- Cómo reproducir: (a) ejecutar `--filter "FullyQualifiedName~CobroSaldosPropertyTests"` y comprobar que la fórmula de `ComprobarInvariantes` no contiene ni `Excedente` ni `saldo_a_favor`; romper `AplicarSaldo` para que no sume el excedente y el test sigue verde. (b) poner `public required int RangoDesde { get; init; } = 5;` en `DTOs/ConfiguracionDtos.cs`: la línea no habla de ITBIS ni de retención ni de porcentaje y la guarda no dice nada.
- Causa raíz: las dos pruebas buscaban el término que se les ocurrió en el nombre (`itbis`, `porcentaje`, «saldo») en lugar del término del requisito (rango autorizado, umbral, vencimiento, tolerancia; crédito y sobrepago). Una invariante que solo se puede satisfacer en un caso no comprueba ese caso: comprueba que nadie se salió de él.
- Solución: (a) la invariante pasa a ser **tres igualdades que tienen que valer a la vez**: `Σ totales − Σ retenciones − Σ cobrado − Σ ajustes = Σ pendientes − Σ sobrepago` (deuda), `Σ importe recibido = Σ cobrado + crédito vivo en saldo_a_favor` (dinero) y `crédito vivo ≥ Σ sobrepago` (cruce), más el ajuste por diferencia de cambio de F3-07 leído de `ajuste_diferencia_cambio`. El generador mete ahora un cobro **mayor que toda la cartera** en uno de cada cuatro casos (50 de 200), por FIFO y sin asignaciones propias para que el `Math.Min` del servicio sea el que recorte; los cobros siguientes gastan con `usar_saldo_a_favor` el crédito que esos sobrepagos dejaron. Se comprueba además, **por cobro**, que `TotalAsignado + Excedente == ImporteRecibido + SaldoFavorUsado`, que el excedente no es negativo y que no se aplica más crédito del que había — ninguna de las tres se comprobaba antes. (b) la guarda amplía a una segunda comprobación, `LosCamposDeConfiguracionFiscalNoTienenValorPorDefecto`, sobre `Data/`, `DTOs/`, `Services/` y `Dominio/` de la API: caza el literal en el inicializador (`= 5m`, `= new DateOnly(...)`) y el campo numérico o de fecha sin `required`, que es un valor por defecto igual de invisible. Se conservan intactos la lista de permitidos y su comentario, y `backend/AtlasRent.Tests/` queda fuera por construcción (la raíz que se escande es el proyecto de la API, no la solución).
- Test que lo cubre: los propios dos (`CobroSaldosPropertyTests`, `CifrasFiscalesEnCodigoTests`). Los recuentos del final del bucle (`sobrepagos == 50`, `usado > 0`) son los que impiden que el generador vuelva a degenerar en silencio.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno; es infraestructura de pruebas.
- **Riesgo / hallazgo abierto:** al ampliar la guarda (b) ha saltado un defecto **de producto**, y no se ha silenciado: `DTOs/ConfiguracionDtos.cs` declara `NumeradorFiscalPeticion.UmbralAlerta` como `[Required] public int UmbralAlerta { get; init; }` **sin `required`**, mientras sus cinco hermanos (`TipoPrefijo`, `RangoDesde`, `RangoHasta`, `FechaVencimiento`, `SiguienteNumero`) sí lo llevan. `[Required]` de `DataAnnotations` no hace nada sobre un `int` no anulable en la deserialización, así que una petición que omita `umbralAlerta` se materializa con `0` y el rechazo llega como 409 de negocio («El umbral de aviso tiene que ser un número de números mayor que cero») en vez de un 400 de validación. No hay corrupción de datos —el servicio no guarda ese `0`—, pero el DTO contradice su propio comentario («no hay ninguno por defecto en el código») y el contrato es inconsistente. **No corregido aquí**: el encargo era la guarda, no el producto. También queda una limitación conocida de la regla: la lista de permitidos compartida acepta `0`, `1` y `1.00`, así que un `ToleranciaCambioUsd = 1.00m` no la trippingaría; no se ha tocado esa lista porque sus entradas están justificadas por los ordinales de enumeración de F3-04.

### BUG-009 · Una prueba de copia de seguridad fallaba según la hora a la que se ejecutaba
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026 (orquestador)
- Gravedad: **Media** · Estado: CORREGIDO · Módulo: pruebas / F1-12
- Detectado por: ejecución de las pruebas de F3-03, que unionsus filtros con las de copia de seguridad.
- Síntoma: `Backup.RotacionCopiaTests.DosEjecucionesDelMismoDiaSonIdempotentes` falla con «ya había copia de hoy y verifica», en cuanto el reloj real está entre las **18:00 y las 24:00 de Santo Domingo** (00:00–06:00 en hora de España). Pasó en la línea base del bloque F3, que se ejecutó a las 23:00 hora peninsular, y falla de forma determinista ahora.
- Cómo reproducir: ejecutar `--filter "FullyQualifiedName~RotacionCopiaTests"` entre las 18:00 y la medianoche de Santo Domingo.
- Causa raíz: la prueba crea `new RelojPrueba()`, que arranca en `DateTimeOffset.UtcNow`, ejecuta una copia y avanza seis horas para simular una segunda ejecución del mismo día. El día de negocio va por **salto de día**, no por reloj: si la primera ejecución cae después de las 18:00, las seis horas siguientes ya están en el día siguiente, y la copia se guarda en otro archivo. La prueba comprobaba la hora de ejecución, no la idempotencia.
- Solución: `RelojPrueba` admite un instante de partida y expone `RelojPrueba.InicioDelDia` (las 09:00 de Santo Domingo del día de hoy). Las pruebas de rotación y las de vigilancia de copia arrancan ahí. Con media mañana de arranque, avanzar horas nunca cruza medianoche. **El resto de los usos de `RelojPrueba` no se tocan**: avanzan días completos y no tienen el problema.
- Test que lo cubre: el mismo, que ahora pasa a cualquier hora del día.
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno; es infraestructura de pruebas.

### BUG-008 · Una alerta cerrada no volvía a levantarse nunca: el único de `alerta.clave` la bloqueaba para siempre
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: alertas / F1-12 + F2-09 + F3-03 (y F3-04, que iba a heredar el defecto)
- Detectado por: pruebas de F3-03 (`ComprobanteAlertasTests`), al numerador que se queda corto, se amplía y vuelve a quedarse corto sin volver a avisar.
- Síntoma: una condición se avisa, se resuelve y vuelve a darse: **no vuelve a aparecer ningún aviso**. La fila sigue en la base con `resuelta_at` puesto, el trabajo diario pasa por encima como si nada y el contador de repeticiones se queda quieto. En pantalla no hay nada rojo, porque no hay ninguna fila viva. Afectaba igual a las alertas de F2-09 (baja programada, liberación, conflicto de reserva, IP bloqueada), al fallo del tipo de cambio, a la copia de seguridad y a la vigilancia de la copia: **todas** las alertas del sistema.
- Cómo reproducir: numerador fiscal con `umbral_alerta` 10 y rango casi agotado → correr `TrabajoComprobantes` (aviso vivo) → ampliar el rango → volver a correr (aviso cerrado) → reducir el rango otra vez → volver a correr. La fila sigue cerrada, no hay ninguna viva con esa clave y `repeticiones` no sube. Equivalente con F2-09: programar una baja → anularla (el aviso se cierra) → volver a programarla → el aviso no se levanta.
- Causa raíz: **el único estaba en la columna equivocada.** `ix_alerta_clave` era `UNIQUE (clave)` sobre la tabla entera, así que una alerta cerrada seguía ocupando su clave. `ServicioAlertas.RegistrarAsync` hacía `INSERT … ON CONFLICT (clave) DO UPDATE … WHERE alerta.resuelta_at IS NULL`: al chocar contra una fila cerrada, ese `WHERE` hacía que el `UPDATE` no actualizara nada, y como el conflicto ya se había detectado **el `INSERT` tampoco llegaba a insertar**. El resultado era un no-op silencioso. Para que «una alerta que se cierra y vuelve a darse tiene que volver a verse» (§3.16, AGENTS.md §6) hacía falta que la restricción **no incluyera** a las alertas resueltas, y el único las incluía. El comentario del código y la prueba de F3-03 lo documentaban como «límite heredado», o sea, el defecto estaba escrito como si fuera una decisión.
- Solución: el único pasa a ser **parcial y sobre el par que identifica de verdad un fallo**: `UNIQUE (tipo, clave) WHERE resuelta_at IS NULL` (migración `20261016002328_AlertaVivaReabrible`, que sustituye a `ix_alerta_clave` y aplica con datos dentro sin tocar ninguno: como las claves ya eran todas distintas, los pares `(tipo, clave)` sin resolver también lo son). `RegistrarAsync` reabre **la misma fila** en dos pasos: un `UPDATE` que pone `resuelta_at = NULL` en la cerrada más reciente de esa `(tipo, clave)` —con `NOT EXISTS` de una viva, para no reabrir de más— y después el `INSERT … ON CONFLICT (tipo, clave) WHERE resuelta_at IS NULL DO UPDATE`, que es el único punto que suma una repetición. Así el contador sube exactamente una vez por aviso en los tres casos (no había fila, estaba viva, se ha reabierto). `ResolverAsync` no cambia: seguir poniendo `resuelta_at` es justamente lo que saca la fila del índice parcial.
- **Por qué reabre la fila y no crea otra:** la clave es la identidad del asunto —«este numerador se está quedando sin números»—, no el número de un aviso. Dos filas para el mismo asunto obligan a leerlas todas para saber si hay algo vivo y parten el contador de repeticiones en dos, que es justo el dato que dice «lleva tres días fallando». Y reabrir no pierde el rastro: la fila conserva que pasó, cuántas veces se ha visto y `ocurrido_at` pasa a apuntar a esta última vez. Con esto el histórico puede acabar con más de una fila cerrada por clave, que es correcto (son episodios distintos) y está escrito en la migración y en `ServicioAlertas`.
- Test que lo cubre: `backend/AtlasRent.Tests/Comprobantes/ComprobanteAlertasTests.cs` → `AlAmpliarElRangoElAvisoSeResuelveYAlVolverASeQuedarCortoSeReabre` (reescrita: antes fijaba el comportamiento viejo) y `backend/AtlasRent.Tests/Bandeja/AlertasSinDuplicarTests.cs` → `ElAvisoSeVuelveALevantarSiLaBajaVuelveADarse` (nuevo, el equivalente de F2-09).
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno que corregir. Las alertas ya cerradas en la base de desarrollo **vuelven a poder avisar** con esta migración, que es justo el efecto buscado: no hay que tocar filas ni reconstruir nada. Lo único que hay que tener en cuenta es el `Down` de la migración: volver al `UNIQUE (clave)` a secas fallaría si ya hay dos filas cerradas con la misma clave.

### BUG-007 · La tolerancia de diferencia de cambio tenía dos sitios donde se configurar y el servicio solo leía uno
- Fecha detectado: 16/10/2026 · Fecha corregido: 16/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: cobros / F3-07 (y configuración, de F1-04)
- Detectado por: agente de F3-07, leyendo `DTOs/ConfiguracionDtos.cs` al implementar §3.10.2.
- Síntoma: el administrador ajustaba la tolerancia en *Configuración → Ajustes generales* y el cambio **no tenía ningún efecto**: el servicio `ServicioCobros` usaba `OpcionesCobros` (sección `Cobros` de configuración / variables de entorno). Y al revés, se cambiaba `Cobros__ToleranciaUsd` y la pantalla seguía mostrando otra cosa.
- Cómo reproducir: `PUT /api/v1/config/ajustes` con `toleranciaCambioUsd = 0.25` y a continuación intentar un ajuste por diferencia de cambio de 0,30 USD: se aceptaba cuando debería rechazarse.
- Causa raíz: **dos fuentes para la misma cifra de negocio.** La tabla `ajuste_general` ya traía `tolerancia_cambio_usd` y `tolerancia_cambio_dop` desde `20261015042341_Configuracion`, nullable, con su DTO `AjusteGeneralPeticion` y su endpoint (F1-04). F3-07 creó una segunda porque su tarea pedía opciones de configuración, sin mirar que la primera ya existía. Con dos fuentes y ambas vacías, el mismo dato tenía tres respuestas posibles según quién mirara: la pantalla, el entorno o el valor por defecto del código.
- Solución: **manda `ajuste_general`.** `ServicioCobros` recibe `ServicioConfiguracion` y lee la tolerancia con `LeerAjustesAsync`, el mismo camino que usa la pantalla, así que hay una sola fuente y la que el administrador ve es la que aplica. Se borra `OpcionesCobros`, su registro en `Program.cs` y sus líneas en `config/variables-entorno.example`. Además, **sin tolerancia configurada el ajuste queda desactivado** con un 409 que dice dónde configurarlo: las columnas nacen a `NULL` a propósito (los importes de §3.10.2 son [Supuesto], no decisión) y un ajuste permitido sin que nadie haya escrito una cifra es una pérdida de dinero silenciosa.
- Test que lo cubre: `backend/AtlasRent.Tests/Cobros/CobroAjusteDiferenciaCambioTests.cs` → `SinToleranciaConfiguradaElAjusteEstaDesactivado` (409 sin tolerancia y sin fila creada, y el mismo ajuste entra al configurar la tolerancia por la API de configuración).
- Commit/PR: pendiente del orquestador.
- Datos afectados: ninguno. Las columnas están vacías en la base de desarrollo porque nadie había escrito esos ajustes, así que no hay tolerancias que corregir ni que migrar.

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
