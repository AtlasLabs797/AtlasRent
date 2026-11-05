# F0-07 — Proveedor de WhatsApp (A1)

Resuelve **F0-07**: elegir el proveedor de WhatsApp. Desbloquea **F4-04** (adaptador, plantillas, webhook firmado e idempotente, entrantes, STOP) y **F4-05** (recordatorios automático/manual y estado de cuenta adjunto), hoy ambas **BLOQUEADA (A1)** (`docs/PLAN.md:32` y `docs/PLAN.md:93-94`).

Requisitos que ya están decididos y que el proveedor **tiene** que cumplir, tomados de `docs/ESPECIFICACION.md` §3.14 (líneas 248-252):

- Fuera de la ventana de 24 h solo **plantillas aprobadas por Meta**.
- **Webhook con firma validada e idempotente** (y el criterio de aceptación de F4-04: firma inválida → `401` auditado).
- Mensajes **entrantes** guardados como texto, solo lectura en la ficha del cliente, sin chat.
- **"STOP"** excluye el contacto de envíos automáticos.

Además, el diseño del despliegue (`AGENTS.md`, `ESPECIFICACION.md` §arquitectura): Windows Server x64 on-premise, **salida HTTPS saliente + un endpoint webhook público por 443**.

> Todas las fuentes de este documento se consultaron el **2026-10-15**. Lo que no pude leer de una fuente oficial en esta sesión aparece marcado **no verificado**; no se inventa ninguna cifra.

---

## 1. Resumen ejecutivo — Recomendación

**Ganadora: Twilio WhatsApp API** (Twilio como proveedor de acceso a la WhatsApp Business Platform, con número y plantillas registrados por su proceso de onboarding).

Por qué, en orden de peso real:

1. **El precio no decide aquí.** A 150-400 mensajes al mes, el recargo de Twilio son **USD 0.005 por mensaje**, entrante o saliente (fuente oficial, §3). A 300 mensajes/mes eso son **USD 1,50 al mes**. Ninguna de las alternativas ahorra una cantidad que justifique su riesgo.
2. **Twilio tiene sandbox gratuito y usable sin cuenta de WhatsApp Business.** La propia documentación dice que no hace falta cuenta de WhatsApp Business ni emisor registrado para usarlo (fuente oficial, §6). Eso permite **desarrollar y probar F4-04 entero antes de pagar la primera plantilla**, que es justo el riesgo que la especificación quiere evitar.
3. **El webhook firmado viene resuelto y documentado con validador para C#.** Twilio firma cada request con la cabecera `X-Twilio-Signature`, calculada con HMAC-SHA1 y el Auth Token de la cuenta, y publica ejemplo de validación en C# (fuente oficial, §5). El requisito "firma inválida → `401` auditado" es directo.
4. **SDK oficial de C#** y helpers de webhook para ASP.NET, que encajan con el stack ya decidido (`AGENTS.md`: ASP.NET Core 10).
5. Twilio hace de intermediaria con Meta para el alta del emisor y las plantillas; el trámite más lento y con más rechazo es ese, y que Twilio lo gestione es justo lo que hace que el coste de entrada sea de tiempo, no de margen.

**Lo que NO se está recomendando:**

- **Meta WhatsApp Business Platform (Cloud API) directa**: es la opción más barata en teoría (sin recargo de intermediario), pero implica montar la integración de Graph API, tokens de larga duración, alta de WABA y revisión de empresa **sin sandbox de prueba**. Para el volumen y el equipo de Atlas Labs, el ahorro (≈ USD 1,50-2,00/mes) no compensa el riesgo de bloqueos de F4-04 y de F4-05.
- **360dialog**: el suelo de **EUR 49/mes por número**, con "sin recargo sobre las tarifas de Meta" (fuente oficial, §7), es para este volumen un coste fijo que no compensa, y su documentación de webhooks con firma no se pudo leer en esta sesión (**no verificado**), justo el requisito crítico.

**Condición de salida**: si al completar el paso 3 del plan de decisión (§9) la tarifa de Meta para República Dominicana resulta muy superior a la de otro agregador, o si Twilio rechaza el alta del número, esta recomendación se revisa. La interfaz del adaptador (`IWhatsAppSender`) es la que hace barato cambiar después.

---

## 2. Modelo de precios vigente (importante: cambió en 2025)

- Meta cobra **por mensaje de plantilla enviado**, no por conversación abierta. La documentación oficial de Meta indica que el modelo pasó a **precio por mensaje de plantilla el 1 de julio de 2025** (fuente oficial, §4).
- **Los mensajes de servicio no se cobran**, y **los mensajes de utilidad que la empresa envía en respuesta a un usuario dentro de la ventana de atención de 24 h tampoco** (fuentes oficiales, §4 y §3).
- Los mensajes no plantilla **dentro de la ventana de 24 h** siguen siendo gratuitos (fuente oficial, §4).
- **República Dominicana (+1 809 / +1 829 / +1 849) no tiene tarifa propia**: Meta la agrupa en **"Rest of Latin America"** (fuente oficial, §4).
- Meta **actualiza precios solo el día 1 de cada trimestre** (fuente oficial, §4). La web oficial recoge tarjetas vigentes desde el 1 de julio de 2026 y actualizaciones con efecto 1 de octubre de 2026; República Dominicana **no aparece entre los mercados con cambio el 1 de octubre de 2026** (fuente oficial, §4).
- **La cifra exacta en USD de la categoría "utility" para Rest of Latin America no se pudo extraer de la tarjeta oficial en esta sesión (la tabla se carga por JavaScript): no verificado.** Hay que leerla en el tarifario oficial o en la consola antes de firma cualquier presupuesto (§9, paso 3).

Consecuencia para el diseño: **los recordatorios de pago deberían clasificarse como plantilla `utility`**, no `marketing`, porque responden a la acción del cliente (un contrato con saldo) y no son promocionales. Solo las campañas promoterias deben ser `marketing`.

---

## 3. Coste estimado para el volumen de Atlas Labs

Supuesto de volumen: **~150 unidades**; recordatorios D12 = a los **5 días** del vencimiento y luego **cada 15 días** mientras haya saldo → hasta ~2 por unidad al mes. Rango pedido: 150-400 mensajes salientes/mes.

| Concepto | Fórmula | Resultado | Fuente |
|---|---|---|---|
| Recargo Twilio por mensaje (saliente) | mensajes × USD 0,005 | 300 msg → **USD 1,50/mes**; 400 msg → **USD 2,00/mes** | Twilio, verificada |
| Recargo Twilio por mensaje (entrante) | respuestas × USD 0,005 | replies de STOP y confirmaciones también se cobran | Twilio, verificada |
| Tarifa Meta plantilla `utility` (Rest of Latin America) | mensajes × tarifa oficial | **no verificado** — leer tarifario antes de presupuestar | Meta, pendiente |
| Coste fijo mensual | — | Twilio: **ninguno indicado** para WhatsApp. 360dialog: **desde EUR 49/mes por número** | Twilio / 360dialog |
| Alquiler del número de WhatsApp | — | **no verificado** en esta sesión | — |
| Cargo por mensaje fallido | USD 0,001 solo en estado "Failed" | marginal | Twilio, verificada |

**Formula total mensual**: `Total = (mensajes_salientes + mensajes_entrantes) × 0,005 USD + mensajes_plantilla × tarifa_Meta_utility + fijos`

Notas de coste que importan más que el total:

- **La tarifa de Meta domina el coste**, no el recargo del intermediario. Por eso la decisión se ha tomado sobre fricción de integración y pruebas, no sobre precio.
- **Moneda**: todo se factura en **USD** (páginas de precios en USD y EUR). La conversión a DOP va por **D22** (`ExchangeRate-API` con fallback manual, `ESPECIFICACION.md:40`). Sin cambio de proveedor.
- **Impuestos**: el tratamiento del ITBIS sobre esta operación **no está verificado** aquí y encaja con **F0-01** (contador). No se afirma nada aquí.

---

## 4. Tabla comparativa

| | **Twilio WhatsApp API** | **Meta Cloud API (directa)** | **360dialog** |
|---|---|---|---|
| Coste fijo mensual | Ninguno indicado para WhatsApp | Ninguno | Desde **EUR 49/mes por número** |
| Recargo por mensaje | **USD 0,005** (entrante o saliente) | Ninguno (solo tarifa de Meta) | Sin recargo sobre tarifas de Meta |
| Coste de fallo | USD 0,001 por mensaje en estado "Failed" | No aplica | No verificado |
| Modelo de facturación | Tarifa propia + tarifa de plantilla de Meta pasada tal cual | Solo tarifa de Meta | Tarifa de Meta |
| Sandbox / prueba | **Sí**, sandbox gratuito, número compartido, sin necesidad de cuenta de WhatsApp Business | Sin sandbox gratuito equivalente; se paga desde el primer mensaje de plantilla | No verificado |
| Plantillas | API de plantillas de Twilio + aprobación de Meta | API de plantillas de Meta | API de plantillas de 360dialog sobre la de Meta |
| Webhook firmado | **`X-Twilio-Signature`**, HMAC-SHA1 con Auth Token, ejemplo de validación en C# | Firma de la app (cabecera de firma de Meta); **no verificado en esta sesión** | **no verificado** en esta sesión |
| Entrantes y STOP | Webhooks de mensajes entrantes; STOP gestionado por la app | Webhooks de mensajes entrantes; STOP gestionado por la app | Webhooks de mensajes entrantes |
| SDK oficial C# | Sí |Cliente HTTP propio | API REST; SDK C# **no verificado** |
| On-premise (Windows Server x64) | HTTPS saliente + endpoint público: sin requisitos especiales | Igual, pero la autenticación por token de Graph API se gestiona a mano | Igual |
| Idempotencia | La implementa la app sobre el `MessageSid` / id de mensaje | La implementa la app sobre el `wamid` de Meta | La implementa la app |

---

## 5. Criterios puntuados (1-5) y justificación

Puntuación: 5 = mejor para este proyecto.

| Criterio | Twilio | Meta directa | 360dialog | Justificación |
|---|---|---|---|---|
| **Precio para el volumen** (150-400/mes) | 4 | 5 | 2 | El recargo de Twilio son ≈ USD 1,50-2,00/mes: irrelevante. Meta directa es la más barata en teoría, pero el ahorro es del mismo orden de magnitud que el coste fijo mensual de 360dialog, que sí es recurrente y evitable. |
| **Facilidad con ASP.NET Core 10** | 5 | 2 | 3 | SDK oficial de C# y helpers de webhook en Twilio. Meta directa exige montar cliente de Graph API, tokens y reintentos a mano. 360dialog es REST y su documentación no se pudo leer aquí. |
| **Webhook firmado e idempotente** | 5 | 4 | 2 | Twilio documenta la firma `X-Twilio-Signature` (HMAC-SHA1, Auth Token) **con ejemplo en C#**, que es literalmente el criterio de aceptación de F4-04. Meta ofrece firma de app pero no se verificó en esta sesión. En 360dialog es el requisito central y no se pudo verificar. |
| **Soporte de plantillas** | 5 | 5 | 4 | Los tres exponen API de plantillas sobre las de Meta; Twilio además centraliza el alta y la aprobación. Empate técnico, ventaja Twilio por la gestión que hace de intermediaria. |
| **Fiabilidad / estabilidad** | 4 | 5 | 3 | Meta es el origen de la plataforma; Twilio es un intermediario grande con estado público y soporte. 360dialog es más pequeño y su documentación inaccesible mete ruido. |
| **Dependencia de un solo proveedor** | 3 | 4 | 2 | Con Twilio se depende de Meta **y** de Twilio. Con 360dialog, de Meta **y** de un intermediario europeo con menos respaldo. Meta directa es la única con dependencia única real. |
| **Coste de cambiar después** | 4 | 3 | 2 | Con la interfaz propia (`IWhatsAppSender`) y el SDK aislado, cambiar es reimplementar un adaptador. Cambiar desde 360dialog o Meta directa a otro es más caro porque no hay sandbox con el que volver a probar. |

**Totales**: Twilio **30/35** · Meta directa **28/35** · 360dialog **18/35**.

**Por qué el precio no pesa en la decisión**: con este volumen, la diferencia anual entre las tres opciones está en el orden de **decenas de dólares**, mientras que un bloqueo de F4-04 por un webhook mal firmado o por plantillas rechazadas cuesta días de trabajo. El criterio decisivo es la capacidad de **probar sin pagar**.

---

## 6. Riesgos de la recomendación

1. **Dependencia de dos partes**: con Twilio se depende de Meta y de Twilio. Si Twilio cambia precios o degrada el servicio, se nota. Mitigación: la interfaz propia y un único punto de sustitución.
2. **Aprobación de Meta fuera de nuestro control**: el alta del emisor y de las plantillas los decide Meta. Es el plazo más largo e incierto del proyecto. Mitigación: empezar el trámite esta semana, en paralelo al desarrollo (plan §9).
3. **El sandbox no es producción**: tiene número compartido, un mensaje cada 3 segundos, solo alcanza a quien se ha unido, muestra el logotipo de Twilio y la sesión expira a los 3 días (fuente oficial, §6). La prueba real de plantillas y firma hay que hacerla contra el emisor de producción.
4. **Recargo por mensaje en las respuestas**: las respuestas de los clientes también se cobran a USD 0,005. Un pico de conversaciones entrantes encarece el mes. Es poco por mensaje, pero conviene vigilance en el informe de costes.
5. **Facturación en USD y variación del tipo**: el gasto real en DOP depende del tipo de cambio (D22). Ninguno de los tres proveedores permite fijar la moneda de las tarifas.
6. **Impuestos**: el tratamiento del ITBIS no está resuelto aquí (F0-01). Puede haber un coste fiscal que ninguno de estos precios incluye.
7. **Tarifa de Meta para República Dominicana sin verificar**: si resultara anormalmente alta, la comparación de precio de §3 cambiaría. No cambia la recomendación, pero sí el presupuesto.

---

## 7. Estado de la verificación de fuentes

| Fuente | Qué se verificó | Resultado |
|---|---|---|
| Twilio, precios de WhatsApp ([twilio.com/en-us/whatsapp/pricing](https://www.twilio.com/en-us/whatsapp/pricing)) | Tarifa por mensaje, recargo, cargo por fallo, gratuidad en ventana de 24 h | Verificado. La página indica **"Pricing current as of September 2026"** |
| Twilio, seguridad de webhooks ([twilio.com/docs/usage/webhooks/webhooks-security](https://www.twilio.com/docs/usage/webhooks/webhooks-security)) | `X-Twilio-Signature`, HMAC-SHA1 con Auth Token, ejemplo de validación en C# | Verificado |
| Twilio, sandbox de WhatsApp ([twilio.com/docs/whatsapp/sandbox](https://www.twilio.com/docs/whatsapp/sandbox)) | Sandbox gratuito, sin cuenta de WhatsApp Business, límites y número compartido | Verificado |
| Meta, precios de la plataforma ([developers.facebook.com/documentation/business-messaging/whatsapp/pricing](https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing)) | Modelo por mensaje desde 1 jul 2025, gratuidades, RD en "Rest of Latin America", actualización trimestral | Verificado, salvo la cifra en USD |
| Meta, WhatsApp Business Platform ([whatsappbusiness.com/products/platform-pricing](https://whatsappbusiness.com/products/platform-pricing/)) | Categorías de mensajes y escalera de volumen | Verificado |
| 360dialog, precios ([360dialog.com/pricing](https://360dialog.com/pricing)) | Desde EUR 49/mes, sin recargo sobre tarifas de Meta | Verificado solo desde la descripción oficial de la página; el desglose por plan se carga por JavaScript y no se pudo leer |
| 360dialog, webhooks ([docs.360dialog.com/docs/webhooks](https://docs.360dialog.com/docs/webhooks)) | Firma de webhook | **No verificado**: la documentación no cargó en esta sesión |
| Requisitos de alta en Meta (empresa verificada, appraisal, certificado de operador) | — | **No verificado**: no se pudo abrir la documentación correspondiente |
| Coste de alquiler del número de WhatsApp | — | **No verificado** |

---

## 8. Requisitos técnicos que hereda el adaptador (F4-04)

- Interfaz propia, sin tipos del SDK en el dominio (D23: "se construye detrás de una interfaz"). Nombre propuesto: `IWhatsAppSender` con `EnviarPlantillaAsync`, `RegistrarPlantillaAsync`, `MarcarOptOutAsync`.
- **Validación de firma antes de procesar nada**: si la firma no valida, responder `401` y escribir la auditoría del intento (criterio de aceptación de F4-04).
- **Idempotencia**: clave única sobre el identificador de mensaje del proveedor en la tabla de envíos, dentro de la misma transacción que la auditoría.
- **Ventana de 24 h**: fuera de ella solo plantillas; los recordatorios de pago son plantilla `utility`.
- **STOP**: marca de opt-out que **excluye** el contacto de envíos automáticos, y el resto de días del cliente (marca "recordatorios pausados").
- **Entrantes**: se guardan como texto y se muestran en modo lectura en la ficha del cliente. El contenido externo nunca se trata como instrucción (`ESPECIFICACION.md:287`).
- **Ningún secreto en logs ni en el repo**: el Auth Token va por variables de entorno; `AGENTS.md` §6 prohíbe secretos y datos personales en logs.

---

## 9. Plan de decisión — qué hacer esta semana

1. **Responsable, día 1**: abrir la cuenta de Twilio y **activar el sandbox de WhatsApp**. No cuesta nada y no exige cuenta de Meta. Con eso F4-04 deja de estar bloqueada para poder **programar y probar** (firmar un request de prueba y rechazarlo a propósito, ver `401` y auditoría).
2. **Responsable, día 1-2**: por la vía de onboarding de Twilio, iniciar el **alta del emisor y el registro del número** de la empresa. Es el trámite más lento y depende de Meta: se empieza ya aunque el desarrollo continúe. Mientras tanto, tener el dominio y el certificado configurados en el servidor (F0-03).
3. **Responsable, día 2**: leer en el **tarifario oficial de Meta** la tarifa en USD de plantilla `utility` para **Rest of Latin America** y añadirla a la §3 de este documento. Sin ese dato no hay presupuesto cerrado. Es el único dato pendiente que bloquea el coste.
4. **Responsable, día 3**: preguntar al contador (**F0-01**) el tratamiento del ITBIS de esta factura y en qué serie se registra.
5. **Equipo, en paralelo**: cerrar la **interfaz `IWhatsAppSender`** y las tres plantillas (vencimiento a 5 días,Recordatorio quincenal, estado de cuenta con PDF adjunto), y dejarlas aprobadas por escrito antes de invertirlas en código.
6. **Decisión de cierre, día 5**: si los pasos 2 y 3 van bien, **F0-07 se marca RESUELTA** en `docs/PLAN.md` y se desbloquean F4-04 y F4-05. Si el alta se rechaza o la tarifa resulta alta, se revisa y se evalúa la opción directa de Meta.

---

## 10. Referencias

- Twilio · Precios de WhatsApp — https://www.twilio.com/en-us/whatsapp/pricing (consultado 2026-10-15; la página indica precios vigentes a septiembre de 2026)
- Twilio · Seguridad de webhooks (firmas) — https://www.twilio.com/docs/usage/webhooks/webhooks-security (consultado 2026-10-15)
- Twilio · Sandbox de WhatsApp — https://www.twilio.com/docs/whatsapp/sandbox (consultado 2026-10-15)
- Meta · Precios de WhatsApp Business Platform — https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing (consultado 2026-10-15)
- WhatsApp Business · Precios de la plataforma — https://whatsappbusiness.com/products/platform-pricing/ (consultado 2026-10-15)
- 360dialog · Precios — https://360dialog.com/pricing (consultado 2026-10-15)
- Requisitos del proyecto: `AGENTS.md` §6, `docs/ESPECIFICACION.md` §3.14 (líneas 248-252) y D22/D23, `docs/PLAN.md` F0-07, F4-04, F4-05