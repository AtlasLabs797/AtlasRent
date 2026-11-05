# F0-02 — Exportar muestras reales de Factusol

Resuelve **F0-02**: obtener las muestras de datos reales de Factusol que hoy nadie tiene documentadas (A5).
Bloquea **FM-01** (importador). También alimenta **FM-02** y **FM-04** (conciliación) y sirve de contraste para **F1-09**, **F1-10**, **F2-05** y **F3-02**.

Es la tarea que más información da de toda la Fase 0 y la única que se hace **una sola vez**: si las muestras salen incompletas, el importador se escribe a ciegas y la conciliación no cuadra.

Los datos que salen de aquí son **reales**: personales, financieros y con documentación de identidad (`AGENTS.md` §6, `docs/SEGURIDAD.md` SEC-004). Ver §5 antes de mover un solo archivo.

---

## 1. Qué exportar

Una línea por entidad. Los nombres de archivo son los sugeridos para `imports/factusol/`.

| # | Archivo sugerido | Entidad | Por qué la hace falta |
|---|---|---|---|
| 01 | `01_clientes.xlsx` | Clientes | Número de Factusol conservado, tipo persona/empresa, documento (cédula, RNC, pasaporte), marcas de agente de retención y de gestión de cobro |
| 02 | `02_contactos.xlsx` | Contactos de cliente | Teléfonos y correos con su etiqueta y sus casillas de envío |
| 03 | `03_inmuebles_unidades.xlsx` | Inmuebles y unidades | Incluye unidad implícita de inmueble alquilado completo, estados, modalidades y tarifas |
| 04 | `04_contratos.xlsx` | Contratos | Datos de cabecera, día de pago, renovación, incrementos, anticipo anotado |
| 05 | `05_facturas_s1_usd.xlsx` | Facturas serie 1 (USD) | Numeración y formato en la serie normal |
| 06 | `06_facturas_s2_dop.xlsx` | Facturas serie 2 (DOP) | Numeración y formato en la serie normal |
| 07 | `07_facturas_s3_fiscal.xlsx` | Facturas serie 3 (fiscal, USD o DOP) | La serie que genera comprobante: la que más campos tiene |
| 08 | `08_facturas_s4_proforma.xlsx` | Facturas serie 4 (proforma) | Proformas y su conversión a factura |
| 09 | `09_facturas_s9_historica.xlsx` | Facturas serie 9 (histórica) | Solo importación. **Ver §6: es el caso difícil (D25)** |
| 10 | `10_comprobantes.xlsx` | Comprobantes fiscales | Correlación factura ↔ comprobante, anulados, rango y vencimiento vigente |
| 11 | `11_cobros.xlsx` | Cobros y asignaciones | Reparto por factura, parciales, moneda recibida, formas de pago |
| 12 | `12_fianzas.xlsx` | Fianzas | Cobro, retención y devolución, con descuentos |
| 13 | `13_retenciones.xlsx` | Retenciones de factura | Solo clientes agente de retención, con estado del comprobante de retención |
| 14 | `14_tipo_cambio.xlsx` | Tipos de cambio | Histórico cubriendo **todas** las fechas de las facturas de muestra |
| 15 | `15_anticipos_reserva.xlsx` | Anticipos de reserva | Son **notas**, nunca cobros (D10). Si no exportables, se documenta a mano |
| 16 | `16_maestros.xlsx` | Maestros de configuración | Series (códigos y nombres reales), formas de pago, cuentas, subempresas, último número usado por serie |

**Las columnas, no solo las filas.** Si Factusol permite elegir columnas, exportar **todas**, también las que hoy no se usan. Descartar columnas en origen es trabajo que no se puede deshacer después.

**El último número usado por serie es imprescindible.** El contador de Atlas Rent arranca en `max(importado) + 1` (especificación §3.7). Si falta ese dato, la primera factura emitida puede repetir un número que ya existe.

---

## 2. Mínimo de filas y variedad de casos que debe cubrir cada export

Mínimo = cantidad mínima aceptable para dar la tarea por buena. Menos que eso no sirve; más, mejor.

| Entidad | Mínimo | Variedad que tiene que estar dentro |
|---|---|---|
| Clientes | 15 | Persona física con cédula · empresa con RNC · cliente con pasaporte · cliente **sin documento o sin RNC** · cliente **agente de retención** · cliente **en gestión de cobro** · cliente con **recordatorios pausados** · cliente con varios contactos (2 teléfonos + 2 correos) · cliente con dirección y con notas largas |
| Contactos | 30 | Teléfono principal y no principal · correo · marcado "usar para WhatsApp" y "usar para email" · contacto sin etiqueta · contacto con número en formato internacional y en local |
| Inmuebles y unidades | 10 inmuebles / 25 unidades | Inmueble con varias unidades · **inmueble alquilado como unidad implícita** · unidad en estado **disponible** · unidad en **mantenimiento** · unidad **fuera de servicio** · unidad que admite **medio día** · unidad que admite **día completo** · unidad con **tarifa en USD** y unidad con **tarifa en DOP** · unidad sin m²/capacidad (campos vacíos) · unidad con medidor y con notas |
| Contratos | 12 | Contrato en **USD** y en **DOP** · **con anticipo anotado** (y contrato sin anticipo) · con **renovación por bloques** (6 meses / 1 año) y con incrementos en % y en importe fijo · distintos **días de pago** · contrato con más de una factura emitida · contrato ya finalizado para ver la liquidación · contrato con **fianza en USD** y otro con **fianza en DOP** · contrato en borrador y en activo |
| Facturas serie 1 (USD) | 10 | Una **pagada** · una **parcial** (cobro menor que el total) · una **vencida** · una **emitida hoy sin cobros** · una con **varias líneas** · una con **descuento** · una con **línea de ITBIS** y otra sin ITBIS · una con **comprobante fiscal** · una **anulada** |
| Facturas serie 2 (DOP) | 10 | El mismo abanico que la serie 1, con al menos una **pagada**, una **parcial** y una **anulada** |
| Facturas serie 3 (fiscal, USD o DOP) | 15 | Una en **USD** y una en **DOP** (moneda libre) · una con **ITBIS por línea** · una con **retención** · una con **cobro en otra moneda** (pago en DOP de factura en USD y al revés) · una con **descuento** · una con **referencia de e-factura** escrita a mano · una con **comprobante asignado** y otra **sin comprobante todavía** · una con **comprobante anulado** · una con varias líneas de tipo distinto (renta, mora, ajuste, servicio, suministro, cargo) · una **modificada después de emitir** |
| Facturas serie 4 (proforma) | 5 | Una en **borrador** · una **aceptada/convertida** en factura (y su factura destino) · una **rechazada** · una con el mismo cliente, unidad y periodo que otra ya facturada, para ver el aviso de duplicado (especificación §3.8) · una con pago en la otra moneda |
| Facturas serie 9 (histórica) | 15 | Facturas de **periodos antiguos** · alguna **parcialmente cobrada** · alguna **sin comprobante asociado** · alguna **anulada** · **mezcla de USD y DOP** · al menos una cuya antigüedad explique por qué existe una serie aparte |
| Comprobantes fiscales | 15 | Numeración **correlativa sin huecos** · algún **anulado** (y su factura) · el **rango vigente** con su fecha de emisión y su **fecha de vencimiento** · comprobantes de facturas de **distintas series**, si es que hay más de una numeradora |
| Cobros | 20 | Cobro **total** de una factura · **parcial** · un cobro que cubre **varias facturas** · un cobro que cubre **factura + fianza** · cobro en **moneda distinta** a la de la factura, con el tipo de cambio aplicado · cobro con **saldo a favor** · cobro **anulado** con su motivo · cada **forma de pago** en uso (transferencia, efectivo, tarjeta, otro) · con **referencia** y con **cuenta receptora** |
| Fianzas | 8 | **Pendiente** · **Parcial** cobrada · **Retenida** · **En liquidación** · **Devuelta completa** · **Devuelta parcial con descuento** (motivo e importe) · fianza en **USD** y en **DOP** · fianza de un contrato ya finalizado que sigue sin devolver |
| Retenciones | 5 | Factura de cliente **agente de retención** con retención aplicada · con **comprobante de retención recibido** · con el **comprobante pendiente** (para ver el indicador "Retención sin comprobante") · retención con base **subtotal** y con base **ITBIS/total** |
| Tipo de cambio | 30 días que cubran todas las fechas de las facturas de muestra (mejor el histórico completo del periodo importado) | Cambios **distintos cada día** · al menos un día con el mismo valor que el anterior · formato exacto que usa Factusol (dígitos y sentido de la paridad) |
| Anticipos de reserva | 3 | Con anticipo **devuelto** y con anticipo **retenido** (anotación, no cobro) · para verificar que el anticipo no aparece en ningún saldo |
| Maestros | — | Series con **código y nombre reales** · **formas de pago** · **cuentas** receptoras · **subempresas** facturadoras · **último número usado por serie** · fecha de la última factura de cada serie |

**Casos que no son opcionales** (si faltan, el importador se escribe con supuestos y luego hay que rehacerlo):
- Una factura pagada, una parcial y una anulada **en cada serie**.
- Un **cobro en USD sobre factura en DOP** y uno **en DOP sobre factura en USD**.
- Una **fianza con descuento** en la devolución.
- Un cliente **sin RNC** y otro **agente de retención**.

---

## 3. Dónde dejar los archivos

Carpeta **`imports/` en la raíz del repositorio**, ya ignorada por git junto con `*.xlsx` y `*.csv` (`.gitignore`). No hay que hacer nada: está excluida.

```
imports/
  factusol/
    01_clientes.xlsx
    02_contactos.xlsx
    ...
    16_maestros.xlsx
    NOTAS.txt          ← una nota con quién exportó, cuándo y a qué base de datos corresponde cada archivo
```

Reglas:

1. **Nunca al repositorio.** Ni por error, ni "solo un momento", ni para probar. Los datos reales de Factusol no entran nunca (especificación §6).
2. **Verificar que sigue fuera** antes de cerrar la tarea:
   ```
   git status --porcelain     # no debe listar nada de imports/
   git check-ignore imports\factusol\01_clientes.xlsx    # debe responder la ruta
   ```
3. **Una entidad por archivo**, nombre numerado. Si Factusol exporta un libro con muchas hojas, vale: se acepta un archivo por entidad con hojas.
4. **Sin renombrar columnas** al exportar. Se mapean en el importador, no en el archivo.
5. **Fechas y números como los emite Factusol.** Si hay que corregir algo, se anota en `NOTAS.txt`; no se edita el dato en silencio. El cuadre al céntimo con Factusol (FM-02) exige que el archivo sea copia fiel.

---

## 4. Nota de privacidad

Estos archivos llevan **datos personales y financieros reales**: nombres, cédulas, RNC, pasaportes, teléfonos, correos, direcciones, titulaciones de contrato e importes de personas. `AGENTS.md` §6 y `docs/SEGURIDAD.md` SEC-004 los tratan como el dato más sensible de la app.

1. **El equipo de desarrollo de Atlas Rent es el único destinatario.** Ni clientes, ni proveedores, ni nadie fuera del proyecto. No reenviar.
2. **Viajan cifrados**: zip con contraseña (contraseña por un canal distinto al del archivo: una llamada o un mensaje) o gestor de archivos cifrado. Nunca correo normal, nunca WhatsApp personal, nunca una carpeta pública de Drive.
3. **La carpeta `imports/` vive en la máquina del responsable del proyecto**, no en equipos compartidos ni en el portátil de un tercero.
4. **Nada de esto entra en el repositorio, ni en PDFs de ejemplo, ni en tests.** Los tests usan datos ficticios (ver F0-06).
5. **Se borran al terminar FM-02 y FM-04** (última conciliación). Antes de borrar, confirmar que existe la última simulación cuadrada y que nadie más necesita los originales. Si hay que conservar el histórico, se conserva **fuera** del repo, cifrado, con fecha de caducidad puesta.
6. Si alguien comparte un archivo por error: cambiar la clave del enlace, avisar a quien lo recibió y anotarlo en `docs/SEGURIDAD.md` como incidente.

---

## 5. Cómo verificar que la exportación está completa

Lista marcable. La tarea no se da por hecha hasta que todo está marcado.

- [ ] Están los **16 archivos** de la tabla §1, con el nombre numerado.
- [ ] Cada archivo abre sin errores y tiene **cabecera de columnas** reconocible (no una captura ni un PDF escaneado).
- [ ] Se exportaron **todas las columnas**, incluidas las que no usa nadie.
- [ ] Clientes: hay al menos uno **sin documento**, uno **agente de retención** y uno **en gestión de cobro**.
- [ ] Unidades: aparecen los tres estados (**disponible**, **mantenimiento**, **fuera de servicio**) y al menos una **unidad implícita** de inmueble completo.
- [ ] Contratos: hay contrato **con anticipo** y **sin anticipo**, y contrato en **USD** y en **DOP**.
- [ ] Facturas: se cumple el mínimo de cada serie de la tabla de la sección 2 (serie 1: 10 · serie 2: 10 · serie 3: 15 · serie 4: 5 · serie 9: 15), y en el conjunto hay **2 anuladas**, **2 con comprobante**, **1 con retención** y **1 con pago en otra moneda**.
- [ ] Facturas: hay **al menos una factura de la serie 3 en USD y otra en DOP** (moneda libre).
- [ ] Proformas: hay al menos una **convertida** en factura, con su factura destino.
- [ ] Serie 9: hay al menos **15 facturas** y son de **periodos distintos** a los de las series 1/2/3.
- [ ] Cobros: hay un cobro **parcial**, uno **multi-factura**, uno **anulado** y al menos uno **en moneda distinta** a la de su factura.
- [ ] Fianzas: hay una **devuelta completa** y una **devuelta parcial con descuento**, con su motivo.
- [ ] Retenciones: hay al menos una con **comprobante recibido** y otra **pendiente**.
- [ ] Tipo de cambio: cubre **todas** las fechas de las facturas de muestra.
- [ ] Maestros: **último número usado por serie** anotado, con la fecha de la última factura de cada serie.
- [ ] Los totales de cada archivo cuadran con lo que muestra la pantalla de Factusol (importes por serie y moneda, al céntimo).
- [ ] `git status --porcelain` **no muestra** nada de `imports/`.
- [ ] El responsable confirma que entendió la nota de privacidad (§4) y cuándo se borran los archivos.

---

## 6. Si Factusol no exporta una entidad

Orden de preferencia:

1. **Lista imprimible o reporte de Factusol** exportado a PDF/HTML y convertido a tabla. Vale para leer, no para importar: hay que transcribirlo.
2. **Capturas de pantalla** de la vista de la entidad, con los datos sensibles tapados. Solo como último recurso para entender el formato; no sirven para el importador.
3. **CSV o XLSX escrito a mano** a partir del listado de Factusol, respectando **exactamente los nombres de columna** que usa el sistema. Es lento pero es lo único que permite probar FM-01.
4. **Consulta directa a la base de datos de Factusol**, si alguien tiene acceso autorizado. Preferible a lo anterior para volumen, pero solo si se hace sobre una **copia**, nunca sobre la base en producción, y si la copia también se trata como dato real (§4).

En cualquiera de los cuatro casos: dejar nota en `NOTAS.txt` de qué se usó y de qué columnas **pudieron** faltar. FM-01 mapea lo que haya; lo que no exista se documenta como tal y se deja en `TODO(A5)`, no se inventa.

---

## 7. La serie 9 (D25) — el caso difícil

La serie 9 es **histórica: solo se importa, no se usa para emitir** (especificación §3.7). Es el riesgo conocido del proyecto (D25, riesgo 2 del plan): hay que comprobar con una prueba real si **duplica deuda de otras series**.

**Exportarla ya, aunque FM-03 sea posterior.** Motivos:

1. FM-03 depende de FM-01, y FM-01 necesita la estructura de la serie 9 para escribir el mapeo. Si la serie 9 no está en las muestras, el importador se escribe sin ella y luego hay que tocarlo.
2. La serie 9 es la que decide si la migración se puede hacer en un solo lote o en dos. Saberlo pronto cambia la planificación de FP-04 y FP-05.
3. Comparar totales de la serie 9 contra las series 1/2/3 sobre los mismos clientes es exactamente lo que hace la prueba de FM-03. Con los datos ya exportados, la prueba es un `diff`; sin ellos, es una segunda petición al responsable dentro de semanas.

Al exportar la serie 9, añade una hoja o un archivo aparte con **el detalle de la deuda por cliente en esa serie**, para poder cruzarlo contra el saldo de las demás. Si Factusol no lo da, se anota como pendiente para FM-03.

---

## Qué no se pudo verificar

- Los nombres reales de los archivos exportados por Factusol, sus columnas exactas y los tipos de dato: dependen de la instalación real de Factusol, no hay acceso desde este repositorio. La lista de la §1 es la mínima necesaria, no el formato final.
- Los mínimos de filas de la §2 son un mínimo razonable para escribir y probar el importador, no un cálculo: si hay menos casos disponibles en la instalación real, hay que ajustar la lista de lo que sí existe en lugar de inventar.
- Qué entidades exporta Factusol de forma nativa y cuáles requieren una de las alternativas de la §6: se sabrá al abrir el sistema.