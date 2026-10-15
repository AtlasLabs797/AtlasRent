# Plan de ejecución — Atlas Rent

**Base:** proyecto independiente; todo se construye en este repositorio sobre el stack de `docs/DOC_TECNICA.md` §2.

Varias personas, avance progresivo, **sin fechas fijas** (D24). El plan fija **orden y dependencias**; cada persona toma la siguiente tarea libre cuyas dependencias estén HECHAS.

Estados: `PENDIENTE` · `EN CURSO (persona)` · `BLOQUEADA (motivo)` · `HECHA (dd/mm/aaaa)`

## Cómo se trabaja una tarea
1. Rama `tarea/<ID>-<nombre-corto>` y arranque según `docs/PROCEDIMIENTOS.md` §1.
2. Quien ejecuta la tarea (persona o agente) lee `AGENTS.md`, la tarea y la sección citada de la especificación; marca EN CURSO; propone en 5 líneas qué hará y espera confirmación.
3. Implementa con tests; ejecuta la verificación de `AGENTS.md` §8.
4. Cierre según `docs/PROCEDIMIENTOS.md` §2: CHANGELOG, PLAN, documentación.
5. PR a `main`. Otra persona revisa el diff y prueba la aceptación. Si falla → `BUGS.md`.
6. Al terminar cada fase: revisión de seguridad (`docs/PROCEDIMIENTOS.md` §5) sobre lo construido.

Una tarea debe caber en una sesión y un PR revisable. Si no cabe, se divide antes de empezar y se anota aquí.

---

## Fase 0 · Preparación (humana, en paralelo con F1)
| ID | Tarea | Bloquea | Estado |
|---|---|---|---|
| F0-01 | Contador: formato/rangos del comprobante (A3), % de ITBIS y retenciones (A4), confirmar no modificar importes de facturas con comprobante | F3-03, F3-04 | PENDIENTE |
| F0-02 | Exportar muestras reales de Factusol: clientes, unidades, contratos, facturas series 1/2/3/4/9, cobros, fianzas (A5) | FM-01 | PENDIENTE |
| F0-03 | Dominio público y DNS para el certificado (A6) | FP-02 | PENDIENTE |
| F0-04 | Cuenta/carpeta de Google Drive para backups de Atlas Rent | F1-12 | PENDIENTE |
| F0-05 | Repositorio privado `AtlasLabs797/AtlasRent`, protección de `main`, revisores, secretos de CI | F1-01 | PENDIENTE |
| F0-06 | Ejemplos de PDF actuales por serie (A2) | F4-01 | PENDIENTE |
| F0-07 | Elegir proveedor de WhatsApp (A1) | F4-04, F4-05 | PENDIENTE |

## Fase 1 · Fundación
| ID | Tarea | Spec | Aceptación | Depende | Estado |
|---|---|---|---|---|---|
| F1-01 | **Esqueleto backend:** solución `backend/` (Api, Watchdog, Tests) con las versiones de DOC_TECNICA §2 (`global.json`, `.csproj`), `Program.cs` con el pipeline base, Serilog, health check, conexión a PostgreSQL de desarrollo, CI completo (build, tests, auditoría de paquetes) | 4 | CI verde; `dotnet run` arranca y `/health` responde; versiones = DOC_TECNICA §2 | — | HECHA (15/10/2026) |
| F1-02 | Tipos de dominio: `Dinero` (decimal + moneda), redondeo, fechas de negocio, zona horaria | 5 | Tests de redondeo y meses 28/29/30/31 | F1-01 | HECHA (15/10/2026) |
| F1-03 | BD base: roles (`db/scripts`), migración inicial, `btree_gist`/`pgcrypto`, auditoría append-only + firma e integridad, RLS base | 5, 7 | Test: UPDATE/DELETE/TRUNCATE en `auditoria` con rol app fallan; verificación de integridad detecta una fila alterada | F1-01 | HECHA (15/10/2026) |
| F1-04 | Auth (JWT + cookies HttpOnly + refresco rotatorio + CSRF + BCrypt): comprobación HaveIBeenPwned, inactividad 30 min / máx. 12 h, validación de sesión en BD para revocación inmediata | 7 | Usuario desactivado → 401 en la siguiente petición | F1-03 | HECHA (15/10/2026) |
| F1-05 | Rate limiting + bloqueo de cuenta (5 → 30 min) + **bloqueo de IP escalado** (nuevo), lista blanca, aviso de IP nueva, pantalla admin de desbloqueo | 7 | 5 fallos bloquean cuenta; N fallos desde una IP la bloquean aunque cambie de usuario; IP en lista blanca no se bloquea | F1-04 | HECHA (15/10/2026) — la pantalla admin de desbloqueo queda pendiente de F1-07 |
| F1-06 | MFA/TOTP: obligatorio para admin, interruptor desactivado para gerente, códigos de recuperación, break-glass | 7 | Admin sin MFA configurado solo puede configurarlo | F1-04 | HECHA (15/10/2026) — el interruptor de gerente y la regla `es_admin` pasan a `IPolicyService` en F1-07 |
| F1-07 | `IPolicyService` + roles + permisos por usuario (techo del rol) + auditoría de denegaciones | 2 | Matriz de permisos de la spec convertida en tests de API | F1-04 | HECHA (15/10/2026) |
| F1-08 | Configuración: series, numerador de comprobante, ITBIS, retenciones, formas de pago, cuentas, subempresas, tolerancias, modo recordatorios | 3.7, 3.9 | Sin cifras fiscales en código; todo editable por admin | F1-07 | HECHA (15/10/2026) — el guard es por línea: falla ante una constante fiscal con nombre propio, no ante una de nombre innocuo |
| F1-09 | Clientes, contactos, prospectos; cifrado AES-256-GCM del documento + índice ciego de búsqueda; secuencia de número | 3.2 | Números concurrentes sin duplicar; documento cifrado en BD y visible completo en pantalla | F1-07 | HECHA (15/10/2026) |
| F1-10 | Inmuebles, unidades, tarifas, documentos PDF (subida validada, descarga con permiso) | 3.1 | Empleado recibe 403 en documentos de propiedad | F1-07 | HECHA (15/10/2026) |
| F1-11 | Layout, navegación y menú sobre `AppShell` del design system; selector de tema Claro / Oscuro / Sistema (D31); textos centralizados | DESIGN | Revisión visual en claro y oscuro; preferencia recordada; sin parpadeo al cargar | F1-04, F1-13 | HECHA (15/10/2026) |
| F1-12 | Backups (Hangfire + Google Drive cifrado): BD + documentos, conservar 4; Watchdog y sistema de actualizaciones | 7 | Backup generado, cifrado, rotado a 4 y subido; fallo → alerta; actualización con copia previa | F1-03, F0-04 | **PARCIAL** (15/10/2026) — hecho: copia cifrada, verificación de integridad, rotación a 4, Watchdog con copia previa obligatoria y disparo diario a las 02:00 AST. **Falta solo la subida a Google Drive, que depende de F0-04** |
| F1-13 | **Esqueleto frontend:** `frontend/` (Vite + React + TS) con las versiones de DOC_TECNICA §2; design system integrado según DESIGN.md §1 (tokens, componentes portados a TSX, `Icon` con lucide-react, fuentes autoalojadas); mecanismo de tema claro/oscuro (DOC_TECNICA §3.1); el backend sirve el build | 4, DESIGN | `lint`, `typecheck`, `test` y `build` en verde; página de muestra de componentes correcta en claro y oscuro | F1-01 | HECHA (15/10/2026) |

## Fase 2 · Operación de alquiler
| ID | Tarea | Spec | Aceptación | Depende | Estado |
|---|---|---|---|---|---|
| F2-01 | Disponibilidad: exclusión por rangos + capacidad de medio día (trigger + bloqueo por unidad) | 3.4, 5 | Test concurrente: dos reservas solapadas → una falla; 2 medios días OK, el 3.º falla | F1-10 | PENDIENTE |
| F2-02 | Cotizaciones: CRUD, estados, PDF básico, crear reserva/contrato desde Aceptada | 3.3 | Estados según D18 | F1-09 | PENDIENTE |
| F2-03 | Reservas mensuales y cortas, anticipo como nota, estados, alerta 7 días, conflicto | 3.4 | Anticipo no aparece en ningún saldo ni informe económico | F2-01 | PENDIENTE |
| F2-04 | Calendario de disponibilidad | 3.1 | Muestra contratos, reservas, medios días y mantenimiento | F2-03 | PENDIENTE |
| F2-05 | Contratos: flujo, aprobación (solicitud que caduca si cambia), versionado | 3.5 | Empleado no puede aprobar ni por API | F2-03 | PENDIENTE |
| F2-06 | Renovaciones automáticas e incrementos (job Hangfire diario idempotente) | 3.5 | Ejecutar el job dos veces no duplica | F2-05 | PENDIENTE |
| F2-07 | Plantillas de contrato y anexos; PDF de contrato | 3.5 | Plantilla congelada al aprobar | F2-05 | PENDIENTE |
| F2-08 | Inventario base, inventario de contrato, revisión de salida | 3.6 | No se liquida sin revisión si hay inventario | F2-05 | PENDIENTE |
| F2-09 | Baja programada, fin y liquidación | 3.5 | La unidad queda libre desde la fecha efectiva | F2-08 | PENDIENTE |
| F2-10 | Alertas y bandeja de aprobaciones | 3.16 | Alertas sin duplicar | F2-05 | PENDIENTE |

## Fase 3 · Finanzas
| ID | Tarea | Spec | Aceptación | Depende | Estado |
|---|---|---|---|---|---|
| F3-01 | Tipo de cambio (ExchangeRate-API): histórico, fallback manual | 3.12 | Fallo de API → alerta y carga manual | F1-08 | PENDIENTE |
| F3-02 | Facturas: borrador, líneas, ITBIS por línea, emisión con número transaccional, cambio congelado | 3.8 | 50 emisiones concurrentes: números únicos y sin huecos por fallo | F1-08, F3-01 | PENDIENTE |
| F3-03 | Comprobante fiscal: asignación en emisión, alertas, bloqueo por agotado/vencido, referencia e-factura manual | 3.7 | 50 emisiones concurrentes en serie 3: comprobantes únicos; agotado → emisión bloqueada sin consumir número de factura | F3-02, F0-01 | PENDIENTE |
| F3-04 | Retenciones y comprobante de retención | 3.9 | cobro + retención = total ⇒ Pagada; indicador sin comprobante | F3-02, F0-01 | PENDIENTE |
| F3-05 | Facturación periódica con simulación y prorrateo; factura de reserva corta (cobro antes) | 3.8, 3.4 | Doble ejecución no duplica; prorrateo correcto en meses 28-31 | F3-02, F2-06 | PENDIENTE |
| F3-06 | Cobros: registro, asignación FIFO editable, saldo a favor, corrección con motivo, recibo | 3.10.1 | 200 casos aleatorios: ningún saldo descuadra (property test) | F3-02 | PENDIENTE |
| F3-07 | Pago en otra moneda + tolerancia + ajuste de diferencia de cambio | 3.10.2 | USD 1.000 pagada con DOP: factura Pagada, ambos importes guardados, sin duplicar factura | F3-06 | PENDIENTE |
| F3-08 | Modificar, anular (con anulación parcial de cobros multi-factura), duplicar, proforma → factura, rehacer en otra moneda | 3.8 | Cobro de 2 facturas, anular 1: la otra sigue pagada | F3-06 | PENDIENTE |
| F3-09 | Fianzas: cobro, devolución, descuentos | 3.11 | Devolución parcial exige motivo e importe | F3-06 | PENDIENTE |
| F3-10 | Vista única de cuentas y cobros con panel de cobro | 3.13 | Empleado no recibe totales por API | F3-07, F3-09 | PENDIENTE |

## Fase M · Migración (en paralelo desde F3-06)
| ID | Tarea | Aceptación | Depende | Estado |
|---|---|---|---|---|
| FM-01 | Importador: zona temporal, mapeo, validaciones, informe de errores | Rechaza filas inválidas con motivo | F0-02, F3-06 | PENDIENTE |
| FM-02 | Simulación 1 y conciliación por serie y moneda | Cuadra al céntimo con Factusol | FM-01 | PENDIENTE |
| FM-03 | Prueba de serie 9 con datos reales y decisión de tratamiento (D25) | Sin deuda duplicada | FM-02 | PENDIENTE |
| FM-04 | Simulación 2 con datos actualizados | Cuadra al céntimo | FM-03 | PENDIENTE |

## Fase 4 · Documentos y mensajería
| ID | Tarea | Aceptación | Depende | Estado |
|---|---|---|---|---|
| F4-01 | PDFs con QuestPDF por serie (factura, proforma, recibo, estado de cuenta, cotización, contrato) | Textos de usuario nunca interpretados; diseño según F0-06 | F3-08 | PENDIENTE |
| F4-02 | Email con MailKit (SMTP configurable), plantillas por idioma, tabla `envio`, copia del PDF enviado | Copia exacta guardada; marca "modificado tras envío" | F4-01 | PENDIENTE |
| F4-03 | Envío masivo con resumen, cola, reintentos (email) | Reintenta solo fallidos; sin duplicados | F4-02 | PENDIENTE |
| F4-04 | WhatsApp: adaptador del proveedor elegido, plantillas, webhook firmado e idempotente, entrantes, STOP | Firma inválida → 401 auditado | F0-07, F4-02 | BLOQUEADA (A1) |
| F4-05 | Recordatorios automático/manual, pausa por cliente, estado de cuenta adjunto | Se detienen al pagar; modo manual no envía nada sin confirmación | F4-03 (+F4-04 para WhatsApp) | PENDIENTE |

## Fase 5 · Informes
| ID | Tarea | Aceptación | Depende | Estado |
|---|---|---|---|---|
| F5-01 | Dashboard e informes con filtros, consolidado USD | Test automático: cuadra con la vista única | F3-10 | PENDIENTE |
| F5-02 | Exportación Excel/CSV con ClosedXML (gerente/admin), neutralización de fórmulas | Celdas que empiezan por `= + - @` neutralizadas | F5-01 | PENDIENTE |

## Fase P · Puesta en producción
| ID | Tarea | Aceptación | Estado |
|---|---|---|---|
| FP-01 | Revisión de seguridad completa (`docs/PROCEDIMIENTOS.md` §5) + cierre de ABIERTOS en SEGURIDAD.md | Sin abiertos Altos/Críticos | PENDIENTE |
| FP-02 | Instalación con `scripts/install.ps1` en `C:\AtlasRent`: BD y roles en la instancia PostgreSQL, servicios, certificado o Caddy, firewall solo 443; convivencia con otras aplicaciones si comparten servidor | Checklist DOC_TECNICA §9 completo | PENDIENTE |
| FP-03 | Prueba de restauración de backup en otra máquina | Restaurado y verificado; fecha anotada en SEGURIDAD.md | PENDIENTE |
| FP-04 | Piloto: un ciclo mensual completo en paralelo con Factusol | Mismas facturas y saldos | PENDIENTE |
| FP-05 | Corte: cierre de Factusol, importación final, conciliación | Saldos y fianzas conciliados antes de operar | PENDIENTE |

## Fase 6 · IA (después del corte)
Integración de IA con proveedores configurables (se eligen al empezar la fase) y agentes externos con tokens delimitados. F6-01 solo lectura · F6-02 propuestas con aprobación · F6-03 asistente en la app. Detalle en ESPECIFICACION 3.18.

---

## Riesgos
1. **Exposición a internet** sin VPN y gerente sin MFA: depende de que F1-05 (bloqueos) y F1-04 (sesión) estén bien hechos. Revisión de seguridad obligatoria antes de abrir el puerto.
2. Calidad de los datos de Factusol (serie 9).
3. Proveedor de WhatsApp sin decidir: bloquea F4-04.
4. Seguridad construida desde cero: auth, sesiones, MFA y bloqueos no parten de código ya probado. Tests de API por rol en cada tarea y revisión de seguridad obligatoria al cerrar la Fase 1.
5. Varias personas en paralelo: conflictos en migraciones de EF. Regla: una migración por PR, rebasar antes de fusionar.
6. Ampliar alcance: cada idea nueva se añade como tarea aquí, no dentro de otra.
