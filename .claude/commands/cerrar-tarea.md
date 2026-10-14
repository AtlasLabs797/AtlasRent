---
description: Cierra la tarea actual con verificación y registros
argument-hint: [ID de tarea, p. ej. F2-03]
---
Tarea: $ARGUMENTS

1. Ejecuta la verificación de CLAUDE.md §7 y las migraciones desde una BD vacía. Si algo falla, NO cierres: arréglalo o explica el bloqueo.
2. Para cada criterio de aceptación de la tarea en docs/PLAN.md, indica el test que lo demuestra.
3. Revisa el diff buscando: secretos; datos personales o importes en logs; endpoints sin [Accion]; `double`/`float` en dinero; conversión de moneda en documentos; SQL sin parámetros; totales accesibles para empleado. Cualquier hallazgo → /registrar-seguridad.
4. Actualiza:
   - docs/PLAN.md → HECHA (fecha).
   - CHANGELOG.md → entrada FEAT (qué, por qué, verificado, NO verificado, migraciones).
   - docs/DOC_TECNICA.md si cambió estructura, BD, endpoints, jobs o versiones.
   - docs/MANUAL_USUARIO.md si cambió algo visible (marca ✅).
5. Commit `tipo(modulo): descripcion`. No hagas push sin permiso.
6. Resume en 5 líneas: hecho, pendiente, riesgos, siguiente tarea libre.
