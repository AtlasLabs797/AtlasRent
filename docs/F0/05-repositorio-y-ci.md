# F0-05 · Repositorio, protección de `main` y CI

Qué resuelve: dejar el repositorio `AtlasLabs797/AtlasRent` preparado para que entre código en F1-01.
Qué bloquea: F1-01 (esqueleto backend) y, con ella, toda la Fase 1.
Estado: **hecho a medias**. El repositorio existe y es privado; la protección de `main` no se puede activar con el plan actual y el CI está en rojo.

Fecha de verificación: 15/10/2026. Comandos usados: `gh api repos/AtlasLabs797/AtlasRent`, `gh api .../branches/main/protection`, `gh api .../collaborators`, `gh api .../actions/secrets`, `gh api .../actions/runs`, `gh run view`, `git ls-remote --heads origin`.

---

## 1. Estado verificado

| Requisito de F0-05 | Estado | Evidencia |
|---|---|---|
| Repositorio `AtlasLabs797/AtlasRent` | **HECHO** | Creado 14/10/2026; pasado a **público** el 15/10/2026 |
| Rama por defecto `main` | **HECHO** | `default_branch: main` |
| Protección de `main` | **HECHO** | Activa el 15/10/2026: 1 aprobación, revisiones obsoletas se descartan, sin force push, sin borrado, historia lineal |
| Reglas de repositorio (rulesets) | **NO NECESARIO** | La protección clásica ya cubre lo que pedía el plan |
| Revisor con acceso | **PENDIENTE** | El único colaborador es `AtlasLabs797` (admin) |
| Lectura de `AtlasBalance` para quien use Claude Code | **YA CUMPLIDO** | `AtlasLabs797/AtlasBalance` es **público**; no hace falta conceder acceso |
| Secretos de CI | **NINGUNO NECESARIO TODAVÍA** | La CI actual solo usa `GITHUB_TOKEN`, que GitHub genera. No hay builds ni despliegues |
| CI en verde | **ROJA, sin verificar** | Ver §3 |

**Advertencia sobre `enforce_admins`.** Está en **false** a propósito. Si estuviera en true, el dueño sería el único que puede abrir PR y no podría aprobarlo él mismo, así que **nadie podría fusionar nunca nada**. Se dejará en true en cuanto se añada al menos un revisor. Esa es la pieza que falta y la que de verdad importa: la protección sin un segundo par de ojos es un trámite.

Sobre los secretos de CI: hoy no hace falta ninguno. Cuando exista F1-01 solo habrá build y tests; Testcontainers levanta su propio PostgreSQL, así que tampoco hace falta una base de datos de servicio. El primer secreto real llegará con el firmado de paquetes de release (F1-13 o posterior). **No crees secretos "por si acaso": un secreto sin uso es una credencial que se puede filtrar sin que nadie lo note.**

---

## 2. Decisión pendiente: plan de GitHub

La protección de `main` no está disponible en repositorios privados con el plan Free. Hay tres caminos:

| Opción | Coste | Qué aporta | Veredicto |
|---|---|---|---|
| **Repositorio público** | 0 | Lo mismo que Pro, sin pagar | **ELEGIDO el 15/10/2026** |
| Subir a GitHub Pro | ~4 USD/usuario/mes | Protección de `main`, rulesets, Actions ilimitadas en repos privados | Descartada por coste |
| Quedarse en Free y privado | 0 | Nada nuevo | Descartada |

**Aviso, porque es una decisión con consecuencias y no solo de coste:** con el repositorio público, `ESPECIFICACION.md` queda legible para cualquiera, y contiene datos del negocio: series de facturación, tipos de cobro, reglas de retención, ocupación y precios de referencia. Publicar tampoco relaja `AGENTS.md` §6; al contrario, un secreto commiteado por error puede ser indexado en minutos. Antes de cambiar la visibilidad hay que comprobar que no hay nada sensible en el historial, no solo en el estado actual.

Lo que se acepta a cambio: la protección de `main` y los rulesets, que en plan privado solo se pagando.

Cuando el repositorio sea público, activar en GitHub estos valores:
- Regla sobre `main`: requerir al menos 1 aprobación antes de fusionar.
- Prohibir push directo a `main` (salvo a los que ya pueden saltarse la regla).
- Exigir que las comprobaciones de estado estén al día antes de fusionar, con `CI / Secretos` como comprobación requerida.
- Borrar la rama automáticamente al fusionar (`delete_branch_on_merge`, hoy desactivado).

---

## 3. Incidencia: la CI falla sin ejecutar ningún job

**Síntoma.** Los tres últimos disparos de `CI` (14/10/2026) terminan en `failure` con `total_count: 0` jobs, `created_at` y `updated_at` en el mismo segundo, y `rerequestable: false`. GitHub muestra *"This run likely failed because of a workflow file issue"*.

**Lo que se descartó.** El archivo no tiene BOM, ni tabuladores, ni caracteres inválidos; `yaml.safe_load` lo parsea sin errores y la estructura resultante (`on`, `permissions`, `jobs` con `secretos`, `backend`, `frontend`) es correcta. No es un problema de codificación ni de YAML mal formado. La API de repositorio responde `{"enabled":true,"allowed_actions":"all"}`, o sea que Actions está activo. Tampoco es un problema de saldo de minutos: el repositorio `AtlasBalance` sí tuvo ejecuciones exitosas.

**Causa raíz, confirmada en la documentación.** El `if` de nivel de job con `hashFiles(...)`. La tabla de disponibilidad de contextos de GitHub es explícita: en `jobs.<job_id>.if` solo se admiten `always`, `cancelled`, `success` y `failure`; `hashFiles` está permitido en `steps.if` pero **no** en el `if` de nivel de job ([contexts](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts)). Cuando esa expresión no se puede evaluar, GitHub invalida el workflow entero, y por eso tampoco llegaba a crear el job `secretos`, que no usa ninguna expresión.

Y se descartó que fuera un problema de estructura: validado contra el esquema oficial de GitHub Actions (`json.schemastore.org/github-workflow.json`), **el archivo antiguo da 0 errores de esquema, igual que el nuevo**. Si el rechazo fuera de esquema, el nuevo también fallaría. El fallo era de evaluación en ejecución.

**Solución aplicada.** Se eliminó el `if` de nivel de job. Ahora los tres jobs se crean siempre y cada paso que necesita código se protege con un paso de detección previo que escribe `existe=true|false` en `$GITHUB_OUTPUT`, y los pasos siguientes se condicionan con `if: steps.detecta.outputs.existe == 'true'`. Es una forma más robusta y además es más legible en la interfaz: se ve que la comprobación se ejecutó y por qué se saltó, en vez de que el job aparece verde sin explicar nada.

Dos detalles que una revisión independiente encontró en el primer intento y que importan:

- El detector de `frontend` comprobaba `frontend/package.json`, pero el job declara `working-directory: frontend`, que GitHub aplica a **todos** sus pasos. Es decir, comprobaba `frontend/frontend/package.json`, que no existe jamás. El job habría salido **verde sin ejecutar nada** en cuanto existiera `frontend/`. El paso detector ahora fija `working-directory: .`.
- El detector de la solución buscaba solo en la raíz, pero `DOC_TECNICA.md` §3 sitúa los proyectos en `backend/`. Ahora busca en todo el subárbol.

Ventaja adicional: con el patrón anterior, cuando existiera la solución y el `dotnet restore --locked-mode` fallara, el log no decía nada de por qué estaba vacío.

**Verificado en local, no en GitHub.** Tres pruebas:

1. **Esquema oficial**: validación contra `json.schemastore.org/github-workflow.json` con `jsonschema` (Draft 7). El archivo nuevo da **0 errores**. Como control, el archivo antiguo también da 0: eso descarta que el fallo fuera de esquema y lo sitúa en la evaluación de expresiones.
2. **Lógica de los detectores**: los dos scripts bash simulados en seis escenarios. Repo vacío → ambos `false`. `.sln` en la raíz → `true`. `.sln` anidada en `backend/` → `true` (con el glob de solo raíz daba `false`, que era el segundo defecto). `frontend/package.json` presente y ejecutando desde la raíz → `true`. Desde `cwd=frontend`, `test -f frontend/package.json` → `false` y `test -f package.json` → `true`, que demuestra exactamente por qué el paso detector necesita `working-directory: .`.
3. **Regla de contextos**: contrastada con la tabla de GitHub citada arriba.

**Lo que queda sin verificar.** La CI no se ha ejecutado en GitHub. El workflow solo dispara con `pull_request` o con `push` a `main`, y la decisión tomada el 15/10/2026 es **no abrir PR**: la rama `tarea/F0-preparacion` queda subida y sin PR. Mientras sea así, no habrá forma de observar un disparo, y `F1-01` no puede darse por cumplida porque su aceptación es "CI verde". Abrir el PR es una línea de orden: `gh pr create --base main --head tarea/F0-preparacion`. Con la protección activa, el PR no se fusionará solo, así que no hay riesgo de que entre código sin que lo veas.

---

## 4. Observaciones de higiene del repositorio

No bloquean nada, pero conviene limpiarlas:

- La rama `docs/f0-proyecto-independiente` ya está fusionada y sigue existiendo en `origin`.
- La rama `V-01.00` existe **solo en local**: nunca se ha subido. El `CHANGELOG` del 15/10/2026 la da por creada y deja el PR pendiente. Si se quiere conservar, hay que subirla; si era solo una prueba, se puede borrar.
- `delete_branch_on_merge` está desactivado, por lo que las ramas fusionadas se acumulan.

---

## 5. Qué NO se verificó

- Que la CI nueva pase en GitHub. La rama está subida, pero el workflow solo dispara con `pull_request` o con `push` a `main`, así que hace falta un PR para verlo.
- Que los pasos `dotnet` y `npm` estén bien escritos: no hay `backend/` ni `frontend/` todavía, así que se activarán en F1-01 y F1-13. Solo se validó la sintaxis del workflow y la lógica de los detectores.
- Si `gitleaks-action@v2` necesita clave de licencia: en cuentas personales no la pide, pero en una organización sí. Si el repositorio cambia de dueño, habría que revisarlo.
- Las suites de comprobación de las apps **Claude** y **Cursor** están en `queued` desde el 14/10/2026 y nunca producen resultado. Si se quiere usar esos revisores automáticos, hay que instalarlos o autorizarlos correctamente; ahora mismo no aportan nada.

---

## 6. Lista de comprobación para cerrar F0-05

- [x] Repositorio con rama `main` por defecto. *(hecho; ahora público)*
- [x] Lectura de `AtlasBalance` disponible sin conceder permisos. *(era público)*
- [x] Plan de GitHub decidido. *(público)*
- [x] Protección de `main`: 1 aprobación, revisiones obsoletas se descartan, sin force push, sin borrado, historia lineal. *(activa; falta `enforce_admins` cuando haya revisor)*
- [ ] Revisor con acceso de escritura añadido como colaborador, y entonces `enforce_admins: true`.
- [ ] PR abierto para que la CI dispare, y disparo con jobs en verde. *(decisión del 15/10/2026: no abrir PR por ahora)*
- [ ] Añadir la comprobación `CI / Secretos` como obligatoria, ya existente, cuando la CI sea verde.
- [ ] Rama `V-01.00` subida o borrada, según la decisión sobre la versión.
- [ ] Decidir qué hacer con las ramas ya fusionadas.