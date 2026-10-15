# Fase 0 · Estado y qué falta por hacer

Resumen de la Fase 0 del plan (`docs/PLAN.md`). Los siete preparativos son **tareas de persona, no de código**: no hay una sola línea de `backend/` ni de `frontend/` que dependa de ellos para poder empezar a construir. Lo que sí hacía falta era dejar cada una preparada para que se ejecute sin dudas, y eso es lo que hay en esta carpeta.

Estado a 15/10/2026.

| ID | Tarea | Bloquea | Estado | Documento |
|---|---|---|---|---|
| F0-01 | Datos fiscales del contador | F3-03, F3-04 | Preparado, falta enviar | [01](01-cuestionario-contador.md) |
| F0-02 | Exportar muestras de Factusol | FM-01 | Preparado, falta exportar | [02](02-exportacion-factusol.md) |
| F0-03 | Dominio, DNS y certificado | FP-02 | Preparado, falta registrar el subdominio | [03](03-dominio-dns-certificado.md) |
| F0-04 | Cuenta de Google Drive para backups | F1-12 | Preparado, con **un bloqueo de diseño** | [04](04-google-drive-backups.md) |
| F0-05 | Repositorio, protección de `main`, CI | F1-01 | **Parcial** | [05](05-repositorio-y-ci.md) |
| F0-06 | PDF actuales por serie | F4-01 | Preparado, falta reunirlos | [06](06-pdfs-por-serie.md) |
| F0-07 | Proveedor de WhatsApp | F4-04, F4-05 | Recomendado, falta decidir | [07](07-whatsapp.md) |

Ninguna bloquea el arranque de F1-01, salvo F0-05, que es medio suya.

---

## Lo que hay que hacer, en orden

### 1. F0-01 · Mandar el cuestionario al contador — 15 minutos
El correo está escrito en [01-cuestionario-contador.md](01-cuestionario-contador.md), listo para copiar y pegar. Es lo más urgente del bloque porque la respuesta del contador suele tardar días y **F3-03 y F3-04 no se pueden construir sin ella**. Encárgate hoy.

### 2. F0-02 · Exportar Factusol — 1 hora, y es la más urgente de verdad
[02-exportacion-factusol.md](02-exportacion-factusol.md) lista las 16 entidades, cuántas filas mínimo y qué variedad de casos tiene que incluir cada una. Es la tarea que más información aporta de toda la Fase 0 y la única que se hace una sola vez: si las muestras salen incompletas, el importador se escribe a ciegas y la conciliación no cuadra al céntimo.

Punto de atención: la **serie 9** es histórica y puede duplicar deuda de otras series (D25). Exporta muestras ya, aunque FM-03 sea una tarea posterior, porque cuando llegue el momento de probarlo ya no habrá forma de recuperar ese dato si no se guardó.

Los archivos van a `imports/`, que ya está en `.gitignore` junto con `*.xlsx` y `*.csv`. **Nunca al repositorio.**

### 3. F0-05 · Visibilidad del repositorio y protección de `main` — decidido
GitHub devuelve HTTP 403 al pedir protección de `main`: *"Upgrade to GitHub Pro or make this repository public to enable this feature"*. No hay atajo, la regla es del plan.

**Decisión tomada el 15/10/2026: el repositorio pasa a público.** Eso habilita la protección de `main` y los rulesets sin pagar. Queda pendiente de hacer.

Consecuencias que hay que asumir, porque son reales:

- Todo el código, la especificación y los documentos técnicos serán legibles por cualquiera. `ESPECIFICACION.md` contiene datos del negocio: series de facturación, tipos de cobro, reglas de retención y de occupancy.
- Un repositorio público sigue teniendo `.gitignore` y sigue siendo obligatorio no commitear nunca secretos ni datos reales. Publicar no relaja `AGENTS.md` §6: al revés, lo hace más peligroso, porque un secreto commiteado por accidente puede ser indexado en minutos.
- Si algún día hay que compartir con un proveedor externo, ya no hará falta: el repositorio es público.
- Si en el futuro se decide volver a privado, se perderá la protección de `main` y habría que contratar Pro para recuperarla.

Antes de cambiar la visibilidad hay que confirmar que no hay nada sensible en el historial, no solo en el estado actual.

- **Revisor**: hoy el único colaborador es el dueño. El flujo del proyecto exige que otra persona revise cada PR, y eso hay que seguir haciéndolo por disciplina: en un repositorio público, quien imagine que puede saltarse el proceso tiene menos fricción, no más.
- **Ramas sueltas**: `docs/f0-proyecto-independiente` está fusionada y sigue existiendo; la rama `V-01.00` existe solo en local y nunca se ha subido, pero el `CHANGELOG` la da por creada.
- **Secretos de CI**: no hace falta ninguno todavía, y no hay que crear ninguno "por si acaso".

### 4. F0-04 · Google Drive: decided, el backup lo sube cada persona con su cuenta
Este es el hallazgo importante de la Fase 0. La documentación oficial de Google dice, textualmente: *"Service accounts don't have storage quota and can't own files. Instead, they must upload files and folders into shared drives, or use OAuth 2.0 to upload items on behalf of a human user."*

Consecuencia: una cuenta de servicio de Google Drive **no puede crear archivos** en el Drive de una cuenta personal. Solo puede leerlos. Es decir, la cuenta de servicio se cae por el camino y no hay atajo técnico.

**Decisión tomada el 15/10/2026: la copia de seguridad la sube la persona que usa la aplicación, con su propia cuenta de Google**, mediante OAuth 2.0 en su nombre. Eso resuelve el problema de cuota sin contratar Google Workspace.

Lo que esa decisión implica y hay que tener presente:

- La app no podrá ejecutar el backup sola. El job de las 02:00 documentado en `DOC_TECNICA.md` §8 **ya no es ejecutable tal cual** y habrá que rediseñarlo en F1-12: el backup pasa a dispararse desde la sesión del usuario, no desde un job desatendido.
- Si nadie entra en la aplicación un día, ese día no hay copia. El backup se ata a la actividad real.
- Cada persona tiene su propia cuota y su propia carpeta. Hay que decidir si el destino es una carpeta común por empresa o una por persona, y hay que vigilancear el ritmo de crecimiento, porque las cuotas personales se llenan.
- El consentimiento OAuth hay que renovarlo, y hay que explicar con claridad para qué pide la app acceso a Drive, porque un permiso amplio es exactamente lo que convierte un robo de token en una fuga de datos.
- Si más adelante se quiere un backup desatendido y fiable, la vía es una unidad compartida de Google Workspace con la cuenta de servicio como miembro. Es una decisión de coste que se puede aplazar.

### 5. F0-07 · WhatsApp: pendiente de decidir en equipo
[07-whatsapp.md](07-whatsapp.md) compara Twilio, la API de Meta directa y 360dialog con precios verificados y fecha de consulta. La recomendación técnica es **Twilio** (30/35, frente a 28/35 de la API de Meta directa), y no por el precio: a 150-400 mensajes al mes la diferencia son decenas de dólares al año. La razón es que trae sandbox para probar sin pagar, webhook firmado documentado con ejemplo en C# y SDK oficial.

**Decisión tomada el 15/10/2026: pendiente, se decide con el equipo.** F4-04 y F4-05 siguen bloqueadas.

Lo que sí conviene hacer mientras tanto, porque no depende de la elección final: **iniciar el alta del emisor en Meta**. La aprobación de las plantillas depende de ellos, es el plazo más largo e incierto de todo el bloque y no hay sandbox que lo simule. Por ese camino pasan las tres opciones.

### 6. F0-03 y F0-06 · Cuando haya tiempo
F0-03 es un subdominio del dominio que ya existe, con su registro DNS y su certificado. Está documentado el procedimiento completo, incluido el detalle de que el puerto 80 no se abre y por eso la validación tiene que ir por TLS-ALPN-01 o DNS-01. No bloquea nada hasta FP-02.

F0-06 son los PDF que hoy genera Factusol, saneados con datos ficticios antes de guardarlos. No bloquea nada hasta F4-01.

---

## La CI está en rojo y sigue así

Independiente de lo anterior: la CI del repositorio falla sin ejecutar ningún job. Está registrada como **BUG-001** en `docs/BUGS.md`, con la causa probable y la solución. La corrección está aplicada en esta rama pero **no se ha podido verificar en GitHub**, porque comprobarlo exige subir la rama. Sin esa verificación, F1-01 no puede darse por cumplida (su aceptación es "CI verde").

La corrección pasó además por una revisión independiente, que encontró dos fallos en un primer intento: el detector de `frontend` comprobaba `frontend/package.json` desde dentro de `frontend/`, por lo que nunca habría dado `true` y el job habría salido verde sin ejecutar nada; y el detector de la solución solo buscaba el `.sln` en la raíz, cuando los proyectos van en `backend/`. Los dos están corregidos y simulados con y sin los ficheros esperados.

## Qué NO se ha verificado

- Ninguna de las siete tareas está cerrada: todas requieren de una persona.
- Ninguna cifra fiscal se ha inventado. Formato y rangos del comprobante, porcentajes de ITBIS y de retenciones siguen siendo huecos hasta que responda el contador.
- La CI corregida no se ha ejecutado en GitHub.
- Los precios de WhatsApp están verificados con fuentes oficiales del 15/10/2026, pero pueden cambiar; el documento 07 señala cuál es la cifra que no pudo verificar.
- No se ha verificado que no haya datos sensibles en el historial del repositorio antes de hacerlo público. Es una comprobación obligatoria antes de cambiar la visibilidad.
- El SDK de .NET instalado (10.0.302) y el que fija `DOC_TECNICA.md` §2 (10.0.302) **coinciden**. No hay discrepancia que resolver aquí; queda pendiente solo decidir `rollForward` al escribir `global.json` en F1-01.