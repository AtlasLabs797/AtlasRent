---
description: Registra (y corrige si se puede) un bug en docs/BUGS.md y CHANGELOG.md
argument-hint: [descripción breve del bug]
---
Bug: $ARGUMENTS

1. Lee docs/BUGS.md y asigna el siguiente ID (BUG-NNN).
2. Escribe primero un test que reproduzca el bug y confirma que falla.
3. Corrige con la solución mínima; ejecuta la verificación de CLAUDE.md §7.
4. Añade la entrada arriba del registro con todos los campos. La causa raíz explica el porqué, no repite el síntoma.
5. Si hay datos en BD afectados, dilo y propón cómo corregirlos; no los toques sin confirmación.
6. Añade la línea FIX en CHANGELOG.md.
7. Si tiene impacto de seguridad, ejecuta también /registrar-seguridad.
