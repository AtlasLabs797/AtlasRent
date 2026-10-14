# AGENTS.md — Atlas Rent

Sistema interno de gestión de alquileres (sustituye a Factusol). Fuente canónica para todos los agentes (Claude Code, Codex, OpenCode, Cursor). Escribe y responde en español.

## 1. Documentos (leer antes de trabajar)
| Documento | Para qué |
|---|---|
| `docs/ESPECIFICACION.md` | **QUÉ** se construye. Manda sobre el resto; discrepancias se anotan en `CHANGELOG.md` |
| `docs/DOC_TECNICA.md` | **CÓMO**: versiones, estructura, BD, lógica de dominio |
| `docs/PLAN.md` | Orden de tareas (F0…FM), dependencias y estados |
| `docs/SEGURIDAD.md` | Reglas y riesgos de seguridad (SEC-*) |
| `docs/DESIGN.md` y `Atlas Rent Design System/` | Diseño visual, tokens y componentes |
| `docs/BUGS.md`, `CHANGELOG.md` | Trazabilidad |

`ESPECIFICACION.md` de la raíz es copia de `docs/ESPECIFICACION.md`: si se edita una, actualizar la otra.

## 2. Stack (idéntico a Atlas Balance V-03.01; versiones en DOC_TECNICA §2)
ASP.NET Core 10 + EF Core 10 + PostgreSQL ≥16 · React 19 + TypeScript + Vite + Zustand + TanStack Query. Despliegue en Windows Server como servicios de Windows.
- **Reutilizar antes de crear:** copiar y adaptar código de `AtlasLabs797/AtlasBalance` (público), sin depender de ese repo. Anotar el origen en `CHANGELOG.md` y en DOC_TECNICA §3.1.
- Dependencias nuevas solo si son imprescindibles (QuestPDF ya aprobado). Proponer antes de añadir.
- Estructura de carpetas: DOC_TECNICA §3 (`backend/`, `frontend/`, `scripts/`, `db/scripts/`).

## 3. Entorno de desarrollo
- PostgreSQL de desarrollo: `docker compose -f docker-compose.dev.yml up -d`, puerto **5433** (el 5432 lo ocupa el PostgreSQL nativo de Atlas Balance en esta máquina; no tocarlo).
- Base `atlas_rent` con roles `rent_owner`, `rent_migrador`, `rent_app`, `rent_backup` ya creados desde `db/scripts/01_roles.sql` y `02_basedatos.sql`. `03_auditoria_append_only.sql` se aplica desde la migración de F1-03.
- Credenciales de desarrollo en `.env.dev` (ignorado por git, no leer ni imprimir). Al crear la API, pasarlas a `dotnet user-secrets`.
- Herramientas: .NET SDK 10, `dotnet-ef` 10, Node 24, Docker Desktop, `gh`.

## 4. Flujo de trabajo
1. Rama `tarea/<ID>-<nombre-corto>` desde `main`. Tomar la siguiente tarea libre de `docs/PLAN.md` (`/siguiente`), marcarla EN CURSO y resumir en 5 líneas qué se hará.
2. Implementar con tests. Cada tarea cabe en una sesión y un PR revisable; si no, dividirla.
3. `/cerrar-tarea <ID>` al terminar (CHANGELOG, PLAN, documentación). PR a `main`; otra persona revisa.
4. Bugs: `/registrar-bug`. Seguridad: `/registrar-seguridad`. Al cerrar cada fase: `/revision-seguridad`.
5. Nunca hacer push a `main`, ni `push --force`, ni `reset --hard`.

## 5. Reglas de código
- Controllers finos; reglas de negocio y transacciones en `Services`. Nombres de tablas y columnas en `snake_case`.
- Dinero `numeric(18,2)` / `decimal`, cambio `numeric(18,6)`. Redondeo half-up por línea, 2 decimales. Cada documento en una sola moneda, sin convertir.
- Cifras fiscales (ITBIS, retenciones, rangos de comprobante) siempre desde configuración, nunca en código.
- Documentos económicos: se anulan, no se borran. Auditoría append-only.
- Cédula/RNC/pasaporte cifrados en BD (AES-256-GCM) con índice ciego HMAC.
- Textos de interfaz centralizados (español, inglés activable). Zona horaria de negocio `America/Santo_Domingo`.
- Comentarios solo cuando el porqué no es evidente.

## 6. Seguridad
- Sin secretos, certificados ni datos reales en el repo (`.gitignore` ya lo cubre). Los datos de Factusol (`imports/`, `exports/`, `*.csv`, `*.xlsx`) nunca se suben.
- Permisos por usuario recortables desde el rol, nunca ampliables. Comprobar permisos en la API, no solo en la interfaz.
- MFA obligatorio solo para administrador (D4). Acceso público por internet: bloqueo de cuenta e IP, rate limit.
- Detalle en `docs/SEGURIDAD.md`.

## 7. Verificación antes de dar una tarea por hecha
Backend (cuando exista `backend/`):
```
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet list package --vulnerable --include-transitive
```
Frontend (cuando exista `frontend/`):
```
npm --prefix frontend ci
npm --prefix frontend run lint
npm --prefix frontend run typecheck
npm --prefix frontend test
npm --prefix frontend run build
```
Si algo no se pudo verificar, decirlo explícitamente en `CHANGELOG.md` y en el PR.
