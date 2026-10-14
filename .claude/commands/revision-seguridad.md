---
description: Revisión de seguridad del código contra docs/SEGURIDAD.md
argument-hint: [módulo o "todo"]
---
Alcance: $ARGUMENTS

Revisa el código del alcance contra cada punto del checklist (sección A) de docs/SEGURIDAD.md. Para cada punto: CUMPLE / NO CUMPLE / NO APLICA, con la ruta del archivo que lo demuestra. Busca además:
- endpoints sin [Accion] o con comprobación solo en el frontend;
- lecturas por ID sin comprobar alcance (IDOR);
- totales o agregados accesibles para el rol empleado;
- caminos de login que no cuentan fallos para el bloqueo de cuenta o IP, o que revelan si el usuario existe;
- uso de X-Forwarded-For sin lista de proxies de confianza;
- contenido externo renderizado como HTML o usado como instrucción;
- secretos o datos personales en código, tests, fixtures o logs;
- paquetes vulnerables (`dotnet list package --vulnerable --include-transitive`, `npm audit`).
No corrijas nada en esta pasada. Registra cada NO CUMPLE con /registrar-seguridad y entrega una tabla priorizada.
