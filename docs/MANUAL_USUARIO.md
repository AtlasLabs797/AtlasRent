# Manual de usuario — Atlas Rent

> Describe cómo funciona la app según la especificación. Cada apartado se marca ✅ cuando la función está construida y probada. Si algo no coincide con lo que ves, avisa al administrador.

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
| Configuración, usuarios, desbloqueos | No | No | Sí |

Si no ves un botón, tu usuario no tiene ese permiso.

## 4. Clientes
- **Nuevo:** Clientes → *Nuevo*. Obligatorio: nombre, tipo (persona o empresa), documento y teléfono. El número de cliente se pone solo.
- **Contactos:** varios teléfonos y emails; marca cuál es para WhatsApp y cuál para email.
- **Prospecto:** alguien a quien solo has hecho una cotización. Pasa a cliente cuando reserva o firma.
- **Pausar recordatorios:** casilla en la ficha (gerente).
- La ficha tiene pestañas: datos, contratos, reservas, facturas, cobros, fianzas, documentos y mensajes recibidos.

## 5. Unidades
- Un **inmueble** es el edificio o la villa; una **unidad** es lo que se alquila.
- *Ocupada / Reservada* se calcula sola. Tú solo marcas *Mantenimiento* o *Fuera de servicio*.
- **Calendario:** qué está libre por día; el medio día ocupa media casilla.

## 6. Cotizaciones
1. Cotizaciones → *Nueva*. Cliente o prospecto, unidad (opcional), líneas, moneda.
2. *Enviar* (si tienes permiso) → pasa a **Enviada**.
3. Marca **Aceptada** o **Rechazada**. Desde una aceptada puedes crear la reserva o el contrato con los datos ya puestos.

## 7. Reservas
**Mensual:** cliente, unidad, fecha de entrada prevista. Bloquea la unidad. Si en 7 días no hay contrato, sale un aviso.

**Corta:** por cada día eliges **Medio día** o **Día completo** (puedes escribir una hora orientativa). En un mismo día caben dos medios días o un día completo. **Se cobra antes:** al confirmar se prepara la factura y la reserva muestra "Sin cobrar" hasta que se pague.

**Anticipo:** si el cliente adelantó dinero, anótalo en la reserva (importe, fecha). **Es solo una nota, no un cobro.** El cobro se registra cuando se paga la factura; la app te recordará el anticipo al hacerla.

## 8. Contratos
1. Desde la reserva o Contratos → *Nuevo*. Renta, moneda, día de pago, fianza y renovación.
2. *Enviar a aprobación*. El gerente lo aprueba o lo devuelve con comentario.
- Se renueva solo hasta que se cancele. Las subidas de renta programadas se aplican solas.
- **Baja:** el gerente pone la fecha de fin. Después hay que **liquidar**: inventario de salida, fianza y saldos.

## 9. Facturas
- **Del mes:** el gerente pulsa *Generar facturas periódicas*, revisa la simulación y confirma.
- **Manual:** *Nueva factura* desde la unidad o el cliente. Queda en **borrador** hasta que el gerente la emite.
- **Emitir** pone el número definitivo y, en la serie 3, el **número de comprobante fiscal** automáticamente. Si quedan pocos comprobantes, la app avisa; si se acaban o vencen, no deja emitir hasta cargar un rango nuevo.
- **Moneda:** cada factura va en una moneda y no cambia. Si la necesitas en la otra moneda, hay que anularla y hacer otra.
- **E-factura:** no se hace aquí. Si se hizo en la otra aplicación, puedes teclear su número en *Referencia e-factura*.
- **Mora:** añádela como una línea más.
- **Proforma:** presupuesto que no genera deuda; con un botón se convierte en factura con los mismos importes.
- **Duplicar:** crea un borrador igual, sin número ni cobros.
- **Anular** (gerente): pide motivo. **Los cobros de esa factura también se anulan.** Si un cobro pagaba varias facturas, solo se anula la parte de esta. La pantalla te muestra qué cobros se verán afectados antes de confirmar.

## 10. Cuentas y cobros (pantalla principal)
- Tabla con facturas y fianzas. Filtros por año, cliente, unidad, estado, vencidas, no enviadas…
- **Registrar cobro** (gerente): selecciona filas → panel lateral → importe, moneda recibida, forma de pago → *Registrar*. La app aplica el dinero a la deuda más antigua; puedes cambiarlo.
- **Pago en la otra moneda:** si la factura es en USD y el cliente paga en pesos, escribe los pesos recibidos y el cambio aplicado. La app calcula el equivalente y salda la factura en USD. **Ya no hace falta duplicar la factura.** Si queda una diferencia pequeña por el cambio, puedes cerrarla con *Ajustar diferencia de cambio*.
- Si el cliente paga de más, queda como **saldo a favor** de ese contrato.
- **Retenciones:** si el cliente es agente de retención, la factura queda pagada cuando cobro + retención = total. Después registra el comprobante de retención.

## 11. Fianzas
En la misma tabla de cobros. Al terminar el contrato, el gerente la devuelve entera o en parte; si descuenta algo, indica motivo e importe.

## 12. Enviar documentos (gerente y administrador)
- *Enviar* en la factura, proforma o recibo: email (o WhatsApp cuando esté activo) y contactos.
- El estado (enviado, entregado, leído, fallido) se ve en la tabla.
- **Envío masivo:** muestra un resumen antes de enviar.
- **Recordatorios:** según configuración, salen solos o el gerente revisa la lista del día y los envía.

## 13. Avisos y bandeja
La campana muestra vencidas, contratos por vencer o pendientes de aprobar, fianzas pendientes, comprobantes por agotarse, envíos fallidos y avisos de seguridad (administrador).

## 14. Informes (gerente y administrador)
Facturado, cobrado, pendiente, vencido, retenido, fianzas y diferencias de cambio, en USD y DOP por separado y consolidado en USD. Exportables a Excel.

## 15. Preguntas frecuentes
- **Me equivoqué en una factura emitida.** El gerente la modifica (si no tiene comprobante) o la anula y emite otra.
- **No veo un botón.** Tu usuario no tiene ese permiso.
- **El cliente dice que pagó y no aparece.** Solo el gerente registra cobros; pásale el justificante.
- **No puedo entrar.** Espera 30 minutos o pide al administrador que te desbloquee.
