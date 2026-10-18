# Manual de usuario — Atlas Rent

> Describe cómo funciona la app según la especificación.
>
> **Estado a 15/10/2026.** Este manual describe la app **tal como sedeliveryá**, no la que hay hoy:
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
- **Calendario:** qué está libre por día; el medio día ocupa media casilla.

## 6. Cotizaciones 🟡
1. Cotizaciones → *Nueva*. Cliente o prospecto, unidad (opcional), líneas, moneda.
2. *Enviar* (si tienes permiso) → pasa a **Enviada**.
3. Marca **Aceptada** o **Rechazada**. Desde una aceptada puedes crear la reserva o el contrato con los datos ya puestos.

## 7. Reservas 🟡
**Mensual:** cliente, unidad, fecha de entrada prevista. Bloquea la unidad. Si en 7 días no hay contrato, sale un aviso.

**Corta:** por cada día eliges **Medio día** o **Día completo** (puedes escribir una hora orientativa). En un mismo día caben dos medios días o un día completo.

**Anticipo:** si el cliente adelantó dinero, anótalo en la reserva (importe, fecha). **Es solo una nota, no un cobro, y no aparece en ningún saldo ni informe económico.** El cobro se registrará en la fase de finanzas; todavía no existe, y tampoco el aviso que te lo recordará.

## 8. Contratos 🟡
1. Desde la reserva o Contratos → *Nuevo*. Renta, moneda, día de pago, fianza y renovación.
2. *Enviar a aprobación*. El gerente lo aprueba o lo devuelve con comentario.
- Se renueva solo hasta que se cancele. Las subidas de renta programadas se aplican solas.
- **Baja:** el gerente pone la fecha de fin. En esa fecha el contrato **se finalize solo** y la unidad queda libre. Después hay que **liquidar**: se revisa el inventario de salida y se decide qué pasa con la fianza. **Los cobros y los saldos todavía no existen** (fase de finanzas), así que por ahora la liquidación deja constancia de qué falta y de la decisión, pero no mueve dinero.

## 9. Facturas ⬜
- **Del mes:** el gerente pulsa *Generar facturas periódicas*, revisa la simulación y confirma.
- **Manual:** *Nueva factura* desde la unidad o el cliente. Queda en **borrador** hasta que el gerente la emite.
- **Emitir** pone el número definitivo y, en la serie 3, el **número de comprobante fiscal** automáticamente. Si quedan pocos comprobantes, la app avisa; si se acaban o vencen, no deja emitir hasta cargar un rango nuevo.
- **Moneda:** cada factura va en una moneda y no cambia. Si la necesitas en la otra moneda, hay que anularla y hacer otra.
- **E-factura:** no se hace aquí. Si se hizo en la otra aplicación, puedes teclear su número en *Referencia e-factura*.
- **Mora:** añádela como una línea más.
- **Proforma:** presupuesto que no genera deuda; con un botón se convierte en factura con los mismos importes.
- **Duplicar:** crea un borrador igual, sin número ni cobros.
- **Anular** (gerente): pide motivo. **Los cobros de esa factura también se anulan.** Si un cobro pagaba varias facturas, solo se anula la parte de esta. La pantalla te muestra qué cobros se verán afectados antes de confirmar.

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

## 15. Preguntas frecuentes
- **Me equivoqué en una factura emitida.** El gerente la modifica (si no tiene comprobante) o la anula y emite otra.
- **No veo un botón.** Tu usuario no tiene ese permiso.
- **El cliente dice que pagó y no aparece.** Solo el gerente registra cobros; pásale el justificante.
- **No puedo entrar.** Espera 30 minutos o pide al administrador que te desbloquee.
