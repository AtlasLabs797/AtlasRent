# BUGS.md — Registro de bugs

Cada bug detectado o corregido se registra aquí. Lo más reciente arriba. No se borran entradas.

**Gravedad:** Crítica (dinero, numeración, datos perdidos o expuestos) · Alta (función principal rota) · Media (hay forma de rodearlo) · Baja (cosmético).
**Estado:** ABIERTO · EN CURSO · CORREGIDO · NO SE CORRIGE (motivo)

## Plantilla
```
### BUG-000 · Título corto
- Fecha detectado: dd/mm/aaaa · Fecha corregido: dd/mm/aaaa
- Gravedad: · Estado: · Módulo:
- Detectado por: (persona / test / agente / usuario)
- Síntoma: qué se ve.
- Cómo reproducir: pasos mínimos.
- Causa raíz: por qué pasaba (no el síntoma).
- Solución: qué se cambió y por qué así.
- Test que lo cubre: ruta del test.
- Commit/PR:
- Datos afectados: ¿hay registros en BD que corregir? ¿cómo se corrigieron?
```

## Registro

### BUG-001 · La CI termina en rojo sin ejecutar ningún job
- Fecha detectado: 15/10/2026 · Fecha corregido: —
- Gravedad: Media · Estado: EN CURSO · Módulo: CI (`.github/workflows/ci.yml`)
- Detectado por: agente, revisando el estado del repositorio en F0-05
- Síntoma: los tres últimos disparos de `CI` (14/10/2026, `main` y `docs/f0-proyecto-independiente`) terminan en `failure` con `total_count: 0` jobs y `created_at` = `updated_at`.
- Cómo reproducir: hacer push de cualquier commit a una rama con el workflow anterior y abrir la pestaña Actions.
- Causa raíz probable: el `if` de nivel de job con `hashFiles(...)` en los jobs `backend` y `frontend`. La tabla de disponibilidad de contextos de GitHub no admite `hashFiles` en `jobs.<job_id>.if`, y eso invalidaría el workflow completo, lo que explica por qué tampoco se creaba el job `secretos`, que no usa expresiones. Se descartó que fuera un problema de codificación (sin BOM ni tabuladores) o de YAML (`yaml.safe_load` lo parsea sin errores), y que fuera de permisos o de saldo de Actions (`enabled: true` y AtlasBalance sí ejecuta). **La causa no está demostrada en ejecución**: solo lo confirmará un disparo con el workflow nuevo.
- Solución: se eliminó el `if` de nivel de job. Los tres jobs se crean siempre; cada uno empieza con un paso de detección que escribe `existe=true|false` en `$GITHUB_OUTPUT`, y los pasos de build y test se condicionan con `if: steps.detecta.outputs.existe == 'true'`. El detector de la solución busca el `.sln`/`.slnx` en todo el subárbol y no solo en la raíz, porque los proyectos van en `backend/`; el de `frontend` fija `working-directory: .` para que no herede el `frontend` del job.
- Test que lo cubre: no hay suite todavía. Comprobación manual: que el disparo cree jobs. Verificado solo en local (YAML parsea y los dos scripts bash se simulan con y sin los ficheros esperados).
- Commit/PR: rama `tarea/F0-preparacion`, pendiente de push.
- Datos afectados: ninguno.
