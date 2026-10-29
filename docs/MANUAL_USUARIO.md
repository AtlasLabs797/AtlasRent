# Manual de usuario — Atlas Rent

> Describe cómo funciona la app según la especificación.
>
> **Estado a 29/10/2026.** Todas las pantallas de este manual existen en la app. Las marcas de cada apartado:
>
> - ✅ **pantalla disponible**: se puede usar en la app.
>
> Si algo de este manual no aparece en tu pantalla, lo más probable es que tu usuario no tenga ese permiso
> (si no ves un botón, mira el apartado 3). Si no es eso, avisa al administrador.

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
| Cobros y fianzas | Ver | Registrar, corregir, devolver, dar de alta fianzas | Todo |
| Totales, informes, exportar | No | Sí | Sí |
| Auditoría (solo lectura) | No | Sí | Sí |
| Recordatorios, envío masivo | No | Revisar y enviar; pausar por cliente | Todo, y elegir el modo en Configuración |
| Configuración, usuarios, bloqueos | No | No | Sí |

Si no ves un botón, tu usuario no tiene ese permiso.

**Novedad para gerentes:** al cobrar, emitir facturas y generar la facturación periódica, ya puedes elegir desde la app la serie, la forma de pago y la cuenta receptora. Antes había que pedírselo al administrador. Y también puedes rehacer una factura en otra moneda.

## 4. Clientes ✅
- **Nuevo:** Clientes → *Nuevo cliente*. Obligatorio: nombre, tipo (persona o empresa), documento y teléfono. El número de cliente se pone solo.
- **Contactos:** varios teléfonos y emails; marca cuál es para WhatsApp y cuál para email.
- **Prospecto:** alguien a quien solo has hecho una cotización. Pasa a cliente cuando reserva o firma.
- **Convertir un prospecto en cliente:** en Clientes → *Prospectos*, abre el prospecto y pulsa *Convertir en cliente*. Se abre el alta con sus datos: revísalos y guarda. Al guardar, el prospecto queda convertido y **sus cotizaciones y reservas pasan al cliente nuevo**. Un prospecto convertido no se puede volver a convertir: la pantalla muestra *Ver cliente*. Si el documento ya pertenece a otro cliente, la app no convierte el prospecto y te avisa.
- **Pausar recordatorios:** casilla en la ficha (gerente). **Frena los avisos automáticos y también**
  el envío manual de uno que esté pendiente.
- **Buscar:** por nombre o número en la lista. Para buscar por documento usa su propio campo: el documento no queda en la dirección ni en el historial del navegador.
- **Buscar un cliente al elegirlo:** en los campos de cliente (por ejemplo en Facturas, Cuentas y cobros), escribe su nombre o número y elige de la lista que aparece.
- **Desactivar:** un cliente con facturas no se borra, se desactiva. El número de cliente solo lo puede fijar la administración.
- **Reactivar:** un cliente o un prospecto desactivado se vuelve a activar con el botón *Reactivar* de su ficha. Para verlos, marca *Mostrar inactivos* en la lista.
- La ficha tiene pestañas: datos, contratos, reservas, facturas, cobros, fianzas, documentos y mensajes recibidos.

## 5. Unidades ✅
- Un **inmueble** es el edificio o la villa; una **unidad** es lo que se alquila.
- *Ocupada / Reservada* se calcula sola. Tú solo marcas *Mantenimiento* o *Fuera de servicio*.
- **Pantalla:** Unidades e inmuebles → entra en un inmueble para ver sus unidades, cambiar el estado manual, las modalidades y las tarifas.
- **Desactivar una unidad:** en la ficha del inmueble, botón *Desactivar unidad*. Deja de ofrecerse para contratos y reservas nuevos. **No se puede** si tiene un contrato o una reserva vivos: primero hay que terminarlos o cancelarlos.
- **Reactivar:** una unidad desactivada tiene el botón *Reactivar unidad*; un inmueble desactivado, el botón *Reactivar* de su ficha.
- **Desactivar un inmueble:** botón *Desactivar* de su ficha. **No se puede** si tiene contratos o reservas vivos.
- **Documentos de propiedad (gerente y administrador):** pestaña *Documentos* del inmueble. Solo PDF de hasta 25 MB; cada subida de la misma categoría crea una versión. El empleado no ve esta pestaña.
- **Calendario** ✅ (menú Operación → Calendario): elige el inmueble y el mes; cada fila es una unidad y cada casilla un día. Busca una unidad concreta con el campo *Buscar unidad*.
  - **Verde claro = libre.**
  - **Oscuro = contrato** (letra **C**).
  - **Azul = reserva**: mensual (letra **R**) o corta (letra **D**).
  - **Media casilla azul y media verde = un medio día reservado**; queda otro medio día libre. No lleva letra.
  - **Ámbar = mantenimiento** (letra **M**).
  - **Gris con borde = fuera de servicio** (letra **–**).

  Pulsa una casilla para ver qué hay ese día y abrir su contrato o reserva; en una casilla libre puedes crear la reserva con la unidad y el día ya puestos.

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

**Anticipo:** si el cliente adelantó dinero, anótalo en la reserva (importe y una nota con lo acordado). **Es solo una nota, no un cobro, y no aparece en ningún saldo ni informe económico.** 
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
- **Baja:** el gerente pone la fecha de fin, **en cualquier fecha** (hoy o posterior), aunque sea antes de que acabe el periodo en curso. La fecha de baja ya no se factura: si la baja es el día 15, se factura hasta el 14, prorrateando ese último mes. En esa fecha el contrato **se finalizará solo** y la unidad queda libre. Después hay que **liquidar**: se revisa el inventario de salida y se decide qué pasa con la fianza.
- **Finalizar** solo se puede cuando llega la fecha de baja; antes el botón está desactivado y dice desde cuándo. **Liquidar** no se deja si queda fianza cobrada sin resolver o facturas con importe pendiente: la pantalla dice qué falta y se resuelve a mano (fianzas y cobros) antes de volver a liquidar.

## 9. Facturas ✅
**Qué hay en pantalla:** *Facturas* (listado con filtros y ficha), *Nueva factura* para crear borradores, *Facturación periódica* (simular y generar), editar un borrador, emitir, modificar líneas, anular con motivo, duplicar, convertir una proforma, rehacer en otra moneda, cerrar una diferencia de cambio, la referencia de e-factura, el PDF y el registro del comprobante de retención.
- **Del mes:** el gerente pulsa *Generar facturas periódicas*, revisa la simulación y confirma. En la simulación cada fila muestra el cliente, la unidad (inmueble · unidad) y la fecha desde la que empieza el contrato.
- **Manual:** *Nueva factura* desde la unidad o el cliente. Queda en **borrador** hasta que el gerente la emite.
- **Crear un borrador:** Facturas → *Nueva factura*. Rellena la cabecera (cliente, inmueble, unidad opcional, serie, moneda, vencimiento y concepto) y pulsa *Crear borrador*. Después añade las líneas con *Añadir línea*: tipo, cantidad, precio unitario, ITBIS y retención.
- **Editar un borrador:** en la ficha, *Editar borrador*. Puedes cambiar la cabecera (*Guardar cabecera*) y las líneas (*Editar*, *Quitar*). Solo los borradores se pueden editar.
- **Totales:** lo que ves mientras escribes es una vista previa. Los totales que valen son los que calcula el sistema al guardar. Si tu usuario no puede ver los tipos de ITBIS y de retención, las líneas se guardan sin impuesto: pide a la administración que los asigne.
- **Emitir** pone el número definitivo y, en la serie 3, el **número de comprobante fiscal** automáticamente. Si quedan pocos comprobantes, la app avisa; si se acaban o vencen, no deja emitir hasta cargar un rango nuevo.
- **Modificada tras envío:** si una factura se cambia después de enviarla, la ficha y la tabla de Cuentas lo marcan con esa etiqueta.
- **Retención:** si el cliente es agente de retención, la factura muestra *Retención sin comprobante* hasta que se recibe su comprobante. En la ficha, *Registrar recibido* pide el número y la fecha de recepción tal como figuran en el documento del cliente. Solo lo puede registrar el gerente o el administrador, y una vez registrado no se puede cambiar.
- **Moneda:** cada factura va en una moneda y no cambia. Si la necesitas en la otra moneda, hay que anularla y hacer otra.
- **E-factura:** no se hace aquí. Si se hizo en la otra aplicación, puedes teclear su número en *Referencia e-factura*.
- **Mora:** añádela como una línea más.
- **Proforma:** presupuesto que no genera deuda; con un botón se convierte en factura con los mismos importes.
- **Duplicar:** crea un borrador igual, sin número ni cobros.
- **Anular** (gerente): pide motivo. **Los cobros de esa factura también se anulan.** Si un cobro pagaba varias facturas, solo se anula la parte de esta. La pantalla te muestra qué cobros se verán afectados antes de confirmar. El dinero de la parte anulada queda marcado como **devuelto** en el cobro («Devuelto al anular facturas» en su ficha; no se convierte en saldo a favor). Si el cobro ya financió otro cobro, la app no deja anular y te dice cuál hay que anular antes.

## 10. Cuentas y cobros (pantalla principal) ✅
**Cómo se cobra:** marca una o varias filas del mismo cliente y contrato y pulsa *Registrar cobro*. En el panel eliges fecha, moneda e importe recibidos, forma de pago y cuenta. Si pagan en otra moneda, la app propone el cambio del día y puedes cambiarlo; verás la conversión aproximada. Pulsa *Simular reparto*, corrige el importe de cada línea si hace falta, vuelve a simular y *Registrar cobro*. Si cambias algo después de simular, hay que simular otra vez. El empleado ve la tabla pero no los totales ni el botón de cobrar. Los filtros quedan en la dirección de la página.

Es la pantalla que sustituye a Factusol: **una sola tabla con todo lo que se debe y lo que se ha
pagado**, y desde ahí se cobra.
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
| Envío | El estado del último envío del documento (Enviado, Entregado, Leído, Fallido…). «—» si no se ha enviado. |

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

- **Filtros de envío:** *No enviadas*, *Envío fallido* y *Modificadas tras envío* se basan en el estado de envío de cada documento. Si el documento no tiene envíos, sale en *No enviadas*.
- Un filtro que una fianza no puede tener (serie, estado, vencidas, modificadas) **deja las fianzas
  fuera** en vez de mostrarlas sin filtrar.
- El orden es por fecha y se puede invertir. Es el único criterio de orden: el pendiente y los días de
  mora son cuentas, no columnas, así que no se puede ordenar por ellos.
- El estado del filtro vive en la barra de direcciones: se puede compartir un enlace con la vista ya
  filtrada.
- **Ver las cuentas de un contrato:** desde la ficha de una factura o de una fianza, *Ver cuentas del contrato* abre esta pantalla filtrada por ese contrato. *Ver todos los contratos* quita el filtro.

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

## 11. Fianzas ✅
**Qué hay en pantalla:** listado y ficha con el resumen, las devoluciones, *Retener*, *Pasar a liquidación* y *Devolver fianza* (el motivo es obligatorio si descuentas algo o no la dejas a cero).
- **Dar de alta una fianza** (gerente y administrador): *Nueva fianza*. Busca el contrato por cliente o unidad e indica el importe exigido, la moneda y el número de fianzas. La fianza se crea sin cobro: el cobro se registra después en *Cuentas y cobros*.
- **Cobrar una fianza:** la ficha te manda a *Cuentas y cobros*, donde se marca y se registra el cobro.
- **Ver las cuentas de un contrato:** *Ver cuentas del contrato* en la ficha.

En la misma tabla de cobros. Al terminar el contrato, el gerente la devuelve entera o en parte; si descuenta algo, indica motivo e importe.

## 12. Enviar documentos (gerente y administrador) ✅
- *Enviar* en la factura, proforma o recibo: email (o WhatsApp cuando esté activo) y contactos.
- El estado (enviado, entregado, leído, fallido) se ve en la tabla.
- **Reintentar:** en *Envíos* → *Historial*, un envío fallido tiene el botón *Reintentar*; el correo se vuelve a mandar. Si otra persona ya lo reintentó, la lista se recarga y lo dice.
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

## 13. Avisos y bandeja ✅
La campana muestra vencidas, contratos por vencer o pendientes de aprobar, fianzas pendientes, comprobantes por agotarse, envíos fallidos y avisos de seguridad (administrador). Para revisar y cerrar cada aviso, mira la pestaña *Alertas* de la Bandeja (ver más abajo).

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

## 15 bis. Administración (solo administrador) ✅

Entradas del menú bajo **Control**. Gerente y empleado no las ven (la auditoría, en el apartado siguiente, sí la ve el gerente).

**Usuarios.** Lista de personas con su rol y estado. «Nuevo usuario» pide nombre de acceso, nombre, email (opcional), contraseña de 12 a 72 caracteres y rol. Al pulsar una fila se abre el detalle:
- **Rol:** el rol marca el techo de lo que la persona puede hacer.
- **Permisos:** el editor solo ofrece lo que permite el rol. Puedes **apagar** los que esa persona no debe tener; no se puede encender nada que su rol no traiga. A un administrador no se le puede quitar el segundo factor obligatorio ni el acceso de emergencia.
- **Restablecer contraseña:** la persona tendrá que cambiarla al entrar y se cierran sus sesiones. Comunícasela por un canal seguro.
- **Desactivar usuario:** corta sus sesiones al instante. No puedes desactivar tu propia cuenta ni quitar el rol al último administrador; la pantalla te lo dice con el motivo.

**Bloqueos y acceso de emergencia.** Pestañas según tu permiso:
- **Cuentas bloqueadas:** lista con usuario, nombre y las fechas desde y hasta. *Desbloquear* deja que la persona vuelva a intentar entrar.
- **IPs bloqueadas:** lista con nivel, fallos y fechas. *Desbloquear* en la fila. Si la IP no sale en la lista, escríbela en *Desbloquear una IP a mano* y pulsa *Desbloquear IP*; si no estaba bloqueada, la app lo dice.
- **Lista blanca:** las IP que nunca se bloquean. Es de solo lectura: se cambia en la configuración del servidor, no desde la app.
- **Acceso de emergencia:** para un administrador que perdió su dispositivo y sus códigos. Lo pide otro administrador con un motivo de 10 a 500 caracteres. Después, **otro administrador distinto** lo aprueba: en *Solicitudes* (filtro *Estado*) pulsa *Aprobar*, o pega el identificador en *Aprobar una solicitud*. Tú no puedes aprobar tu propia solicitud. Al aprobar sale un código de un solo uso: entrégalo por un canal seguro, no se vuelve a mostrar.

**Configuración.** Pestañas:
- **Series:** número, prefijo, moneda, tipo y condiciones de pago. Si la serie ya emitió números, no se puede cambiar su moneda, prefijo ni tipo: crea una serie nueva. «Dar de baja» la deja de ofrecer en documentos nuevos; lo emitido se conserva.
- **Comprobantes:** el rango autorizado, su vencimiento y el siguiente número. Avisa cuando un rango está agotado, vencido, por agotarse o falta por configurar: sin rango no se puede emitir.
- **Impuestos, Cobros y Subempresas:** ITBIS, retenciones, formas de pago, cuentas receptoras y subempresas. Los porcentajes los pones tú; la aplicación no trae ninguno.
- **Tipo de cambio:** histórico y carga a mano cuando la fuente automática no trajo el valor de un día (hasta 6 decimales). Solo sirve para informes.
- **Ajustes:** tolerancias de cambio, modo y días de los recordatorios, y si el segundo factor es obligatorio también para gerentes.
- **Mensajes:** plantillas de correo por tipo e idioma, en texto plano.
- **Agentes de IA:** tokens para que un agente consulte datos. Solo admiten lectura o propuestas. **El token se muestra una sola vez** al emitirlo: cópialo entonces. «Revocar» lo invalida al instante.

## 15 ter. Auditoría (gerente y administrador) ✅
Registro de lo que se ha hecho en la app. Es **solo de lectura**: desde aquí no se cambia ni se borra nada.
- **Filtros:** desde, hasta, usuario (escribe su identificador, no su nombre), entidad y acción. *Filtrar* aplica los filtros y *Limpiar* los quita.
- **Tabla:** fecha, usuario, acción, entidad, resultado, IP y detalle. «Sistema» indica que el evento lo hizo la propia aplicación.
- **Detalle:** *Ver* abre los datos del evento. Los datos sensibles salen ocultos con `***`. Si el evento no guarda datos, lo dice.

## 16. Preguntas frecuentes
- **Me equivoqué en una factura emitida.** El gerente la modifica (si no tiene comprobante) o la anula y emite otra.
- **No veo un botón.** Tu usuario no tiene ese permiso.
- **El cliente dice que pagó y no aparece.** Solo el gerente registra cobros; pásale el justificante.
- **No puedo entrar.** Espera 30 minutos o pide al administrador que te desbloquee.

## Bandeja, envíos y panel de inicio

**Panel de inicio.** Al entrar ves cuatro indicadores (facturado, cobrado, vencido y unidades ocupadas), cada uno con su comparación. Si tu usuario no puede ver totales, en su lugar aparece "Solo gerencia". Debajo tienes las alertas abiertas más recientes (hasta cinco), con *Ver todas* para ir a la Bandeja, lo que está pendiente de decidir y accesos directos a lo que puedes usar.

**Bandeja** (gerente y administrador). Tiene tres pestañas:
- Contratos: los contratos enviados a aprobación, con la renta vigente y la propuesta. Puedes aprobar o rechazar; rechazar pide un motivo. Si el aviso dice que la solicitud cambió, alguien la decidió antes o el contrato se modificó: vuelve a revisarla.
- Propuestas de la IA: lo que el asistente propone, con su justificación y la evidencia. El asistente solo propone; no se manda nada hasta que apruebas. Si el saldo cambió, la propuesta caduca y se te explica el motivo.
- Alertas: lo que necesita revisión (contratos, reservas, facturas, comprobantes fiscales, tipo de cambio, fianzas, recordatorios, envíos, seguridad, copias de seguridad y auditoría). Filtra por *Tipo* y por *Estado* (*Abiertas*, *Cerradas* o *Todas*). Cada alerta muestra cuántas veces ha salido. *Cerrar* la quita de abiertas; si el problema sigue, **vuelve a abrirse sola**. Cada persona ve solo los tipos que le corresponden: el empleado ve las de contratos y reservas.

**Envíos.**
- Historial: cada correo con su estado (Pendiente, Enviando, Enviado, Entregado, Leído o Fallido). Puedes descargar la copia exacta de lo que salió y reintentar los fallidos.
- Envío masivo (gerente y administrador): elige el filtro y pulsa Preparar envío. Verás cuántos documentos salen, cuáles se excluyen y por qué, y una vista previa. Preparar no manda nada: el envío empieza al confirmar. Después sigues el progreso y puedes reintentar solo los fallidos.
- Recordatorios (gerente y administrador): en modo manual confirmas cada aviso; el saldo del mensaje se recalcula al confirmar y, si el cliente ya pagó, el aviso se omite. Con "Pausa" dejas de avisar a un cliente hasta que la levantes.
