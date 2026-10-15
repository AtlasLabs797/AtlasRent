# F0-05 · Repositorio, protección de `main` y CI

Qué resuelve: dejar el repositorio `AtlasLabs797/AtlasRent` preparado para que entre código en F1-01.
Qué bloquea: F1-01 (esqueleto backend) y, con ella, toda la Fase 1.
Estado: **hecho a medias**. El repositorio existe y es privado; la protección de `main` no se puede activar con el plan actual y el CI está en rojo.

Fecha de verificación: 15/10/2026. Comandos usados: `gh api repos/AtlasLabs797/AtlasRent`, `gh api .../branches/main/protection`, `gh api .../collaborators`, `gh api .../actions/secrets`, `gh api .../actions/runs`, `gh run view`, `git ls-remote --heads origin`.

---

## 1. Estado verificado

| Requisito de F0-05 | Estado | Evidencia |
|---|---|---|
| Repositorio privado `AtlasLabs797/AtlasRent` | **HECHO** | `private: true`, creado 14/10/2026 |
| Rama por defecto `main` | **HECHO** | `default_branch: main` |
| Protección de `main` | **BLOQUEADO** | GitHub devuelve HTTP 403: *"Upgrade to GitHub Pro or make this repository public to enable this feature"* |
| Reglas de repositorio (rulesets) | **BLOQUEADO** | Mismo 403; los rulesets también exigen plan de pago |
| Revisor con acceso | **PENDIENTE** | El único colaborador es `AtlasLabs797` (admin). `PLAN.md` exige que "otra persona revisa" cada PR |
| Lectura de `AtlasBalance` para quien use Claude Code | **YA CUMPLIDO** | `AtlasLabs797/AtlasBalance` es **público**; no hace falta conceder acceso |
| Secretos de CI | **NINGUNO NECESARIO TODAVÍA** | La CI actual solo usa `GITHUB_TOKEN`, que GitHub genera. No hay builds ni despliegues |
| CI en verde | **ROJA** | Ver §3 |

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

**Causa probable.** El `if` de nivel de job con `hashFiles(...)` en `backend` y `frontend`. Cuando GitHub no puede evaluar una expresión en esa posición, invalida el workflow entero, y por eso tampoco llega a crear el job `secretos`, que no usa ninguna expresión.

**Solución aplicada en esta rama.** Se eliminó el `if` de nivel de job. Ahora los tres jobs se crean siempre y cada paso que necesita código se protege con un paso de detección previo que escribe `existe=true|false` en `$GITHUB_OUTPUT`, y los pasos siguientes se condicionan con `if: steps.detecta.outputs.existe == 'true'`. Es una forma más robusta y además es más legible en la interfaz: se ve que la comprobación se ejecutó y por qué se saltó, en vez de que el job aparece verde sin explicar nada.

Ventaja adicional: con el patrón anterior, cuando existiera la solución y el `dotnet restore --locked-mode` fallara, el log no decía nada de por qué estaba vacío.

**Pendiente de verificar.** El archivo está validado localmente (YAML parsea, los dos scripts bash devuelven el valor esperado en un directorio sin `.sln`), pero **no se ha ejecutado en GitHub**. Hace falta hacer push de la rama y comprobar que los jobs aparecen. Sin ese push no se puede afirmar que la CI esté verde, y `F1-01` exige "CI verde" como criterio de aceptación.

---

## 4. Observaciones de higiene del repositorio

No bloquean nada, pero conviene limpiarlas:

- La rama `docs/f0-proyecto-independiente` ya está fusionada y sigue existiendo en `origin`.
- La rama `V-01.00` existe **solo en local**: nunca se ha subido. El `CHANGELOG` del 15/10/2026 la da por creada y deja el PR pendiente. Si se quiere conservar, hay que subirla; si era solo una prueba, se puede borrar.
- `delete_branch_on_merge` está desactivado, por lo que las ramas fusionadas se acumulan.

---

## 5. Qué NO se verificó

- Que la CI nueva pase en GitHub: requiere push de la rama y observación del disparo.
- Que los pasos `dotnet` y `npm` estén bien escritos: no hay `backend/` ni `frontend/` todavía, así que se activarán en F1-01 y F1-13. Solo se validó la sintaxis del workflow.
- Si `gitleaks-action@v2` necesita clave de licencia: en cuentas personales no la pide, pero en una organización sí. Si el repositorio cambia de dueño, habría que revisarlo.
- Las suites de comprobación de las apps **Claude** y **Cursor** están en `queued` desde el 14/10/2026 y nunca producen resultado. Si se quiere usar esos revisores automáticos, hay que instalarlos o autorizarlos correctamente; ahora mismo no aportan nada.

---

## 6. Lista de comprobación para cerrar F0-05

- [ ] Repo privado con rama `main` por defecto. *(hecho)*
- [x] Lectura de `AtlasBalance` disponible sin conceder permisos. *(era público)*
- [ ] Plan de GitHub decidido (Free o Pro).
- [ ] Si es Pro: protección de `main` con 1 aprobación, sin push directo y con `CI` como comprobación requerida.
- [ ] Revisor con acceso de escritura añadido como colaborador.
- [ ] Rama con la CI corregida subida y disparo con jobs en verde.
- [ ] Rama `V-01.00` subida o borrada, según la decisión sobre la versión.