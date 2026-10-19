# F0-06 — Ejemplos de los PDF actuales por serie

Resuelve **F0-06**: conseguir ejemplos reales de los documentos que hoy genera Factusol (A2).
Bloquea **F4-01** (PDFs con QuestPDF por serie). Alimenta `docs/DESIGN.md` §7 y §8, que hoy tienen el diseño base como *Supuesto*.

No se quiere copiar el PDF viejo: se quiere **extraer el diseño** (estructura, jerarquía, qué va en cabecera, qué va en pie, cómo se muestran las retenciones, el comprobante o un pago en otra moneda). Los datos se cambian por marcadores antes de guardar nada en el repositorio.

---

## 1. Qué PDF pedir

Los tipos de documento que existen según la especificación §3.14: **cotización, contrato, anexos/inventario, factura, proforma, recibo, estado de cuenta**. `docs/DESIGN.md` §7 trabaja los seis primeros; los anexos/inventario están en §3.14 y §3.5/§3.6 y también se necesitan.

| Documento | Por serie | Mínimo de muestras | Notas |
|---|---|---|---|
| **Factura** | Sí: 1, 2, 3 y 4 | 2 por serie (sencilla + completa) | Serie 3 es la fiscal: es la que lleva comprobante, ITBIS y retención. Serie 4 son proformas: pedir también alguna ya **convertida** en factura, para ver cómo queda el par proforma → factura |
| **Proforma** | Serie 4 | 2 | Una sencilla y una con todos los elementos |
| **Recibo de cobro** | No lleva serie propia | 2 | Un cobro simple y uno que cubre varias facturas |
| **Estado de cuenta** | No lleva serie propia | 1–2 | El que hoy se manda al cliente con el resumen de deuda |
| **Cotización** | No lleva serie propia | 2 | Una en borrador y una aceptada |
| **Contrato** | No lleva serie propia | 2 | Uno mensual y uno corto por medio día/día completo; si el contrato tiene anexos, uno con anexos |
| **Anexo / inventario de salida** | No lleva serie propia | 1–2 | Con descripción de elementos, faltantes y el descuento aplicado contra la fianza |

Prioridad si hay que elegir: **facturas de la serie 3** (es la fiscal y la más rica) → contratos → recibo → estado de cuenta → cotización → proformas → anexos.

Serie 9: no se pide muestra de PDF. Es histórica y solo se importa; su factura se puede Exportar desde Factusol, pero el diseño no aporta (ninguna factura nueva llevará esa serie).

**Extra que casi siempre existe y conviene tener**: un documento que ocupe **más de una página** (factura larga o contrato largo). Sirve para decidir si la cabecera se repite, dónde va "Página x de y" y qué pasa con la tabla de líneas partida.

---

## 2. Requisitos de cada muestra

**Mínimo 2 por serie:**

1. **Sencilla.** Una factura normal: un cliente, una línea de renta, sin retención, sin descuento, pago completo. Es el caso que se ve todos los días y el que define el 80% del diseño.
2. **Completa.** Con **todos** los elementos a la vez:
   - comprobante fiscal asignado,
   - ITBIS,
   - **retención** (cliente agente de retención),
   - **pago en otra moneda** (importe recibido en la otra moneda + tipo de cambio aplicado),
   - **descuento**,
   - varias líneas de tipos distintos,
   - referencia de e-factura escrita a mano,
   - número de página si ocupa más de una.

Por serie, el resumen queda:

| Serie | Sencilla | Completa |
|---|---|---|
| 1 · USD | factura USD de una línea, pagada | con comprobante, ITBIS, pago en DOP y descuento |
| 2 · DOP | factura DOP de una línea, pagada | con comprobante, retención, pago en USD y varias líneas |
| 3 · fiscal (moneda libre) | factura fiscal de una línea, pagada | con **todo** el listado anterior; **una en USD y otra en DOP** (la serie admite ambas) |
| 4 · proforma | proforma sencilla | proforma completa + una proforma ya convertida en factura |

Lo que **no** hace falta: una factura de cada cliente, ni 50 facturas, ni datos de casos raros que nunca ocurren. Dos por serie, bien elegidas, es el punto.

---

## 3. Cómo extraer el diseño (y cómo no filtrar datos reales)

El objetivo del PDF guardado en el repositorio es **el diseño**, no el documento. Por eso hay que sustituir los datos **antes** de guardar:

1. Abrir el PDF de muestra.
2. Sustituir **todo dato personal o económico** por marcadores:
   - `{{CLIENTE}}`, `{{DOCUMENTO}}` (cédula, RNC o pasaporte), `{{DIRECCION}}`, `{{TELEFONO}}`, `{{EMAIL}}`
   - `{{UNIDAD}}`, `{{INMUEBLE}}`, `{{RENTA_MENSUAL}}`, `{{TOTAL}}`, `{{PENDIENTE}}`, `{{TIPO_CAMBIO}}`
   - RNC de ejemplo con formato neutro: `RNC-000-00000-00`
   - Fechas: usar fechas futuras o neutras (`{{FECHA}}`), no las reales
3. **Borrar** (no tapar con un rectángulo negro: el texto sigue debajo) todo lo que sea nombre de persona, dirección, teléfono, correo, banco, cuenta, firma, sello y código de barras de cobro.
4. Comprobar el resultado visualmente página por página antes de guardar: si queda un solo dato real, el archivo no entra.
5. Si el PDF original **tiene el diseño incrustado como imagen** (no texto seleccionable), la muestra sirve como referencia visual pero hay que avisarlo: habrá que reconstruir la maqueta a ojo, no se puede leer la estructura.

**Estas muestras van al repositorio y de ahí a GitHub.** Un PDF con un nombre, un RNC y una dirección de una persona real es una filtración de datos, aunque el archivo sea "solo un ejemplo" (`AGENTS.md` §6, `docs/SEGURIDAD.md` SEC-004).

Si hay duda de si un dato es "de ejemplo" o real: es real. Duda hacia fuera.

---

## 4. Dónde se guardan y con qué nombre

**Recomendado: `Atlas Rent Design System/referencias/pdf-factusol/`** (carpeta nueva dentro del design system).

Por qué ahí y no en otro sitio:

- `docs/DESIGN.md` §7 es la especificación de los PDF y §8 dice, literalmente, que el diseño se ajusta con los ejemplos de F0-06. Las muestras son la entrada directa de esa decisión: cuando se retoque el diseño, quien lo toque mira allí primero.
- El design system es lo que **se copia al frontend** (`AGENTS.md` §10, `DESIGN.md` §1). Las muestras no son tokens ni componentes y no deben viajar ahí dentro, pero sí deben estar en el mismo sitio que el resto de diseño.
- No van en `tokens/` ni en `components/` (son fuente del DS, no referencias) ni en `uploads/` (es una carpeta de capturas sueltas, sin criterio).
- No van en `docs/`: ahí está la especificación escrita; un PDF de Factusol es una muestra, no documentación.
- No van en `imports/`: esa carpeta está ignorada por git y contiene datos reales; estas muestras, ya saneadas, sí se versionan.

Alternativa aceptable si prefieres no meter muestras en el design system: `docs/F0/muestras-pdf/`. Perdería la cercanía con `DESIGN.md` §7, pero mantiene todo lo de F0 junto. Si se elige esta, se anota en `DESIGN.md` §7 para que nadie busque las muestras donde no están.

Nombres de archivo:

```
serie-1-usd-sencilla.pdf          serie-2-dop-completa.pdf
serie-1-usd-completa.pdf          serie-3-fiscal-usd-sencilla.pdf
serie-2-dop-sencilla.pdf          serie-3-fiscal-usd-completa.pdf
serie-4-proforma-sencilla.pdf     serie-3-fiscal-dop-completa.pdf
serie-4-proforma-completa.pdf     serie-4-proforma-convertida.pdf
recibo-sencillo.pdf               recibo-multifactura.pdf
estado-cuenta.pdf
cotizacion-borrador.pdf           cotizacion-aceptada.pdf
contrato-mensual.pdf              contrato-corto.pdf
contrato-con-anexo.pdf            anexo-inventario-salida.pdf
factura-multipagina.pdf
```

Y junto a ellos un `README.md` corto con una línea por archivo: qué serie es, qué caso cubre y en qué se fija. Ese índice es lo que hace útil la carpeta; sin él son PDFs huérfanos.

**Nada de los PDF originales de Factusol con datos reales.** Los originales se quedan fuera del repositorio, en la carpeta privada de quien los exporta, y se borran tras extraer las muestras.

---

## 5. Qué se decide con ellas (A2)

`docs/DESIGN.md` §7 fija hoy un diseño base como *Supuesto*. Con estas muestras se decide, entre otras cosas:

1. **Tamaño y márgenes de página.** ¿Carta (8,5 × 11 in) como dice `DESIGN.md` §7, o lo que use hoy Factusol? Si hoy es A4, se documenta la diferencia y se mantiene Carta (es lo decidido) o se cambia de forma explícita.
2. **Cabecera.** Qué va a la izquierda y qué a la derecha, dónde aparece el **número de comprobante**, si el número de factura va en grande o en la línea de detalle.
3. **Comprobante fiscal.** Dónde se imprime, con qué rótulo y si lleva fecha de vencimiento del rango.
4. **Retenciones.** En el bloque de totales como hoy en `DESIGN.md` §7, o como líneas negativas entre las líneas de la factura. Hay que seguir lo que ya usan los clientes.
5. **Pago en otra moneda.** Dónde aparece el importe recibido en la otra moneda y el tipo de cambio aplicado, para que la factura siga siendo de una sola moneda (D7, §3.10.2).
6. **Descuento.** Si va como línea propia, dentro de otra línea o solo en el pie.
7. **Estado "ANULADA".** `DESIGN.md` §7 ya decide marca de agua gris; con la muestra se decide si además lleva sello o rótulo.
8. **Pie.** Condiciones de pago, cuentas bancarias de la serie, "Página x de y", referencia de e-factura externa.
9. **Multipágina.** Si la cabecera se repite, dónde se parte la tabla de líneas.
10. **Tipografías y logotipo.** Qué fuentes se usan hoy y si el logo de la empresa facturadora tiene que reproducirse (`DESIGN.md` §6 y §8 ya lo tienen como pendiente).

Lo que **no** se decide con estas muestras: la lógica fiscal, los porcentajes ni los rangos de comprobante. Eso viene del contador (F0-01).

---

## Qué no se pudo verificar

- Qué documentos genera hoy Factusol exactamente y con qué nombre: depende de la instalación real. La tabla de §1 sale de la especificación §3.14 y `DESIGN.md` §7, no de una inspección del sistema.
- Cuántas muestras reales existen por serie: si en algún caso no hay dos, se aporta una y se anota la carencia.
- Si los PDF actuales son de texto seleccionable o imágenes rasterizadas: condiciona el trabajo de extracción, se sabrá al abrirlos.
- Si la carpeta `Atlas Rent Design System/referencias/` es aceptada por quien mantiene el design system: la ubicación recomendada es una propuesta razonada, no una decisión ya tomada.