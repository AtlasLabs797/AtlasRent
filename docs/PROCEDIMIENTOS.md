# Procedimientos de trabajo — Atlas Rent

Pasos fijos para las tareas que se repiten. Valen para cualquier persona o agente, con cualquier herramienta. Si tu herramienta tiene atajos (por ejemplo, comandos con barra), deben limitarse a ejecutar estos procedimientos.

---

## 1. Siguiente tarea

1. Lee `AGENTS.md` y `docs/PLAN.md`.
2. Busca una tarea EN CURSO a nombre de quien te lo pide. Si no hay, toma la primera PENDIENTE cuyas dependencias estén HECHAS y que no esté BLOQUEADA. Si la que toca está bloqueada por una pregunta abierta (A#) o por una tarea de Fase 0, dilo y propón otra libre.
3. Crea la rama `tarea/<ID>-<nombre-corto>` desde `main` y marca la tarea EN CURSO en `docs/PLAN.md`.
4. Lee la sección de `docs/ESPECIFICACION.md` que cita la tarea y la parte de `docs/DOC_TECNICA.md` del módulo. Si toca interfaz o PDF, lee también `docs/DESIGN.md`.
5. Presenta en unas 5 líneas qué vas a tocar y qué test cubrirá cada criterio de aceptación. **Espera confirmación antes de escribir código.**

## 2. Cerrar tarea

Entrada: ID de la tarea (p. ej. `F2-03`).

1. Ejecuta la verificación de `AGENTS.md` §8 y las migraciones desde una BD vacía. Si algo falla, no cierres: arréglalo o explica el bloqueo.
2. Para cada criterio de aceptación de la tarea en `docs/PLAN.md`, indica el test que lo demuestra.
3. Revisa el diff buscando: secretos; datos personales o importes en logs; endpoints sin `[Accion]`; `double`/`float` en dinero; conversión de moneda en documentos; SQL sin parámetros; totales accesibles para el empleado; colores o estilos fuera de los tokens del design system; pantallas que no se vean bien en tema oscuro. Cualquier hallazgo de seguridad → §4.
4. Actualiza:
   - `docs/PLAN.md` → HECHA (fecha).
   - `CHANGELOG.md` → entrada FEAT (qué, por qué, verificado, NO verificado, migraciones).
   - `docs/DOC_TECNICA.md` si cambió estructura, BD, endpoints, jobs o versiones.
   - `docs/MANUAL_USUARIO.md` si cambió algo visible (marca ✅).
   - `docs/DESIGN.md` si se creó un patrón visual nuevo.
5. Commit `tipo(modulo): descripcion`. No hagas push sin permiso.
6. Resume en 5 líneas: hecho, pendiente, riesgos y siguiente tarea libre.

## 3. Registrar bug

Entrada: descripción breve del bug.

1. Lee `docs/BUGS.md` y asigna el siguiente ID (BUG-NNN).
2. Escribe primero un test que reproduzca el bug y confirma que falla.
3. Corrige con la solución mínima y ejecuta la verificación de `AGENTS.md` §8.
4. Añade la entrada arriba del registro con todos los campos. La causa raíz explica el porqué, no repite el síntoma.
5. Si hay datos en BD afectados, dilo y propón cómo corregirlos; no los toques sin confirmación.
6. Añade la línea FIX en `CHANGELOG.md`.
7. Si tiene impacto de seguridad, sigue también §4.

## 4. Registrar problema de seguridad

Entrada: descripción breve.

1. Lee `docs/SEGURIDAD.md` y asigna el siguiente ID (SEC-NNN).
2. Evalúa la gravedad con la escala del archivo, teniendo en cuenta que la app está expuesta a internet. Sin secretos ni datos reales en la evidencia.
3. Si se puede corregir en esta sesión: test que demuestre el fallo → corrección → test en verde.
4. Escribe la entrada en la sección C con todos los campos e indica si hubo exposición real.
5. Si queda ABIERTO o es Alta/Crítica, avísalo en la **primera línea** de tu respuesta.
6. Actualiza la casilla del checklist (sección A) si aplica.
7. Añade la línea SEC en `CHANGELOG.md`.

## 5. Revisión de seguridad

Entrada: módulo o "todo". Se hace al cerrar cada fase y antes de producción (FP-01).

Revisa el código del alcance contra cada punto del checklist (sección A) de `docs/SEGURIDAD.md`. Para cada punto: CUMPLE / NO CUMPLE / NO APLICA, con la ruta del archivo que lo demuestra. Busca además:

- endpoints sin `[Accion]` o con comprobación solo en el frontend;
- lecturas por ID sin comprobar alcance (IDOR);
- totales o agregados accesibles para el rol empleado;
- caminos de login que no cuentan fallos para el bloqueo de cuenta o IP, o que revelan si el usuario existe;
- uso de `X-Forwarded-For` sin lista de proxies de confianza;
- contenido externo renderizado como HTML o usado como instrucción;
- secretos o datos personales en código, tests, fixtures o logs;
- paquetes vulnerables (`dotnet list package --vulnerable --include-transitive`, `npm audit`).

No corrijas nada en esta pasada. Registra cada NO CUMPLE con §4 y entrega una tabla priorizada.
