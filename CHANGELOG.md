# CHANGELOG — Trazabilidad

Una entrada por tarea, decisión, bug o incidencia de seguridad. Lo más reciente arriba. No se reescriben entradas pasadas; si algo estaba mal, se añade una entrada nueva que lo corrige.

Formato:
```
## [dd/mm/aaaa hh:mm] TIPO · ID-tarea · Título · (persona)
- Qué: cambios concretos (archivos/módulos).
- Por qué: motivo o referencia (spec §, D#, BUG-, SEC-, A#).
- Verificado: comandos/tests ejecutados y resultado. Qué NO se verificó.
- Migraciones: sí/no (nombre).
- Riesgo / pendiente:
- Commit/PR:
```
TIPO: FEAT · FIX · SEC · DOCS · MIGR · CONFIG · DECISION

---

## [15/10/2026 02:40] FIX · F0-05 · La CI fallaba sin ejecutar ningún job (BUG-001) (agente)
- Qué: `.github/workflows/ci.yml` reescrito. Se eliminó el `if` de nivel de job con `hashFiles(...)` de los jobs `backend` y `frontend`. Ahora los tres jobs se crean siempre y cada uno empieza con un paso de detección que escribe `existe=true|false` en `$GITHUB_OUTPUT`; los pasos de build y test se condicionan con `if: steps.detecta.outputs.existe == 'true'`. Registrado como BUG-001 en `docs/BUGS.md`.
- Por qué: los tres últimos disparos terminaban en `failure` con `total_count: 0` jobs, `created_at` = `updated_at` y `rerequestable: false`, que es la firma de un workflow rechazado en la validación de GitHub. Se descartó que fuera problema de codificación (sin BOM ni tabuladores) o de YAML (`yaml.safe_load` lo parsea sin errores) y que fuera de permisos o saldo de Actions (`enabled: true` y AtlasBalance sí ejecuta). El `if` de nivel de job era el único constructo sin validar empíricamente, y explica por qué tampoco se creaba el job `secretos`, que no usa expresiones.
- Verificado: `yaml.safe_load` del archivo nuevo (3 jobs, 7/9 pasos, sin `if` de nivel de job) y los dos scripts bash de detección ejecutados en local (`existe=false` en un directorio sin `.sln` ni `frontend/package.json`). **NO verificado en GitHub**: no se ha hecho push de la rama, así que no se puede afirmar que la CI esté verde.
- Migraciones: no.
- Riesgo / pendiente: subir `tarea/F0-preparacion` y comprobar que aparecen jobs. F1-01 exige "CI verde" como aceptación.
- Commit/PR: rama `tarea/F0-preparacion`, pendiente de push y PR.

## [15/10/2026 02:40] DOCS · F0 · Paquete de preparación de la Fase 0 (agente)
- Qué: se crea `docs/F0/` con siete documentos: `00-estado.md` (índice y orden de ejecución), `01-cuestionario-contador.md`, `02-exportacion-factusol.md`, `03-dominio-dns-certificado.md`, `04-google-drive-backups.md`, `05-repositorio-y-ci.md`, `06-pdfs-por-serie.md`, `07-whatsapp.md`. `docs/PLAN.md` §Fase 0 pasa las siete tareas de PENDIENTE a EN CURSO con su estado real. `AGENTS.md` §3 añade la fila del paquete.
- Por qué: las siete tareas de F0 son humanas (contador, datos reales, dominio, cuenta de Drive, PDFs, decisión de proveedor). Ninguna se cierra desde el repositorio, pero cada una se puede dejar preparada para ejecutarse sin ambigüedad. Ninguna bloquea el arranque de F1-01 salvo F0-05.
- Verificado: referencias cruzadas de cada documento comprobadas contra `ESPECIFICACION.md`, `DOC_TECNICA.md`, `DESIGN.md`, `SEGURIDAD.md` y `PLAN.md` (catálogo de series 1 USD / 2 DOP / 3 fiscal / 4 proforma / 9 histórica confirmado en la especificación; `DESIGN.md` §7 y §8 y `SEC-004` existen). Escaneo de los siete archivos en busca de correos, RNC, claves y caracteres fuera del alfabeto latino: sin resultados. **NO verificado**: ninguna tarea está cerrada; los huecos fiscales siguen vacíos a propósito; la afirmación de Google sobre cuota de cuentas de servicio se comprobó en la documentación oficial (ver F0-04).
- Migraciones: no.
- Riesgo / pendiente: dos hallazgos que cambian decisiones. (1) Google Drive: una cuenta de servicio no tiene cuota ni puede ser propietaria de archivos, así que el backup necesita una unidad compartida de Workspace o OAuth en nombre de una persona; hay que decidirlo antes de F1-12. (2) GitHub Free no permite protección de rama en repos privados (HTTP 403); Publicar el repositorio queda descartado. Además, las suites de las apps Claude y Cursor están en `queued` desde el 14/10 y no producen resultado.
- Commit/PR: rama `tarea/F0-preparacion`, pendiente de push y PR.

---

## [14/10/2026 23:59] DECISION · F0 · Proyecto independiente, instrucciones neutras y modo oscuro (spec v3.2)
- Qué:
  - **Sin Atlas Balance:** eliminada toda reutilización y dependencia de Atlas Balance en `docs/ESPECIFICACION.md` (v3.2; D1, D2, D20, D30, párrafo de reutilización, 3.18, §4, §5, §5.1, §7), `docs/DOC_TECNICA.md`, `docs/PLAN.md`, `docs/SEGURIDAD.md`, CI, `config/variables-entorno.example` y `docker-compose.dev.yml`. Donde había "mecanismo de Balance" queda la decisión concreta: concurrencia con `xmin`, auditoría con hash encadenado, RLS con variable de sesión fijada por interceptor de EF Core, formato de paginación, refresco de sesión en Axios, validación con `JsonUnmappedMemberHandling.Disallow`.
  - **Versiones propias** en DOC_TECNICA §2, consultadas en npm y NuGet hoy (React 19.3.1, TypeScript 6.0.3, Vite 8.3.4, EF Core 10.0.13, SDK 10.0.302…). FluentAssertions → AwesomeAssertions (FA ≥ 8 es comercial). QuestPDF: criterio de licencia explícito (Community solo con facturación < 1 M USD).
  - **Plan:** F1-01 pasa a ser el esqueleto backend sin depender de F0-05; nueva F1-13 (esqueleto frontend + design system + temas); F1-11 incluye el selector de tema; el riesgo 4 pasa a ser "seguridad construida desde cero".
  - **Instrucciones neutras:** `AGENTS.md` reescrito sin referencias a modelos ni herramientas concretas (subagentes descritos por capacidad, no por nombre). Los flujos de los comandos `/siguiente`, `/cerrar-tarea`, `/registrar-bug`, `/registrar-seguridad` y `/revision-seguridad` pasan a `docs/PROCEDIMIENTOS.md`; `.claude/commands/` quedan como atajos de una línea. `CLAUDE.md` solo importa `AGENTS.md`. Menciones a "Claude" en PLAN, BUGS, SEGURIDAD y DOC_TECNICA → "agente".
  - **Modo oscuro (D31):** `Atlas Rent Design System/tokens/themes.css` (nuevo) con paridad completa, basado en el tema oscuro de la familia Atlas Labs; tokens `--danger-solid`, `--ai-solid` y `--ai-soft-hover` para fondos con texto blanco (aplicados en `components.css`, `components-app.css`, `ApprovalCard.jsx`, `ChatPanel.jsx`); `styles.css` importa `themes.css`. Mecanismo `data-theme` en DESIGN §3.1 y DOC_TECNICA §3.1.
  - **Diseño:** `docs/DESIGN.md` con integración en Vite (fuentes con `@fontsource`, `--font-sans` admite "Geist Sans"), temas, diseño base de PDF [Supuesto A2] y pendientes actualizados; nota en el `DESIGN.md` del DS remitiendo a `docs/DESIGN.md`; `DatePicker.prompt.md` creado.
  - **Repo:** `AGENTS.md` (secciones de diseño en `docs/DESIGN.md` y skills en `C:\Users\usuario\Documents\Skills`), `README.md` en español, `.gitignore` con rutas reales (`frontend/dist/`, `backend/**/wwwroot/`, `*.tsbuildinfo`, `logs/`, `*.crt`, `*.cer`) y `.claude/settings.local.json` fuera del repo.
- Por qué: decisión del responsable (14/10/2026): Atlas Rent es un proyecto propio, varios agentes de distintos proveedores trabajarán en él y se quiere modo oscuro.
- Verificado: sin menciones a Atlas Balance fuera de entradas antiguas del CHANGELOG; copias de `ESPECIFICACION.md` idénticas; contraste WCAG de los tokens oscuros calculado; página de prueba con el CSS del DS revisada a ojo en claro y oscuro (botones, badges, tabla, KPI, alerta, toast, campo). `.env.dev` sigue ignorado. NO verificado: los componentes React del DS en oscuro (se comprueba al portarlos en F1-13); `_ds_bundle.js` y las fichas `.card.html` son artefactos generados y no se han regenerado.
- Migraciones: no.
- Riesgo / pendiente: licencia de QuestPDF según facturación de la empresa; logo real; ajustar PDF con los ejemplos de F0-06.
- Commit/PR: sin commit.

## [14/10/2026 22:45] CONFIG · F0-05 · Entorno de desarrollo inicial
- Qué: carpeta enlazada con `AtlasLabs797/AtlasRent` (rama main); `AGENTS.md` redactado (estaba vacío); PostgreSQL de desarrollo en Docker con roles `rent_*` y base `atlas_rent`; `dotnet-ef` actualizado a 10.0.13.
- Por qué: el puerto 5432 lo ocupa el PostgreSQL nativo de Atlas Balance, así que `docker-compose.dev.yml` publica en **5433** (en desarrollo usar Port=5433 en las cadenas de conexión).
- Verificado: login como `rent_app` y extensiones `btree_gist`/`pgcrypto` presentes. `03_auditoria_append_only.sql` no aplicado (la tabla llega en F1-03).
- Pendiente: el SDK instalado es 10.0.302 y la spec indica 10.0.112; en F1-01 `global.json` debe usar rollForward o igualarse a Balance.

## [14/10/2026 14:50] DECISION · F0 · Stack alineado con Atlas Balance V-03.01 (spec v3.1)
- Qué: stack, versiones, estructura de carpetas, pruebas, CI, despliegue y diseño igualados a Atlas Balance V-03.01 para copiar su código: React 19.2.8, TS 5.6.3, Vite 8.0.16, React Router 8.3.0, Zustand 5.0.14, TanStack Query 5, Axios 1.16, Recharts 3, Lucide React; ASP.NET Core 10 (SDK 10.0.112), EF Core 10.0.12, Npgsql 10.0.3, EFCore.NamingConventions, Hangfire 1.8, Serilog 10, MailKit 4.17, ClosedXML 0.105; xUnit v3, Testcontainers, Playwright, ESLint 10; PostgreSQL ≥16 con RLS; scripts PowerShell, ZIP x64, actualizaciones por GitHub Releases; backups cifrados a Google Drive.
- Eliminado: TanStack Table, Radix UI, zod, react-i18next, FluentValidation, Otp.NET/QRCoder como paquetes propios (se usa lo de Balance), Material Symbols, CSS Modules, tabla de cola del Watchdog, carpeta externa de backup (A7 resuelta con Google Drive).
- Añadido: nombre Atlas Rent (D29, supuesto), instancia PostgreSQL compartida con Balance si existe (D30), reglas de reutilización en CLAUDE.md §2, tabla de piezas reutilizadas en DOC_TECNICA §3.1. Única dependencia nueva: QuestPDF.
- Archivos: CLAUDE.md, docs/ESPECIFICACION.md, PLAN.md, DESIGN.md, DOC_TECNICA.md, SEGURIDAD.md, db/scripts/*.sql (roles rent_*), config/variables-entorno.example, docker-compose.dev.yml (postgres:16-alpine), CI, .claude/.
- Por qué: petición del responsable; stack de Atlas Balance aportado el 14/10/2026.
- Verificado: coherencia cruzada entre documentos. Sin código aún.

## [14/10/2026 14:30] DECISION · F0 · Especificación v3.0 y paquete regenerado
- Qué: especificación reescrita (v3.0) integrando las decisiones D1-D28; regenerados CLAUDE.md, AGENTS.md, PLAN.md, DESIGN.md, DOC_TECNICA.md, MANUAL_USUARIO.md, SEGURIDAD.md, BUGS.md, configuración de Claude Code, CI, scripts SQL (`db/scripts`), `config/variables-entorno.example`.
- Por qué: respuestas del responsable del 14/10/2026:
  - Stack .NET 10 + PostgreSQL 18 + React, igual que Gestión de Caja y Atlas Connect; despliegue nativo en Windows Server sin Docker (D1, D2).
  - Acceso por web sin VPN con bloqueo de cuenta e IP; MFA solo admin (D3, D4; riesgos SEC-R01, SEC-R02 aceptados).
  - Documento en una sola moneda; pago en otra moneda mediante cobro con cambio aplicado, sustituyendo la duplicación de facturas (D7, D8).
  - Anular factura anula solo la parte de cobro asignada a esa factura (D6).
  - Anticipo como nota; alquiler corto por medio día / día completo cobrado antes; recordatorios automáticos o manuales; cotizaciones sin vencida; permiso de envío de cotizaciones por empleado; documentos de identidad sin enmascarar pero cifrados; backups: 4 diarios (D10-D20).
  - WhatsApp pendiente de proveedor (D23); sin fechas fijas (D24).
- Eliminado respecto a v2.x: Docker/Linux en servidor, NestJS, pg-boss, Caddy, VPN, MCP público, alquiler por horas, anticipo como cobro, vigencia de cotizaciones, enmascarado de documentos, Playwright para PDF.
- Verificado: coherencia cruzada entre documentos revisada. Sin código aún.

## [14/10/2026 07:45] DECISION · F0 · Comprobante normal, sin e-factura (spec v2.2)
- Qué: se revierte la propuesta e-CF de la v2.1. Vuelve el numerador de comprobante fiscal propio por serie (rango, vencimiento, alertas, bloqueo). E-factura fuera de la app: campo de texto manual opcional `referencia_fiscal_externa`. Eliminados P18, estados "Pendiente e-CF"/"e-CF rechazado", tabla `ecf_referencia` y módulo `ecf`.
- Archivos: CLAUDE.md, docs/ESPECIFICACION.md (0.0 #16, §1, §2, §3, 4.7, 4.16, 5.4, 6, 9.6, 10, 12, 13), PLAN.md (F0-01, F3-03, riesgos), DESIGN.md, MANUAL_USUARIO.md, DOC_TECNICA.md, SEGURIDAD.md.
- Por qué: decisión del responsable.
- Riesgo: validez del comprobante normal tras el 15/11/2026 (Ley 32-23) la confirma el contador.

## [13/10/2026 23:30] DOCS · F0 · Paquete inicial para Claude Code
- Qué: CLAUDE.md, docs/PLAN.md, docs/DESIGN.md, docs/BUGS.md, docs/SEGURIDAD.md, docs/MANUAL_USUARIO.md, docs/DOC_TECNICA.md, configuración de Claude Code (.claude/), CI base, .env.example, .gitignore, docker-compose.dev.yml.
- Por qué: arranque del proyecto.
- Verificado: coherencia cruzada entre documentos revisada a mano. Sin código aún.

## [13/10/2026 23:00] DECISION · F0 · Especificación v2.1
- Qué: docs/ESPECIFICACION.md pasa a v2.1 con 15 correcciones (sección 0.0, #16-#30).
- Por qué: e-CF obligatorio (Ley 32-23, plazo final 15/11/2026) antes del corte; fórmula de pendiente incoherente; esfuerzo mal estimado; contenedores; numeración de importadas; JWT vs revocación; permisos de borrador y exportación; MCP público; PDF sin aislamiento; auditoría borrable.
- Pendiente: P18 (sistema e-CF y validez de series 1/2) bloquea la Fase 3.
