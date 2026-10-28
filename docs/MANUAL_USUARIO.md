# Manual de usuario — Atlas Rent

> Describe cómo funciona la app según la especificación.
>
> **Estado a 15/10/2026.** Este manual describe la app **tal como se entregará**, no la que hay hoy:
> la parte de contratos, reservas y cotizaciones ya está construida y probada por API, pero **todavía
> no hay pantallas**. Antes de buscar un botón que aquí se describe, mira la marca del apartado:
>
> - ✅ **pantalla construida y probada**: se puede usar hoy en la app.
> - 🟡 **la regla existe y está probada por la API, pero no hay pantalla**: se puede usar por API, un
>   cliente o una integración. En la interfaz todavía no hay dónde hacerlo.
> - ⬜ **no construido todavía**: es de una fase posterior.
>
> La lista de pantallas pendientes está en `frontend/src/navegacion/menu.ts`, con un `TODO(A#)` al
> lado de cada módulo. Si algo no coincide con lo que ves, avisa al administrador.

## 1. Qué es
La herramienta donde se gestiona todo el alquiler: clientes, unidades, cotizaciones, reservas, contratos, facturas, cobros y fianzas. Sustituye a Factusol.

## 2. Entrar
1. Abre la dirección de la app en el navegador (funciona desde la oficina y desde fuera).
2. Escribe tu email y contraseña.
3. Si eres administrador, escribe también el código de 6 dígitos de tu app de autenticación.

Ten en cuenta:
- Tras **30 minutos** sin actividad se cierra la sesión; como máximo dura 12 horas.
- **5 intentos fallidos** bloquean tu cuenta 30 minutos. Muchos fallos desde la misma conexión bloquean esa conexión. Si te pasa, avisa al administrador.
- Si entras desde un sitio nuevo, el administrador recibe un aviso. Es normal.
- No compartas tu contraseña ni la guardes en ordenadores ajenos.
- La contraseña tiene **al menos 12 caracteres** y la app rechaza las que han aparecido en filtraciones públicas (se comprueba sin enviar tu contraseña a nadie). Puedes cambiarla cuando quieras desde tu cuenta; al cambiarla se cierran tus otras sesiones.
- Si el administrador **restablece** tu contraseña, te pedirá cambiarla en la siguiente entrada. Si desactiva tu usuario, tu sesión se cierra en la siguiente acción.

## 3. Qué puede hacer cada rol
| | Empleado | Gerente | Administrador |
|---|---|---|---|
| Clientes, unidades, reservas | Ver, crear, editar | Todo | Todo |
| Cotizaciones | Crear; enviar si tiene el permiso | Todo | Todo |
| Contratos | Preparar borradores y enviarlos a aprobación | Aprobar, modificar, dar de baja, liquidar | Todo |
| Facturas | Ver y preparar borradores | Emitir, modificar, anular, enviar | Todo |
| Cobros y fianzas | Ver | Registrar, corregir, devolver | Todo |
| Totales, informes, exportar | No | Sí | Sí |
| Recordatorios, envío masivo | No | Revisar y enviar; pausar por cliente | Todo, y elegir el modo en Configuración |
| Configuración, usuarios, desbloqueos | No | No | Sí |

Si no ves un botón, tu usuario no tiene ese permiso.

## 4. Clientes 🟡
- **Nuevo:** Clientes → *Nuevo*. Obligatorio: nombre, tipo (persona o empresa), documento y teléfono. El número de cliente se pone solo.
- **Contactos:** varios teléfonos y emails; marca cuál es para WhatsApp y cuál para email.
- **Prospecto:** alguien a quien solo has hecho una cotización. Pasa a cliente cuando reserva o firma.
- **Pausar recordatorios:** casilla en la ficha (gerente). **Frena los avisos automáticos y también**
  el envío manual de uno que esté pendiente.
- La ficha tiene pestañas: datos, contratos, reservas, facturas, cobros, fianzas, documentos y mensajes recibidos.

## 5. Unidades 🟡
- Un **inmueble** es el edificio o la villa; una **unidad** es lo que se alquila.
- *Ocupada / Reservada* se calcula sola. Tú solo marcas *Mantenimiento* o *Fuera de servicio*.
- **Calendario** ✅ (menú Operación → Calendario): elige el inmueble y el mes; cada fila es una unidad y cada casilla un día. Verde claro = libre, oscuro = contrato, azul = reserva (mensual **R**, corta **D**), media casilla azul = un medio día reservado (queda otro medio libre), ámbar = mantenimiento, gris con borde = fuera de servicio. Pulsa una casilla para ver qué hay ese día y abrir su contrato o reserva; en una casilla libre puedes crear la reserva con la unidad y el día ya puestos.

## 6. Cotizaciones ✅
> Pantalla construida; probada con la API simulada, todavía no contra el servidor real.

1. Cotizaciones → *Nueva cotización*. Cliente o prospecto, unidad (opcional), líneas y moneda. El total que ves mientras escribes es una **vista previa** (cada línea se redondea a 2 decimales); el importe oficial lo calcula el servidor al guardar.
2. *Marcar como enviada* (si tienes permiso) → pasa a **Enviada**. Una cotización enviada ya no se edita: se hace otra.
3. Marca **Aceptada** o **Rechazada**. Desde una aceptada, *Crear reserva* o *Crear contrato* abren el formulario con los datos copiados; no se crea nada hasta que lo confirmas.
4. *Descargar PDF* genera el documento. Solo el borrador se puede editar o dar de baja.

## 7. Reservas ✅
> Pantalla construida; probada con la API simulada, todavía no contra el servidor real.

**Mensual:** cliente, unidad, fecha de entrada prevista. Bloquea la unidad. Si en 7 días no hay contrato, sale un aviso. Al activar el contrato de esa reserva, la reserva pasa sola a *Convertida*; si llega la fecha de entrada y el contrato sigue sin activar (o no existe), la reserva pasa a *En conflicto*, sigue bloqueando la unidad y sale un aviso.

**Corta:** por cada día eliges **Medio día** o **Día completo** (puedes escribir una hora orientativa en texto libre, por ejemplo «mañana» o «a partir de las 3»; es opcional y no bloquea nada: lo que cuenta es medio día o día completo). En un mismo día caben dos medios días o un día completo.

**Anticipo:** si el cliente adelantó dinero, anótalo en la reserva (importe y una nota con lo acordado). **Es solo una nota, no un cobro, y no aparece en ningún saldo ni informe económico.** El cobro se registrará en la fase de finanzas; todavía no existe, y tampoco el aviso que te lo recordará.

**Si la unidad no está libre**, la reserva no se guarda y la pantalla lo explica: hay un contrato, otra reserva, o el día ya está completo. Tampoco se puede reservar una unidad en mantenimiento o fuera de servicio, ni cambiar o cancelar una reserva que tiene una **factura vigente** (hay que anular antes la factura).

**En conflicto:** la reserva sigue bloqueando la unidad hasta que el gerente decida; la salida es cancelarla y buscar otra unidad al cliente. **Facturar:** la reserva corta se factura desde su ficha (*Facturar reserva*, gerente o administrador); la mensual se factura por su contrato.

## 8. Contratos ✅
> Pantalla construida; probada con la API simulada, todavía no contra el servidor real.

1. Desde la reserva o Contratos → *Nuevo*. Renta, moneda, día de pago, fianza y renovación.
2. *Enviar a aprobación*. El gerente lo aprueba o lo rechaza con comentario obligatorio (el contrato vuelve a borrador). El empleado ve el contrato pero no los botones de aprobar, rechazar, activar ni dar de baja.
3. La ficha muestra el **flujo**: borrador → solicitud de aprobación → aprobado → activo → baja programada → finalizado → liquidado. Pestañas: resumen, historial (versiones y solicitudes), incrementos e inventario y salida.
- **Cambios en un contrato activo** (*Registrar cambio*): piden motivo y generan una versión nueva; la moneda no cambia y la renta nueva solo afecta a las facturas futuras.
- **Incrementos:** porcentaje o importe fijo, con la fecha desde la que rigen.
- **Inventario:** la copia se congela al activar. Para la salida, *Revisar la salida* pide el estado de cada elemento (OK, faltante o dañado), una nota en lo que falta, y el importe propuesto; se firma entera y una sola vez. No es un cobro.
- **Plantillas y anexos** (botón en Contratos): texto de los contratos y catálogo de anexos; crear y editar con permiso de redacción, desactivar solo el gerente o administrador. *Descargar PDF* genera el contrato.
- Se renueva solo hasta que se cancele. Las subidas de renta programadas se aplican solas.
- **Baja:** el gerente pone la fecha de fin, **en cualquier fecha** (hoy o posterior), aunque sea antes de que acabe el periodo en curso. La fecha de baja ya no se factura: si la baja es el día 15, se factura hasta el 14, prorrateando ese último mes. En esa fecha el contrato **se finalizará solo** y la unidad queda libre. Después hay que **liquidar**: se revisa el inventario de salida y se decide qué pasa con la fianza. **Los cobros y los saldos todavía no existen** (fase de finanzas), así que por ahora la liquidación deja constancia de qué falta y de la decisión, pero no mueve dinero.
- **Finalizar** solo se puede cuando llega la fecha de baja; antes el botón está desactivado y dice desde cuándo. **Liquidar** no se deja si queda fianza cobrada sin resolver o facturas con importe pendiente: la pantalla dice qué falta y se resuelve a mano (fianzas y cobros) antes de volver a liquidar.

## 9. Facturas ⬜
- **Del mes:** el gerente pulsa *Generar facturas periódicas*, revisa la simulación y confirma.
- **Manual:** *Nueva factura* desde la unidad o el cliente. Queda en **borrador** hasta que el gerente la emite.
- **Emitir** pone el número definitivo y, en la serie 3, el **número de comprobante fiscal** automáticamente. Si quedan pocos comprobantes, la app avisa; si se acaban o vencen, no deja emitir hasta cargar un rango nuevo.
- **Moneda:** cada factura va en una moneda y no cambia. Si la necesitas en la otra moneda, hay que anularla y hacer otra.
- **E-factura:** no se hace aquí. Si se hizo en la otra aplicación, puedes teclear su número en *Referencia e-factura*.
- **Mora:** añádela como una línea más.
- **Proforma:** presupuesto que no genera deuda; con un botón se convierte en factura con los mismos importes.
- **Duplicar:** crea un borrador igual, sin número ni cobros.
- **Anular** (gerente): pide motivo. **Los cobros de esa factura también se anulan.** Si un cobro pagaba varias facturas, solo se anula la parte de esta. La pantalla te muestra qué cobros se verán afectados antes de confirmar. El dinero de la parte anulada queda marcado como **devuelto** en el cobro (no se convierte en saldo a favor). Si el cobro ya financió otro cobro, la app no deja anular y te dice cuál hay que anular antes.

## 10. Cuentas y cobros (pantalla principal) ⬜

Es la pantalla que sustituye a Factusol: **una sola tabla con todo lo que se debe y lo que se ha
pagado**, y desde ahí se cobra. Todavía no hay pantalla; lo que hay es la API que la alimentará.

### Qué se ve

Una fila por documento, con una **insignia de tipo**: `Factura`, `Proforma` o `Fianza`. Las fianzas
salen en la misma tabla que las facturas porque son dinero: quien cobra necesita ver la deuda y la
fianza del mismo contrato sin cambiar de pantalla.

Columnas:

| Columna | Qué es |
|---|---|
| Serie y número | La serie y el número del documento. Las fianzas no tienen. |
| Comprobante | Tipo y número del comprobante fiscal, si la serie genera comprobantes. |
| Fecha | De emisión. En una fianza, la del día en que se dio de alta. |
| Cliente, unidad | De quién es y en qué unidad. En una fianza, los de su contrato. |
| Concepto | El que lleva el documento. En una fianza, cuántas fianzas son. |
| Moneda, total | En dólares o en pesos. En una fianza, el total es **lo exigido**. |
| Retención | Lo retenido, si el cliente es agente de retención. |
| Cobrado | **Dinero real cobrado.** No incluye la retención ni los ajustes por cambio. |
| Pendiente | En una factura, lo que falta por cobrar. En una fianza, **lo que falta por exigir**. |
| Saldo a devolver | Solo en fianzas: lo que se le tiene que devolver al cliente. |
| Vencimiento, días vencida, vencida | Cuándo se debía y cuántos días lleva de retraso. Las fianzas no vencen. |
| Estado | *Pendiente*, *Parcial*, *Pagada* o *Anulada* en facturas; el estado de la fianza en fianzas. |
| Estado de envío | **Vacío todavía**: se llena cuando existan los envíos (F4-01). |

- **Lo que no aplica en una fila sale vacío, nunca en cero.** Una proforma no tiene cobrado ni
  pendiente porque no es una deuda; que pone «0» se leería como una deuda que no existe.
- **En una fianza, «pendiente» y «saldo a devolver» son dos cosas distintas.** El pendiente es lo que
  aún no se le ha exigido al cliente y el saldo es lo que hay que devolverle. Una fianza recién dada de
  alta tiene pendiente > 0 y saldo 0, y se ven las dos columnas para que nadie las confunda.
- Las **proformas no salen** salvo que filtres por ellas: son presupuestos, no cuentas.

### Filtros

Año, serie, moneda, estado del documento, estado de cobro, cliente, unidad, **vencidas** (lo que tiene
pendiente y su vencimiento ya pasó), **modificadas** (tocadas después de emitirse), y los interruptores
de incluir proformas y fianzas.

- **«No enviadas» y «envío fallido» todavía no están**: dependen de saber si un documento se envió y con
  qué resultado, que es lo que trae el bloque de envíos (F4-01). Cuando llegue, se añaden aquí.
- Un filtro que una fianza no puede tener (serie, estado, vencidas, modificadas) **deja las fianzas
  fuera** en vez de mostrarlas sin filtrar.
- El orden es por fecha y se puede invertir. Es el único criterio de orden: el pendiente y los días de
  mora son cuentas, no columnas, así que no se puede ordenar por ellos.
- El estado del filtro vive en la barra de direcciones: se puede compartir un enlace con la vista ya
  filtrada.

### Totales al pie

**Solo el gerente y el administrador los ven**, y **por moneda**: una línea por moneda, con el total, lo
retenido, lo cobrado y lo pendiente de **todo** lo que hay filtrado, no solo de la página que se está
viendo. **Nunca se convierten entre monedas**: consolidar en dólares es cosa de los informes.

El empleado ve la misma lista de filas, **pero sin el pie de totales**: no recibe la cifra, y no la
recibe como «todo cero», que también es una cifra.

### Registrar un cobro

1. Marca una o varias filas de facturas.
2. Se abre el panel lateral con la propuesta: **si has marcado filas, la propuesta es para esas y no
   propone ninguna otra**; si no has marcado nada, es la FIFO de siempre (la deuda más antigua primero).
3. Escribes el importe recibido, la moneda, la forma de pago y la cuenta.
4. *Registrar*. Puedes cambiar la propuesta antes de confirmar; la pantalla de cobro sigue teniendo lo
   de siempre.

Al marcar filas que superan el importe del cobro, la propuesta recorta la última marcada y lo que sobra
queda como excedente o saldo a favor, como en cualquier otro cobro.

### Lo que se puede hacer desde una fila

Ver, duplicar, modificar (si no tiene comprobante), anular, el PDF y el envío. **Cada acción es la de su
propio documento**: la vista no modifica nada por su cuenta, solo lleva a la pantalla que sí lo hace.

## 11. Fianzas ⬜
En la misma tabla de cobros. Al terminar el contrato, el gerente la devuelve entera o en parte; si descuenta algo, indica motivo e importe.

## 12. Enviar documentos (gerente y administrador) ⬜
- *Enviar* en la factura, proforma o recibo: email (o WhatsApp cuando esté activo) y contactos.
- El estado (enviado, entregado, leído, fallido) se ve en la tabla.
- **Envío masivo:** muestra un resumen antes de enviar.
- **Recordatorios:** según configuración, salen solos o el gerente revisa la lista del día y los envía.
  Tienes tres modos en *Configuración → Ajustes* (**administrador**):
  - **Apagado**: no sale ninguno.
  - **Manual**: cada mañana a las 9:00 se prepara la lista de avisos y **no sale nada** hasta que pulses *Enviar* en cada uno.
  - **Automático**: se preparan y se envían solos a las 9:00.

  Los dos plazos de la misma pantalla son **cuántos días desde el vencimiento sale el primer aviso**
  (5) y **cada cuántos días se repite mientras siga debiendo** (15). Con los valores de fábrica saldrían
  los días 5, 20 y 35; a los 6 ni al 21 no se repite, a propósito. **Si los dejas vacíos no sale ningún
  aviso y la app te avisa de que hay que configurarlos**: es preferible callar a mandar recordatorios con
  plazos que nadie ha decidido.

  Cada aviso es **uno por cliente y unidad** (aunque tenga varias facturas vencidas), va en texto corto
  y **con el estado de cuenta adjunto**. Con pago parcial menciona **solo lo que falta por pagar**, no la
  factura entera.

  **Se detienen al pagar:** si alguien paga entre que se preparó el aviso y que pulsas *Enviar*, no sale y
  queda en la lista como *omitido* con el motivo. También se omite si el cliente tiene la pausa puesta.

- **Pausar los avisos de un cliente:** casilla en su ficha (gerente). **También frena el envío manual**
  de un aviso pendiente: mientras esté marcada, a ese cliente no se le avisa por ningún camino.
- **Recordatorios por WhatsApp:** todavía no. Depende de decidir con qué proveedor se manda, y eso está
  pendiente. Hoy los recordatorios son **solo por correo**.

## 13. Avisos y bandeja 🟡
La campana muestra vencidas, contratos por vencer o pendientes de aprobar, fianzas pendientes, comprobantes por agotarse, envíos fallidos y avisos de seguridad (administrador).

## 14. Informes (gerente y administrador) ✅
> Construida en F5-01. **Pendiente:** nadie la ha abierto todavía en un navegador, así que su aspecto en
> tema claro y oscuro está sin revisar (lo está el código, no la pantalla).

En **Informes** tienes los bloques de §3.15: proformado, facturado, cobrado, retenido, pendiente, vencido,
fianzas retenidas y diferencias de cambio. Cada bloque sale en **USD y DOP por separado** y con el
**consolidado en dólares**.

- **Filtros:** desde, hasta, moneda y estado. Por cliente, unidad, contrato, inmueble, serie o empresa no
  hay desplegable todavía: si vienes desde otra pantalla con un filtro en la dirección, se aplica; si no,
  el informe va de todo.
- **Lo de arriba son los indicadores del día:** unidades disponibles, reservadas, ocupadas y en
  mantenimiento, y los contratos activos. Son un **corte de toda la empresa**, no del filtro: cuentan lo que
  hay en la casa ese día. Los bloques de dinero de abajo sí respetan los filtros.
- **Gráfico:** facturado y cobrado por mes, ya en dólares.
- **Exportar:** los botones de Excel y CSV descargan el detalle de facturas, proformas y fianzas con los
  filtros que estás viendo. El Excel lleva además una hoja con el resumen.
- **Avisos que pueden salir:** si hay documentos en pesos sin tipo de cambio congelado, el consolidado en
  dólares **no los incluye** y la pantalla lo dice. Si has puesto filtro de fechas, el saldo a favor no
  suma al total de cobrado, porque es una foto de hoy y no un movimiento del periodo.

**Ojo con los dos importes:** el consolidado usa el tipo de cambio que tenía cada documento cuando se
emitió, no el de hoy. Por eso un informe del mes pasado no cambia al verlo mañana, y por eso dos facturas
iguales en meses distintos pueden consolidar distinto.

## 15. Asistente (gerente y administrador) ✅
> Construido en F6-03, con el aviso de privacidad de F6-04. **Pendiente:** nadie lo ha abierto
> todavía en un navegador, así que su aspecto en tema claro y oscuro está sin revisar.

Escribes una pregunta, eliges **sobre qué** quieres la respuesta (clientes, cuentas, facturas,
contratos o informes) y la IA contesta sobre esos datos. No hay conversación guardada: cada
pregunta va sola y no queda historial en el navegador.

**Tus datos personales no salen**, pero tu pregunta sí:

- Los **nombres, teléfonos y documentos** que carga el sistema se sustituyen por seudónimos
  (`[[cliente_1]]`) antes de enviarse, y al volver la respuesta se vuelve a poner el nombre real.
- **Lo que escribas en la pregunta va tal cual** al proveedor de IA. Si escribes «¿cuánto debe
  Juan Pérez?», ese nombre viaja. La pantalla te avisa de esto antes de la primera consulta.
- El aviso es permanente, no sale solo la primera vez.

Lo que **no** hace, para que nadie espere otra cosa: **no toca datos**. Ni cobra, ni emite facturas,
ni crea contratos. Solo lee y responde. Lo único que escribe es una **propuesta** (por ejemplo,
enviar un recordatorio de saldo), y eso tiene que **aprobarlo una persona** con permiso; si entre
que se propone y se aprueba el saldo del cliente cambia, la propuesta caduca y no se envía nada.

Cada respuesta dice al pie qué proveedor y qué modelo han contestado. Si no hay IA configurada en la
instalación, la pantalla lo dice y no hay que enviar nada para enterarse.

## 16. Preguntas frecuentes
- **Me equivoqué en una factura emitida.** El gerente la modifica (si no tiene comprobante) o la anula y emite otra.
- **No veo un botón.** Tu usuario no tiene ese permiso.
- **El cliente dice que pagó y no aparece.** Solo el gerente registra cobros; pásale el justificante.
- **No puedo entrar.** Espera 30 minutos o pide al administrador que te desbloquee.
