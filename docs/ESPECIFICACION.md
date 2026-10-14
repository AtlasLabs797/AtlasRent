# Atlas Rent — Sistema interno de gestión de alquileres
## Especificación funcional y técnica

Versión **3.2** · 14/10/2026 · Sustituye a las versiones 2.x, 3.0 y 3.1. La 3.2 hace de Atlas Rent un proyecto independiente (no reutiliza ni depende del código de Atlas Balance), fija versiones propias y añade modo oscuro (D31).

> Fuente de verdad del **QUÉ**. El **CÓMO** está en `DOC_TECNICA.md` y el orden de trabajo en `PLAN.md`. Si otro documento contradice este, manda este y la discrepancia se anota en `CHANGELOG.md`.

**Marcas**
- **[Decidido]** confirmado por el responsable.
- **[Supuesto]** valor por defecto razonable; se puede cambiar sin rehacer el diseño.
- **[Verificar]** depende de un tercero (contador, proveedor, licencia).

---

## 0. Decisiones de la revisión (13-14/10/2026)

| # | Tema | Decisión |
|---|---|---|
| D1 | Stack | **Stack propio**: ASP.NET Core 10 + EF Core 10 + PostgreSQL + React 19 + TypeScript 6 + Vite 8 + Zustand 5 + TanStack Query 5. Versiones exactas en DOC_TECNICA §2. Dependencias nuevas solo si son imprescindibles y se proponen antes. [Decidido] |
| D2 | Despliegue | **Windows Server x64**, API y Watchdog como **servicios de Windows**, backend publicado autónomo (self-contained), frontend estático, Kestrel con HTTPS directo (Caddy opcional), scripts PowerShell de instalación/actualización/desinstalación, paquetes ZIP x64, actualizaciones con validación, copia previa y recuperación. Instalación en `C:\AtlasRent`. Sin Docker en el servidor. [Decidido] |
| D3 | Acceso | **Por web desde internet**, con usuario y contraseña. Sin VPN. Protección: bloqueo de cuenta, **bloqueo de IP**, rate limit, contraseñas comprobadas contra filtraciones, aviso de IP nueva, auditoría con IP. [Decidido] |
| D4 | MFA | **Solo administrador.** Riesgo del gerente sin MFA aceptado por el responsable (SEGURIDAD.md, SEC-R01). Queda un interruptor de configuración "MFA para gerente" **desactivado**, para poder activarlo sin desarrollo. [Decidido] |
| D5 | e-factura | **No.** La app usa **comprobante fiscal normal** con numerador propio. Si una factura necesita e-factura, se hace en otra aplicación y aquí se teclea el número a mano en un campo opcional. Sin API. [Decidido] |
| D6 | Anular factura | **Anula también los cobros asignados a esa factura.** Si un cobro pagó varias facturas, solo se anula la parte de la factura anulada. [Decidido] |
| D7 | Moneda de documentos | Cada documento (cotización, proforma, factura) va en **una sola moneda** y nunca se convierte. USD 1.000 en la proforma = USD 1.000 en la factura. Si se quiere en pesos, se hace en pesos. [Decidido] |
| D8 | Pago en otra moneda | **Se permite.** Se registra un cobro en la moneda recibida con su tipo de cambio y se aplica a la factura en la moneda de la factura (ver 4.10.2). Sustituye la práctica actual de duplicar la factura. [Decidido] |
| D9 | Tipo de cambio | Solo para **informes consolidados**. Se congela en cada factura al emitirla y en cada cobro al registrarlo. [Decidido] |
| D10 | Anticipo de reserva | **Es solo una nota** (importe, moneda, fecha, observación). No es cobro, no descuenta deuda. El cobro se registra cuando se paga la factura. [Decidido] |
| D11 | Alquiler corto | Solo **medio día** o **día completo**, sin franjas fijas. **Se cobra antes.** Sin alquiler por horas. [Decidido] |
| D12 | Recordatorios | Modo **automático** o **manual** (los lanza el gerente), configurable; pausa por cliente. Solo gerente/admin. [Decidido] |
| D13 | Mora | Manual, como una línea más de la factura. [Decidido] |
| D14 | ITBIS y retenciones | Pantalla de configuración con el % de cada uno. Sin cifras en el código. [Decidido] |
| D15 | Envío de documentos | Facturas, proformas y recibos: **solo gerente/admin**. Cotizaciones: empleado **si tiene el permiso activado** en su usuario. [Decidido] |
| D16 | Datos personales | Cédula/RNC/pasaporte **visibles completos** (sin enmascarar), **cifrados en la base de datos**. [Decidido] |
| D17 | Permisos | Recortables por persona (se quitan permisos del rol, nunca se añaden). [Decidido] |
| D18 | Cotizaciones | Borrador → Enviada → Aceptada / Rechazada. Sin vigencia ni estado vencida. [Decidido] |
| D19 | Idioma | Español. Inglés activable por configuración. [Decidido] |
| D20 | Conservación | Datos con efecto económico: **10 años** (Código Tributario, art. 50 h, Ley 11-92). Backups: **diario, se guardan los 4 últimos**, con copia cifrada en **Google Drive**. [Decidido] |
| D21 | Email | Cuenta propia de Google, **configurable** (SMTP). [Decidido] |
| D22 | Tipo de cambio diario | ExchangeRate-API con fallback manual. [Decidido] |
| D23 | WhatsApp | **Proveedor por decidir.** Se construye detrás de una interfaz; las tareas de WhatsApp quedan bloqueadas hasta elegirlo. [Decidido] |
| D24 | Equipo y fechas | Varias personas, avance progresivo, **sin fechas fijas**. [Decidido] |
| D25 | Serie 9 | Se valida con una prueba real durante la migración. [Decidido] |
| D26 | Series 3 y 4 | Facturas en **USD o DOP** indistintamente. [Decidido] |
| D27 | Redondeo | Half-up por línea, 2 decimales. [Decidido] |
| D28 | Comprobantes anulados | Solo se marcan como anulados; no se reutilizan. [Decidido] |
| D29 | Nombre | **Atlas Rent** (familia Atlas Labs). Repositorio `AtlasLabs797/AtlasRent`. [Supuesto] |
| D30 | PostgreSQL | Mínimo **16**. Si el servidor ya tiene una instancia ≥16, se puede usar con **base de datos y roles propios** (ningún rol de otra aplicación accede a la base de Atlas Rent ni al revés); si se instala nueva, la última estable (18). Nada de la app depende de funciones exclusivas de 17/18. [Decidido] |
| D31 | Tema visual | **Claro y oscuro**, con paridad completa. Por defecto sigue la preferencia del sistema; cada usuario puede elegir Claro / Oscuro / Sistema desde la barra superior y la elección se recuerda en ese navegador. Los PDF siempre en claro. [Decidido; dónde se guarda la elección: Supuesto] |

**Proyecto independiente:** Atlas Rent no reutiliza ni depende del código de otras aplicaciones de Atlas Labs. Con la familia Atlas Labs solo comparte el lenguaje visual, que vive en su propio design system (`Atlas Rent Design System/`, ver `docs/DESIGN.md`).

**Correcciones técnicas heredadas de la v2.1 que siguen vigentes:** pendiente calculado por factura (no facturado − cobrado); números importados en el mismo campo con contador inicializado a `max+1`; borradores para el empleado en lugar de solicitudes de aprobación; exportaciones solo gerente/admin; auditoría inmutable a nivel de base de datos; PDFs generados por código sin HTML de usuario; facturas con retención con indicador "retención sin comprobante".

**Riesgo fiscal anotado (no bloquea):** la Ley 32-23 fija el 15/11/2026 como último plazo de e-factura obligatoria para pequeños, micro y no clasificados (Aviso DGII 06-26). La validez del comprobante normal después de esa fecha la confirma el contador.

---

## 1. Descripción

Aplicación web interna para **sustituir Factusol** en la gestión de unas **150 unidades** de alquiler mensual (medio y largo plazo) y de alquileres cortos por **medio día o día completo** (oficinas, salas).

Centraliza clientes, unidades, cotizaciones, reservas, contratos, renovaciones, facturación, cobros, retenciones, fianzas, inventarios, bajas y liquidaciones, documentos, envíos por email/WhatsApp, recordatorios, informes, usuarios y auditoría.

**No es:**
- Sistema de e-factura (D5).
- Conciliación bancaria (futuro).
- Contabilidad general ni nómina.
- Multiempresa: una sola empresa; las "subempresas" son etiquetas de propietaria/facturadora.

---

## 2. Roles y permisos

**Sí** = permitido · **Permiso** = solo si el usuario lo tiene activado.

| Acción | Admin | Gerente | Empleado |
|---|---|---|---|
| Configuración, series, numeradores, impuestos, usuarios, roles | Sí | No | No |
| Ver clientes, unidades, reservas, contratos | Sí | Sí | Sí |
| Crear/editar clientes, prospectos, reservas | Sí | Sí | Sí |
| Documentos de propiedad (PDF) | Sí | Sí | No |
| Crear/editar contratos en borrador | Sí | Sí | Sí |
| Aprobar, activar, modificar activo, dar de baja, liquidar | Sí | Sí | No |
| Ver facturas individuales | Sí | Sí | Sí |
| Crear/editar facturas, proformas y cotizaciones en borrador | Sí | Sí | Sí |
| Emitir factura (asigna número y comprobante) | Sí | Sí | No |
| Modificar o anular factura emitida | Sí | Sí | No |
| Registrar, corregir o anular cobros | Sí | Sí | No |
| Fianzas: cobrar, devolver, descontar | Sí | Sí | No |
| Enviar facturas, proformas, recibos, estados de cuenta | Sí | Sí | No |
| Enviar cotizaciones | Sí | Sí | Permiso |
| Envío masivo y recordatorios | Sí | Sí | No |
| Editar plantillas de mensaje | Sí | No | No |
| Totales, informes, dashboard económico, exportaciones | Sí | Sí | No |
| Auditoría | Sí | Lectura | No |
| Desbloquear IP o cuenta | Sí | No | No |

**Reglas**
- Todo se valida en backend. Ocultar un botón no es protección.
- Un intento no permitido se rechaza (403) y se audita.
- El rol fija el **techo** de permisos; por usuario se activan o desactivan permisos **dentro de ese techo**, nunca por encima (D17). "Enviar cotizaciones" está dentro del techo del empleado y viene **desactivado** al crear el usuario.
- El empleado nunca ve totales agregados (ni en pantallas, ni en API, ni en exportaciones).
- MFA (TOTP) obligatorio para admin (D4).

---

## 3. Funcionalidad

### 3.1 Inmuebles y unidades
- **Inmueble:** edificio o villa. Puede tener varias unidades o alquilarse completo (unidad implícita, invisible en pantalla).
- Internamente todo lo alquilable es un **recurso** ("Unidad" en pantalla). Contratos y reservas apuntan siempre a recurso.
- Datos del inmueble: nombre, dirección, tipo, propietaria (subempresa), notas, plantilla de contrato por defecto, renovación por defecto (meses).
- Datos de la unidad: nombre/número, tipo, propietaria, facturadora por defecto, modalidades permitidas (mensual / medio día / día completo), tarifa por modalidad y moneda, estado manual (disponible / mantenimiento / fuera de servicio), opcionales (m², capacidad, habitaciones, baños, medidor, notas).
- **Documentos PDF** por inmueble con categorías (compraventa, título, otros) y versiones. Solo gerente/admin.
- **Inventario base** por unidad (opcional), sin fotos.
- **Ocupada / Reservada** se calcula por fechas, nunca es un campo.
- **Calendario** de disponibilidad por unidad e inmueble.

### 3.2 Clientes y prospectos
- **Número de cliente** de Factusol conservado; nuevos con secuencia de base de datos; nunca se reutiliza. Distinto del ID técnico.
- Tipos: persona física / empresa. Obligatorios: número, nombre/razón social, tipo, documento (cédula, RNC o pasaporte), teléfono.
- Opcionales: nacionalidad, dirección, notas, idioma, canal preferido.
- **Contactos múltiples:** teléfonos y emails como registros (tipo, etiqueta, principal, usar para WhatsApp, usar para email).
- Marcas: **agente de retención**, **en gestión de cobro**, **recordatorios pausados**.
- Documento de identidad: visible completo, cifrado en BD, búsqueda exacta por documento mediante índice ciego (D16).
- **Prospecto:** entidad aparte para cotizaciones. Recibe número de cliente al convertirse.
- Ficha con pestañas: datos, contratos, reservas, facturas, cobros, fianzas, documentos, mensajes recibidos (solo lectura).

### 3.3 Cotizaciones
- Para cliente o prospecto; opcionalmente una unidad. Líneas, moneda única, PDF.
- Estados: **Borrador → Enviada → Aceptada / Rechazada** (D18).
- Desde Aceptada: botón "Crear reserva" o "Crear contrato" con los datos copiados. No se crea nada automáticamente.

### 3.4 Reservas
**Mensual (antes del contrato):** cliente, unidad, fecha de reserva, entrada prevista, observaciones. Bloquea la unidad desde la entrada prevista.

**Corta (medio día / día completo, D11):** cliente, unidad, fecha(s), tipo por día (medio día / completo), hora orientativa en texto libre opcional, observaciones.
- Capacidad por unidad y día: **2 medios días o 1 día completo**. Un día completo bloquea el día; dos medios días también.
- **Se cobra antes:** al confirmar la reserva corta se crea la factura (borrador o emitida según rol). Indicador "sin cobrar" visible hasta que la factura esté pagada.

**Anticipo (D10):** campo de nota en la reserva: importe, moneda, fecha, observación, y si se cancela, "devuelto / retenido" como anotación. **No es cobro.** Al crear la primera factura del contrato o la factura de la reserva corta, la app muestra el aviso "Anticipo anotado: USD X el dd/mm".

**Estados:** Activa → Convertida (en contrato) | Completada (corta) | Cancelada | En conflicto.
- **En conflicto:** llega la fecha de entrada y el contrato sigue en borrador. Sigue bloqueando hasta que el gerente decida.
- Alerta si una reserva mensual lleva **7 días** sin convertirse (una vez, persistente).
- La disponibilidad se comprueba siempre en base de datos (sin carreras entre dos usuarios).

### 3.5 Contratos
- Obligatorios: cliente, unidad, facturadora, inicio, duración inicial, renta, moneda, día de pago, fianza(s), regla y duración de renovación, plantilla.
- Flujo: **Borrador → Pendiente de aprobación → Aprobado → Activo → Baja programada → Finalizado → Liquidado.** Rechazo vuelve a Borrador con comentario.
- Empleado crea y edita borradores y los envía a aprobación; solo gerente/admin aprueban, modifican activos, dan de baja y liquidan.
- **Versionado:** cada cambio en un contrato activo crea versión; los cambios de renta afectan solo a facturas futuras.
- **Renovación automática** por bloques (6 meses, 1 año u otro) hasta cancelación; queda en historial, sin anexo ni firma nueva.
- **Incrementos de renta:** % o importe fijo, con fecha; se aplican solos con aviso interno.
- **Baja:** preaviso recomendado 30 días, sin penalización, fecha efectiva de fin.
- **Fin ≠ liquidación:** el fin libera la unidad; la liquidación cierra inventario de salida, fianza y saldos. Alerta persistente hasta liquidar.
- Plantilla congelada al aprobar. Inventario copiado y congelado al activar. Anexos: catálogo, uno activo por tipo.

### 3.6 Inventario de salida
- Si el contrato tiene inventario, revisión de salida obligatoria antes de liquidar: estado de cada elemento, faltantes/daños, importe propuesto, observación. Sin fotos.
- Los descuentos se aplican contra la fianza solo con gerente/admin.

### 3.7 Series y comprobantes
- **Catálogo de series** configurable. Cada serie: prefijo, contador propio, **moneda** (USD / DOP / **libre**), tipo (Normal / Proforma / Fiscal / Histórica), plantilla PDF, suma a facturado (sí/no), suma a saldos (sí/no), genera comprobante fiscal (sí/no).
- Series iniciales: **1** USD · **2** DOP · **3** fiscal, moneda libre · **4** proforma, moneda libre · **9** histórica (solo importación).
- **Contador transaccional:** se incrementa en la misma transacción que la emisión; si falla, no se consume número. Unicidad (serie, número).
- Facturas importadas conservan su número en el mismo campo; el contador arranca en `max(importado)+1`.
- **Numerador de comprobante fiscal** (1:1 con la serie fiscal): tipo/prefijo, rango autorizado (desde-hasta), fecha de vencimiento, siguiente número, umbral de alerta. Se asigna en la misma transacción que el número de factura.
  - Alertas por números restantes y por vencimiento.
  - **Bloqueo** de emisión si está agotado o vencido.
  - Comprobante de factura anulada: marcado como anulado, nunca se reutiliza (D28).
  - No se puede cambiar "genera comprobante" en una serie con facturas emitidas: se crea serie nueva.
- **Referencia e-factura externa** (D5): campo de texto opcional en la factura, tecleado a mano por gerente/admin, auditado, impreso en el PDF si tiene valor. Sin validación ni lógica adicional.
- **[Verificar]** formato y rangos del comprobante con el contador.

### 3.8 Facturación
- **Líneas** editables: renta, mora, ajuste, servicio, suministro, cargo, descuento. ITBIS opcional por línea; % por defecto desde la unidad o el contrato.
- **Moneda única por factura** (D7). En series de moneda fija, la factura toma esa moneda.
- **Estados de documento:** Borrador / Emitida / Anulada / Convertida (proforma). **Estado de cobro** (derivado): Pendiente / Parcial / Pagada. **Vencida** derivada (saldo > 0 y fecha superada).
- **Emitir** (gerente/admin): asigna número y, si aplica, comprobante; congela el tipo de cambio del día **solo para informes** (D9); a partir de aquí el número no cambia.
- **Periódica:** "Generar facturas periódicas" con **simulación obligatoria**. Contratos activos del mes natural; crea facturas emitidas. Impide duplicados (contrato, periodo, concepto). Alerta de "periodos sin facturar".
- **Primera factura** de un contrato: acción manual de gerente/admin.
- **Manual:** desde unidad (propone cliente del contrato activo) o desde cliente.
- **Suministros:** facturas separadas, importe manual; vencimiento = día de pago del contrato; si se crea después del vencimiento del mes, vence en el siguiente ciclo.
- **Prorrateo** (inicio y fin): días inclusivos; renta / días del mes × días facturables.
- **Mora** (D13): línea manual.
- **Modificar** emitida (gerente/admin): conceptos, cantidades, precios, descuentos, ajustes; nunca número ni comprobante; auditado. Si la factura tiene comprobante fiscal, **no se cambian importes**: se anula y se emite otra. **[Verificar]** con el contador.
- **Anular** (gerente/admin, motivo obligatorio) (D6):
  - El número y el comprobante quedan anulados y no se reutilizan.
  - Las **asignaciones de cobros a esa factura se anulan**. Si un cobro solo pagaba esa factura, el cobro queda anulado. Si pagaba varias, solo se anula la parte de esta factura; el resto del cobro sigue aplicado a las demás.
  - La pantalla de confirmación muestra los cobros afectados y sus importes.
  - Nada se borra físicamente: todo queda en historial y auditoría.
- **Duplicar:** copia cliente, unidad, contrato, moneda, líneas, impuestos y retenciones configuradas. No copia número, comprobante, cobros, envíos, retención recibida, referencia e-factura ni proforma de origen. Nace en **borrador** con la fecha del día. Avisa si ya existe una del mismo contrato y periodo.
- **Proforma → factura:** misma moneda e importes (D7); la proforma queda "Convertida" y no cuenta dos veces. Serie destino por defecto 3, editable.
- **Rehacer en otra moneda:** si el cliente necesita el **documento** en otra moneda (no solo pagar en otra), se anula y se crea un borrador en la moneda nueva con importe tecleado. Si solo quiere **pagar** en otra moneda, se usa 3.10.2.

### 3.9 ITBIS y retenciones (D14)
- Pantalla de configuración: tipos de ITBIS (nombre, %) y tipos de retención (nombre, %, base: subtotal / ITBIS / total). Valor por defecto marcado.
- La retención aplica solo a clientes agente de retención: líneas de retención en la factura.
- **Neto a cobrar** = total − retenciones. La factura está **Pagada** cuando cobros + retenciones = total. La retención **no** es dinero cobrado.
- Seguimiento del **comprobante de retención** (pendiente / recibido). Mientras falte: indicador "Retención sin comprobante" y alerta.

### 3.10 Cobros
#### 3.10.1 Registro
- Solo gerente/admin. Datos: cliente, fecha efectiva, **moneda e importe recibidos**, forma de pago (configurable), referencia, cuenta receptora, asignaciones.
- **Asignación** por defecto a la deuda más antigua del mismo contrato; editable. Permite parciales, varias facturas, fianza, y excedente → **saldo a favor del contrato**. Nunca mueve dinero entre contratos automáticamente.
- Corrección/anulación con motivo; revierte asignaciones; historial completo.
- Recibo PDF opcional.

#### 3.10.2 Pago en otra moneda (D8)
Sustituye la práctica actual de duplicar la factura en la otra moneda, que duplicaba deuda, gastaba números y comprobantes y descuadraba informes.
1. Se selecciona la factura (p. ej. USD 1.000) y se registra lo recibido: **DOP 59.500**.
2. La app propone el tipo de cambio del día; el gerente puede fijar el realmente aplicado (p. ej. 59,50).
3. Equivalente en moneda de la factura = 59.500 / 59,50 = **USD 1.000,00**, que se asigna a la factura.
4. Cada asignación guarda **ambos importes**: el de la moneda de la factura y el de la moneda recibida, más el cambio.
5. **Diferencia por redondeo o cambio:** si queda un saldo pendiente menor o igual a la **tolerancia configurada** (por defecto USD 1,00 / DOP 50,00 [Supuesto]), el gerente puede cerrarlo con un ajuste "Diferencia de cambio" auditado. Por encima de la tolerancia queda como saldo pendiente o se corrige el cambio.
6. La factura sigue siendo de USD 1.000: no se duplica, no se gasta otro número ni comprobante.

### 3.11 Fianzas
- Entidad propia, **visible en la vista de cobros**. Importe exigido, número de fianzas, moneda, cobrado, saldo, estado, contrato.
- Estados: Pendiente → Parcial → Retenida → En liquidación → Devuelta / Retenida parcial.
- Devolución completa o parcial; si es parcial, **motivo e importe descontado obligatorios**.
- Alerta persistente si tras el fin del contrato sigue sin devolver o liquidar.

### 3.12 Tipo de cambio (D9, D22)
- Moneda base de informes: **USD**. Monedas: USD y DOP.
- Cambio diario por ExchangeRate-API, guardado con fecha, fuente y valor. Si la API falla: alerta y carga manual.
- Se congela en la factura al emitir y en el cobro al registrar. **No modifica importes de documentos.** Solo sirve para consolidar informes.

### 3.13 Vista única de cuentas y cobros
Pantalla principal del día a día (sustituye Factusol).
- **Filas:** facturas y fianzas (insignia de tipo). Proformas con filtro. Las reservas con anticipo anotado muestran un icono informativo, no una fila de dinero.
- **Columnas:** serie-número, comprobante, fecha, cliente, unidad, concepto, moneda, total, retención, cobrado, **pendiente**, vencimiento, días vencida, estado, estado de envío.
- **Filtros:** año, serie, moneda, estado, cliente, unidad, vencidas, no enviadas, envío fallido, modificadas tras envío. Estado del filtro en la URL.
- **Cobro desde la misma pantalla:** seleccionar filas → panel lateral con importe, moneda recibida, cambio, forma de pago y asignación propuesta editable.
- **Acciones por fila:** ver, duplicar, modificar, anular, PDF, enviar (según rol).
- **Totales al pie:** solo gerente/admin, por moneda.

### 3.14 Documentos, email y WhatsApp
- **PDF:** cotización, contrato, anexos/inventario, factura, proforma, recibo, estado de cuenta. Plantilla por serie (diseños finales: los aporta el responsable más adelante).
- **Plantillas de mensaje** por tipo (cotización, factura, recordatorio, recibo, estado de cuenta) e idioma; variables `{cliente}`, `{numero}`, `{total}`, `{pendiente}`, `{vencimiento}`, `{unidad}`. Email con asunto, cuerpo y PDF adjunto.
- **Destinatarios:** contactos de la ficha con casillas; principal por defecto.
- **Envío masivo** (gerente/admin): pantalla de resumen (cuántos, excluidos con motivo, vista previa), confirmación, cola, progreso, reintentar fallidos. Un mensaje por documento.
- **Estado de envío** en tabla `envio` (pendiente, enviado, entregado, leído, fallido). Copia exacta del PDF enviado. Si el documento cambia después: "modificado tras el envío".
- **Antiduplicados:** mismo documento al mismo destinatario se excluye salvo forzado.
- **Email** (D21): SMTP configurable (cuenta Google por defecto).
- **WhatsApp** (D23): proveedor por decidir. Requisitos que cumplirá el proveedor elegido:
  - Fuera de la ventana de 24 h solo plantillas aprobadas por Meta.
  - Webhook con firma validada e idempotente.
  - Mensajes entrantes guardados como texto, solo lectura en la ficha del cliente, sin chat.
  - "STOP" excluye el contacto de envíos automáticos.
- **Recordatorios** (D12): primero a los **5 días** del vencimiento, luego cada **15** mientras haya saldo [Supuesto, configurable]. Se detienen al pagar. Con pago parcial, mencionan solo el pendiente. **Un mensaje por cliente y unidad** con texto corto y **PDF de estado de cuenta** adjunto.
  - Modo **automático**: la app los envía sola cada día.
  - Modo **manual**: la app prepara la lista diaria y el gerente la revisa y envía.
  - Interruptor global (automático / manual / apagado) y pausa por cliente.

### 3.15 Informes y dashboard (gerente/admin)
| Bloque | Cálculo |
|---|---|
| Proformado | Proformas sin convertir (informativo, aparte) |
| Facturado | Facturas emitidas no anuladas de series que suman a facturado |
| Cobrado | Asignaciones de cobro vigentes, por destino (facturas, fianzas, saldo a favor) |
| Retenido | Retenciones aplicadas (con / sin comprobante) |
| Pendiente | Σ saldo de cada factura en su moneda |
| Vencido | Pendiente con vencimiento superado |
| Fianzas retenidas | Cobradas y no devueltas |
| Diferencias de cambio | Ajustes por diferencia de cambio |

- USD y DOP por separado; consolidado en USD con el cambio congelado de cada documento.
- Filtros: fechas, propietaria/facturadora, inmueble, unidad, cliente, contrato, moneda, estado, serie.
- Exportación Excel/CSV.
- Indicadores: unidades disponibles, reservadas, ocupadas, en mantenimiento; contratos activos; bloques económicos.
- **Los informes deben cuadrar con la vista única** (test automático).

### 3.16 Alertas y bandeja
- Alertas: facturas vencidas, bajas programadas, unidades que se liberan en 30 días, reservas > 7 días, conflictos, fianzas pendientes de devolver, liquidaciones pendientes, periodos sin facturar, comprobantes por agotarse o por vencer, retenciones sin comprobante, envíos fallidos, fallo del tipo de cambio, fallo de backup, IP bloqueadas, entradas desde IP nueva.
- **Solicitudes de aprobación:** contratos enviados a aprobación (y en el futuro, acciones de agentes). Muestra quién, qué, valor actual y propuesto. Caduca si el registro cambia antes de aprobar.
- Canales: aviso en app y email.

### 3.17 Eventos e idempotencia
Eventos de negocio con **outbox** en la misma transacción. Un evento repetido no puede producir dos facturas, dos cobros ni dos mensajes.

### 3.18 IA y agentes (fase posterior, fuera del alcance inicial)
- API propia con token por usuario/agente, mismos permisos y auditoría; el agente nunca supera el rol del usuario.
- Primero solo lectura; después propuestas que pasan por aprobación.
- Contenido externo (WhatsApp, Excel, PDFs) nunca se trata como instrucción.
- Integración de IA con proveedores configurables detrás de una interfaz (se eligen al empezar la fase 6), validación de permisos, límites y presupuestos, auditoría y seudonimización de datos sensibles. Agentes externos mediante tokens con permisos delimitados.

---

## 4. Arquitectura (resumen; detalle en DOC_TECNICA.md)

```
Navegador → React SPA → API REST ASP.NET Core → EF Core → PostgreSQL
Usuarios (internet / oficina) ──HTTPS 443──▶ Windows Server x64 (C:\AtlasRent)
                                             ├─ Servicio Windows: AtlasRent.Api (Kestrel, Caddy opcional)
                                             │    API REST /api/v1 + frontend estático
                                             │    Hangfire: backups, alertas, tipo de cambio, recordatorios,
                                             │    integridad de auditoría, limpieza, sesiones, actualizaciones
                                             ├─ Servicio Windows: AtlasRent.Watchdog (operaciones críticas)
                                             └─ PostgreSQL ≥16 (solo localhost; RLS)
Salidas: SMTP Google · ExchangeRate-API · Google Drive (backups cifrados) · GitHub Releases · proveedor WhatsApp
```

| Capa | Elección |
|---|---|
| Backend | C# + ASP.NET Core 10 (SDK 10.0.302, `global.json` con `rollForward: latestFeature`), Kestrel, servicios de Windows, MemoryCache |
| ORM | EF Core 10 + Npgsql 10 + EFCore.NamingConventions (snake_case); SQL manual en migraciones para exclusiones, triggers, RLS y permisos |
| BD | PostgreSQL ≥16 (D30), RLS |
| Jobs | Hangfire 1.8 + Hangfire.PostgreSql |
| Frontend | React 19 + TypeScript 5 + Vite 8 + React Router 8 + Zustand 5 + TanStack Query 5 + TanStack Virtual 3 + Axios + React Hook Form 7 + Recharts 3 + Lucide React + QRCode; CSS nativo con variables y tokens del design system, temas claro y oscuro (D31); React.lazy |
| PDF | **QuestPDF** (generación por código, sin HTML) **[Verificar licencia: la Community es gratuita solo si la empresa factura menos de 1 M USD brutos al año; si no, licencia Professional]** |
| Excel | ClosedXML 0.105 |
| Email | MailKit / MimeKit 4.17 |
| Seguridad | JWT + cookies HttpOnly + CSRF, BCrypt, MFA/TOTP, rate limiting, auditoría firmada, RLS, AES-256-GCM para documentos de identidad |
| Logs | Serilog 10 con redacción de datos sensibles |
| Tests | xUnit v3, AwesomeAssertions, Testcontainers PostgreSQL; ESLint 10, Node.js Test Runner, Playwright |
| Desarrollo | Node.js 24 + npm, .NET SDK 10, Docker Compose (solo PostgreSQL de desarrollo) |

---

## 5. Reglas de implementación críticas
- **Dinero:** `numeric(18,2)` en BD, `decimal` en C#, nunca `double`/`float`. Cambio `numeric(18,6)`. Redondeo half-up por línea (`MidpointRounding.AwayFromZero`).
- **Fechas:** de negocio `date` (`DateOnly`); marcas de tiempo `timestamptz` en UTC; zona de negocio `America/Santo_Domingo`.
- **Numeración:** `UPDATE ... RETURNING` del contador y del comprobante en la misma transacción de emisión.
- **Disponibilidad:** restricción de exclusión `btree_gist` para rangos (contratos y reservas mensuales); para reservas cortas, comprobación en trigger con bloqueo por unidad (capacidad 2 medios días).
- **Idempotencia:** `UNIQUE (contrato, periodo, concepto)` en facturas periódicas no anuladas; outbox; clave de idempotencia en webhooks y en mutaciones sensibles.
- **Saldo de factura** = total − retenciones − asignaciones vigentes − ajustes de diferencia de cambio, en la moneda de la factura.
- **Auditoría:** append-only a nivel de permisos de BD + trigger + **firma de registros** (hash encadenado SHA-256, `db/scripts/03_auditoria_append_only.sql`) con verificación diaria de integridad.
- **Política central:** `IPolicyService.Decidir(actor, accion, contexto)` en todos los endpoints.
- **Concurrencia optimista** con la columna de sistema `xmin` de PostgreSQL como token de concurrencia (Npgsql).

## 5.1 Modelo de datos (núcleo)
**Identidad y configuración:** `usuario`, `rol`, `permiso`, `usuario_permiso`, `sesion`, `bloqueo_ip`, `intento_login`, `empresa`, `subempresa`, `serie`, `secuencia_comprobante`, `tipo_itbis`, `tipo_retencion`, `forma_pago`, `cuenta_bancaria`, `plantilla_mensaje`, `plantilla_contrato`, `configuracion`.

**Terceros y activos:** `cliente`, `cliente_contacto`, `prospecto`, `inmueble`, `recurso`, `recurso_tarifa`, `inventario_base`, `inventario_item`, `documento_inmueble`.

**Operación:** `cotizacion`, `cotizacion_linea`, `reserva`, `reserva_dia` (cortas), `contrato`, `contrato_version`, `contrato_renovacion`, `contrato_incremento`, `contrato_inventario`, `anexo_catalogo`, `contrato_anexo`, `revision_salida`.

**Finanzas:** `factura` (serie, numero, comprobante_id, cliente, recurso, contrato, moneda, cambio_informe, estado, totales, referencia_efactura_externa, proforma_origen_id), `factura_linea`, `factura_retencion`, `comprobante_fiscal`, `cobro` (moneda_recibida, importe_recibido), `cobro_asignacion` (factura | fianza | saldo_favor; importe_moneda_destino, importe_moneda_recibida, cambio_aplicado, estado), `ajuste_diferencia_cambio`, `saldo_favor`, `fianza`, `fianza_devolucion`, `tipo_cambio`.

**Transversal:** `solicitud_aprobacion`, `evento_outbox`, `auditoria`, `mensaje_entrante`, `envio`, `alerta`, `lote_importacion`, `backup_registro` (y las tablas de soporte de Hangfire).

Todo registro importante: `origen` (factusol, manual, automático, agente, importación), `created_at/by`, `updated_at/by`. PK UUID; número visible aparte. Registros con efecto económico: nunca se borran, se anulan. Resto: borrado lógico (`deleted_at`, `deleted_by`).

---

## 6. Migración desde Factusol
- **Alcance:** clientes, inmuebles/unidades, contratos activos, **12 meses** de facturas y cobros, saldos abiertos completos, **todas las fianzas vigentes**.
- **Fuente:** exportación Excel/CSV.
- **Flujo:** exportar → zona temporal → mapeo → validación (duplicados, inconsistencias) → **simulación** → informe → aprobación → importación → **conciliación**.
- Importador determinista; nunca decide importes por su cuenta.
- **Cuadre obligatorio:** totales por serie y moneda iguales a Factusol al céntimo.
- **Serie 9** (D25): prueba real durante la migración para comprobar si duplica deuda de otras series antes de importarla.
- **Serie 3:** comprobantes existentes importados como históricos; el numerador continúa desde el último usado.
- Cada lote con ID, usuario, fecha, registros, errores y resultado.
- Datos reales de Factusol **nunca** en el repositorio.

## 7. Seguridad (resumen; detalle y registro en SEGURIDAD.md)
- HTTPS con certificado válido para un dominio público; HSTS.
- Contraseñas BCrypt (12 rondas), mínimo 12 caracteres, comprobación contra HaveIBeenPwned.
- **Bloqueo de cuenta:** 5 fallos → 30 min. **Bloqueo de IP:** fallos repetidos desde la misma IP → IP bloqueada (escalado). Lista blanca de IP de oficina. Desbloqueo por admin. [Valores configurables]
- Rate limiting por IP y por usuario.
- MFA TOTP para admin.
- Sesión: cookies httpOnly + Secure + SameSite=Strict, CSRF double-submit, inactividad 30 min, máximo 12 h, revocación inmediata.
- Aviso al admin de entrada desde IP nueva.
- Permisos siempre en backend; auditoría con IP de todas las acciones y de los intentos denegados.
- Cifrado AES-256-GCM de documentos de identidad; clave fuera de la BD.
- PostgreSQL solo en localhost; Windows Firewall solo 443 entrante.
- Backups cifrados, 4 diarios, copia en Google Drive, restauración probada.
- Row Level Security y credenciales separadas de aplicación y administración.
- **[Verificar]** Ley 172-13 de protección de datos personales con abogado.

## 8. Calidad
- Entornos: desarrollo, staging (datos anonimizados), producción.
- CI: compilación, tests, análisis de dependencias vulnerables, escaneo de secretos.
- **Tests obligatorios:** numeración y comprobantes concurrentes; prorrateo y redondeo (meses 28-31); saldos con retención, parciales, varias facturas, saldo a favor; pago en otra moneda y tolerancia; anulación con cobros que pagan varias facturas; capacidad de medio día; permisos por rol llamando a la API; bloqueo de cuenta e IP; idempotencia; cuadre de migración; restauración de backup.

## 9. Preguntas abiertas
| # | Pregunta | Valor por defecto mientras tanto |
|---|---|---|
| A1 | Proveedor de WhatsApp | Interfaz preparada; tareas bloqueadas |
| A2 | Diseño de los PDF por serie | Diseño base limpio; se ajusta con los ejemplos que aporte el responsable |
| A3 | Formato y rangos del comprobante fiscal | Configurable [Verificar contador] |
| A4 | Porcentajes de ITBIS y retenciones | Se cargan en la pantalla de configuración |
| A5 | Estructura real de los Excel de Factusol | Muestras al iniciar la migración |
| A6 | Dominio público para el certificado HTTPS | Subdominio del dominio de la empresa |

## 10. Glosario
- **Unidad / recurso:** lo que se alquila.
- **Serie:** numeración de facturas con contador y comportamiento propios.
- **Comprobante fiscal:** número fiscal aparte del número de factura, del numerador de la serie fiscal.
- **Referencia e-factura externa:** número tecleado a mano de una e-factura hecha en otra aplicación.
- **Proforma:** documento sin efecto en deuda ni cobrado.
- **Anticipo:** nota informativa de dinero adelantado en una reserva; no es cobro.
- **Saldo a favor:** dinero cobrado que excede lo facturado, asociado a un contrato.
- **Diferencia de cambio:** ajuste que cierra el pequeño saldo que deja un pago en otra moneda.
- **Outbox:** evento guardado en la misma transacción que el cambio, para no duplicar efectos.
