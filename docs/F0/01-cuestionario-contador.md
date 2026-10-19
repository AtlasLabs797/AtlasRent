# F0-01 — Cuestionario para el contador

Resuelve **F0-01**: formato y rangos del comprobante fiscal (A3), porcentajes de ITBIS y de retenciones (A4), y confirmación de que no se modificarán importes de facturas que ya tienen comprobante.
Bloquea **F3-03** (comprobante fiscal) y **F3-04** (retenciones). También es insumo de **F1-08** (pantalla de configuración).

Las cifras fiscales van **siempre en configuración, nunca en código** (`AGENTS.md` §6). Este documento no inventa ni una sola: son huecos para que los rellene el contador.

---

## 1. Carta lista para enviar

> **Asunto:** Atlas Rent — datos fiscales para la configuración (comprobantes, ITBIS, retenciones)
>
> Estimado/a {{CONTADOR}}:
>
> Estamos construyendo **Atlas Rent**, el sistema que sustituye a Factusol para el alquiler de nuestras ~150 unidades. La aplicación emite, numera y comprueba facturas por sí misma, y necesita conocer los datos fiscales que tú manejas hoy en Factusol. No hay ninguna intención de cambiar el criterio contable ni fiscal: queremos replicarlo exactamente.
>
> Te pido el detalle de estos puntos. Puedes responder por correo con una tabla o devolver este mismo correo con la tabla del punto 3 rellena.
>
> ---
>
> **1. Series de facturación**
>
> En Atlas Rent manejamos cinco series. Necesito saber, para cada una, si **genera comprobante fiscal** y con qué numeración:
>
> | Serie | Qué factura representa | ¿Genera comprobante fiscal? | Moneda |
> |---|---|---|---|
> | 1 | Factura normal en USD | __________ | USD |
> | 2 | Factura normal en DOP | __________ | DOP |
> | 3 | Factura fiscal (con comprobante) | __________ | USD o DOP indistintamente |
> | 4 | Proforma / factura proforma | __________ | USD o DOP indistintamente |
> | 9 | Factura histórica (solo migración de datos viejos) | __________ | __________ |
>
> Si el formato o el rango **dependen de la serie** (no solo de un numerador único), indícalo explícitamente.
>
> **2. Numerador de comprobante fiscal**
>
> - Tipo o prefijo del comprobante (ejemplo del formato real, con ceros o sin ceros): `________________________`
> - Rango autorizado en uso: desde `__________` hasta `__________`
> - ¿Cuántos comprobantes se emiten por factura? Uno a uno: __________
> - **Fecha de emisión** del rango en uso: `____/____/________`
> - **Fecha de vencimiento** del rango en uso: `____/____/________`
> - Umbral de aviso que quieres (números restantes a partir del cual avisar): `__________`
> - ¿Existe más de un rango activo a la vez o uno detrás de otro? __________
> - Cuando un comprobante se anula, ¿se reutiliza ese número? __________
>
> El rango **caduca**: si se agota o llega su fecha de vencimiento, la aplicación bloquea la emisión y avisa. Por eso necesito ambas fechas del rango que se está usando ahora, no solo los números.
>
> **3. ITBIS**
>
> - Tipos de ITBIS que usamos (nombre y porcentaje):
>
>   | Nombre | % | ¿Se aplica por línea o a la factura? | Valor por defecto |
>   |---|---|---|---|
>   | __________ | ____ | __________ | __________ |
>   | __________ | ____ | __________ | __________ |
>
> - ¿El ITBIS puede no aplicarse a alguna línea? __________
> - ¿Hay redondeo propio del ITBIS distinto del redondeo general a 2 decimales? __________
>
> **4. Retenciones**
>
> Solo se aplican a clientes marcados como **agente de retención**.
>
> | Nombre | % | Base de cálculo (subtotal / ITBIS / total) | Valor por defecto |
> |---|---|---|---|
> | __________ | ____ | __________ | __________ |
> | __________ | ____ | __________ | __________ |
>
> - ¿Quién emite el comprobante de retención y en cuánto tiempo debemos tenerlo? __________
> - ¿El comprobante de retención tiene numeración propia con rango y vencimiento como el fiscal? __________
>
> **5. Facturas que ya tienen comprobante — Confirmación**
>
> Nuestra regla propuesta es la siguiente y quiero tu visto bueno por escrito:
>
> > Si una factura **ya tiene comprobante fiscal asignado**, **no se le pueden cambiar los importes** (ni conceptos que los alteren). Para corregir importes se **anula** esa factura (su número y su comprobante quedan anulados y no se reutilizan) y se **emite una factura nueva** con los importes correctos y un comprobante nuevo.
>
> ¿Correcto? __________
> Si no, dinos qué se permite hacer y qué consecuencias fiscales tiene. __________
>
> **6. Preguntas adicionales (no bloquean, pero evitan rehacer)**
>
> - Tolerancia de descuadre por tipo de cambio al pagar en otra moneda: ¿con cuál trabajarías? __________
> - Referencias de e-factura externa que hoy se anotan a mano: ¿hay alguna convención interna? __________
> - ¿Algún otro rango o numeración que dependa de ti y que no esté en la lista?
>
> ---
>
> Nada de esto se escribe en el código: entra en una pantalla de configuración editable por el administrador, así que se puede corregir en caliente si algún día cambia un porcentaje o se renueva un rango.
>
> Gracias.

---

## 2. Tabla de respuestas (la rellena quien corresponda)

Una fila por dato. `________` = pendiente. Nada de esto se inventa: si no se sabe, se deja en blanco y se anota quién lo trae.

| # | Dato | Valor | Fuente / quién responde | Dónde se usa en Atlas Rent |
|---|---|---|---|---|
| 1 | Serie 1: ¿genera comprobante? | ________ | ________ | F1-08 configuración de series |
| 2 | Serie 2: ¿genera comprobante? | ________ | ________ | F1-08 configuración de series |
| 3 | Serie 3: ¿genera comprobante? | ________ | ________ | F1-08 · F3-03 emisión |
| 4 | Serie 4: ¿genera comprobante? | ________ | ________ | F1-08 configuración de series |
| 5 | Serie 9: ¿genera comprobante? | ________ | ________ | FM-03 prueba de serie 9 |
| 6 | Tipo/prefijo del comprobante | ________ | ________ | F1-08 numerador de comprobante |
| 7 | Rango autorizado: desde | ________ | Contador | F1-08 · F3-03 |
| 8 | Rango autorizado: hasta | ________ | Contador | F1-08 · F3-03 |
| 9 | Fecha de emisión del rango en uso | ________ | Contador | F3-03 alerta y bloqueo |
| 10 | Fecha de vencimiento del rango en uso | ________ | Contador | F3-03: vencido = emisión bloqueada |
| 11 | Umbral de alerta de números restantes | ________ | ________ | F1-08 · F3-03 |
| 12 | Nº de comprobantes por factura (1:1 o N:1) | ________ | ________ | F3-03 asignación |
| 13 | ITBIS: nombre y % | ________ | ________ | F1-08 · F3-02 ITBIS por línea |
| 14 | Retención: nombre, % y base | ________ | ________ | F1-08 · F3-04 |
| 15 | Confirmación: factura con comprobante no cambia importes | Sí / No → nota: ________ | Contador | F3-02/F3-08 modificar y anular |
| 16 | Tolerancia de descuadre por cambio de moneda | ________ | ________ | F3-07 (valor por defecto USD 1,00 / DOP 50,00 es *Supuesto*) |

---

## 3. Notas para quien rellena esto

1. **Las series no son intercambiables.** Serie 1 es USD, serie 2 es DOP, serie 3 es la fiscal de comprobante y la 4 es proforma; las 3 y 4 admiten cualquiera de las dos monedas (D26). Serie 9 es histórica y solo se importa, no se usa para emitir (especificación §3.7).
2. **Un comprobante por factura, no un pool.** El numerador va 1:1 con la serie fiscal y se asigna en la misma transacción que el número de factura; si la emisión falla, no se consume número ni comprobante (`docs/DOC_TECNICA.md` §4.3). Por eso hace falta saber si el formato depende de la serie.
3. **El vencimiento es bloqueante, no informativo.** Con el rango vencido o agotado, la app lanza la emisión entera hacia atrás y avisa. Si nadie mira el aviso, la empresa se queda sin poder facturar. Merece la pena pactar con el contador una alerta con antelación.
4. **Comprobante anulado no se reutiliza** (D28): se marca anulado y el número queda retirado.
5. **Confirmación por escrito.** El punto 5 del cuestionario necesita respuesta afirmativa o matizada, no un "ok" de chat: de esa regla depende el comportamiento de "modificar factura" y "anular factura".
6. **Nada de esto va al código.** Si una respuesta llega tarde o cambia, se corrige en configuración (`F1-08`), no tocando el repositorio.

---

## Qué no se pudo verificar

- Cuáles son los valores reales de formato, rangos, fechas de vencimiento, porcentajes de ITBIS y de retenciones: no hay fuente interna y no se inventan. Huecos `________` hasta que responda el contador.
- Si el numerador de comprobante es único o por serie: la especificación (§3.7) lo define 1:1 con la serie fiscal, pero el estado actual en Factusol no se pudo comprobar desde el repositorio.
- Si existe comprobante de retención con numeración propia: no está descrito en la especificación y se pregunta en el punto 4.
- Plazo y responsable de emitir el comprobante de retención: dependencia externa, sin dato.