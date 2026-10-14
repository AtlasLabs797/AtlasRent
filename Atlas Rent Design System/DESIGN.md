# DESIGN.md — Atlas Rent Design System

Fuente única de verdad para construir la UI de **Atlas Rent** (producto de la familia Atlas Labs). Léelo antes de escribir cualquier pantalla. Si algo no está aquí, no se inventa: se pregunta.

> **En el repositorio de Atlas Rent** la integración (Vite + React 19 + TypeScript, CSS nativo, sin Tailwind ni Next.js), los temas claro/oscuro y las reglas de PDF están en `docs/DESIGN.md`, que manda sobre §1 y sobre el mapeo Tailwind de este archivo. Tema oscuro: `tokens/themes.css` (`<html data-theme="dark">`).

---

## 1. Instalación en el repositorio

```
/design-system
  styles.css              ← único CSS que se enlaza (solo @import)
  tokens/                 ← fonts, colors, typography, spacing, effects, base, components*.css
  components/             ← primitivas React (.jsx + .d.ts + .prompt.md por componente)
    core/ forms/ navigation/ data/ feedback/ ai/ documents/ workflow/
  guidelines/             ← fichas visuales de fundamentos (HTML)
  ui_kits/atlas-rent/     ← app de referencia navegable
  _ds_bundle.js           ← build precompilado (window.AtlasRent… ) para HTML estático
  DESIGN.md  readme.md  SKILL.md
```

**Next.js / React**

```js
// app/layout.tsx
import '@/design-system/styles.css';
// uso
import { Button } from '@/design-system/components/core/Button';
```

Los `.jsx` son React puro sin dependencias (solo `react`). Los `.d.ts` son el contrato de props; los `.prompt.md` explican cuándo usar cada uno.

**HTML estático**: cargar React 18 + `styles.css` + `_ds_bundle.js`, y leer los componentes del namespace global.

**Iconos**: Lucide 0.470.0 por CDN (`unpkg.com/lucide-static@0.470.0/icons/<name>.svg`) vía `<Icon name="…" />`. Si se instala `lucide-react`, sustituir dentro de `Icon.jsx` y nada más.

**Fuentes**: Geist + Geist Mono desde Google Fonts (`tokens/fonts.css`). En Next.js se puede cambiar por `next/font/google` manteniendo los nombres de familia.

---

## 2. Principios

1. **Plano.** Sin sombras en tarjetas, botones ni inputs. La profundidad la dan el cambio de superficie y la línea de 1px.
2. **Un acento.** Hibisco solo para: acción primaria, nav activa, foco, fila seleccionada, KPI destacado. Nunca como color de estado.
3. **Los números mandan.** Todo importe, tasa, ID y fecha en Geist Mono tabular, alineado a la derecha.
4. **Violeta = IA.** Todo lo generado o propuesto por un agente usa el canal `--ai-*`.
5. **Sin imágenes.** Color plano. Sin degradados decorativos, sin ilustraciones, sin emoji.

---

## 3. Tokens

### Color

| Rol | Token | Valor |
|---|---|---|
| Acento | `--accent` | `#AD2275` (hibisco-600) |
| Acento hover / press | `--accent-hover` / `--accent-press` | `#C7378A` / `#8A1A5D` |
| Acento sobre oscuro | `--accent-on-dark` | `#E98DBD` |
| Tinte acento | `--accent-soft` | hibisco-500 8% sobre blanco |
| Fondo app | `--surface-app` | `#F5F5F7` |
| Tarjeta | `--surface-card` | `#FFFFFF` |
| Hundido / hover | `--surface-sunken` | `#EDEDEF` |
| Tile oscuro | `--surface-inverse` | `#272729` |
| Texto | `--text-strong` / `--text-muted` / `--text-subtle` | `#1D1D1F` / `#6E6E73` / `#86868B` |
| Borde | `--border-subtle` / `--border-default` / `--border-strong` | `#F0F0F0` / `#E0E0E0` / `#D2D2D7` |
| Éxito | `--success` · `-soft` · `-border` | `#176A3A` · `#E7F5EC` · `#CDEBD9` |
| Aviso | `--warning` · `-soft` · `-border` | `#8A5200` · `#FDF3E1` · `#FAE6C2` |
| Peligro | `--danger` · `-soft` · `-border` | `#A82234` · `#FDECEE` · `#FBD9DD` |
| Info | `--info` · `-soft` · `-border` | `#214CAD` · `#E6EEFC` · `#D2E0FA` |
| IA | `--ai` · `--ai-soft` · `--ai-border` | `#7340E0` · violeta 8% · violeta 24% |
| Importe + / − | `--amount-positive` / `--amount-negative` | `#176A3A` / `#A82234` |

**Estados de unidad**: Disponible `--unit-disponible` (verde) · Reservado `--unit-reservado` (cobalto) · Ocupado `--unit-ocupado` (tinta, no acento) · En mantenimiento `--unit-mantenimiento` (ámbar) · Fuera de servicio `--unit-fuera` (gris).

### Tipografía

Geist (UI) + Geist Mono (cifras). Pesos **300 / 400 / 600 / 700** — el 500 no existe.

| Rol | Token | Valor |
|---|---|---|
| Display | `--type-display` | 600 56/1.07 |
| H1 | `--type-h1` | 600 40/1.1 |
| H2 | `--type-h2` | 600 28/1.14 |
| H3 / título de tarjeta | `--type-h3` | 600 21/1.19 |
| Body (por defecto) | `--type-body` | 400 17/1.47, tracking −0.022em |
| Label | `--type-label` | 600 14/1.29 |
| Caption | `--type-caption` | 400 14/1.43 |
| Overline | `--type-overline` | 600 12, MAYÚSCULAS, +0.08em |
| Mono | `--type-mono` | 400 14 tabular |
| KPI | `--type-kpi` | 600 44 mono |

### Espaciado, radios y medidas

- Escala 4px: `--space-4` 8 · `--space-6` 12 · `--space-7` 17 · `--space-9` 24 · `--space-10` 32 · `--space-14` 80.
- Radios: `5` xs · `8` sm · `11` campos · **`18` tarjetas, paneles, diálogos** · `999` todo botón, badge, chip, segmentado y buscador.
- Controles: 32 / **44** / 52 px. Filas 56 (compacta 44). Tarjeta padding 24 (compacta 20). Gutter 32.
- Shell: sidebar 264 (rail 72 <1100px, oculta <720px), topbar 64, contenido máx. 1440 (980 en pantallas de texto).

### Elevación y movimiento

- `--shadow-xs/sm/md` = `none`. Solo flotan menús/toasts (`--shadow-lg`) y diálogos/⌘K (`--shadow-xl`).
- Scrim `rgba(0,0,0,.48)` + `blur(4px)`. Chrome frosted: blanco 72% + `saturate(180%) blur(20px)`.
- Easing único `cubic-bezier(.22,1,.36,1)`; duraciones 100/120/180/240/420ms.
- Hover = cambia superficie. Press = `scale(.95)`. Foco = anillo 3px `--accent-ring`.
- Menús: fade + 4px. Diálogos: fade + 8px. Drawers: slide 24px. Nada anima al cargar. Respeta `prefers-reduced-motion`.

### Mapeo Tailwind (opcional)

```js
// tailwind.config.js
theme: { extend: {
  colors: {
    accent: { DEFAULT: 'var(--accent)', hover: 'var(--accent-hover)', soft: 'var(--accent-soft)' },
    surface: { app: 'var(--surface-app)', card: 'var(--surface-card)', sunken: 'var(--surface-sunken)' },
    ink: { DEFAULT: 'var(--text-strong)', muted: 'var(--text-muted)', subtle: 'var(--text-subtle)' },
    line: { subtle: 'var(--border-subtle)', DEFAULT: 'var(--border-default)', strong: 'var(--border-strong)' },
    success: 'var(--success)', warning: 'var(--warning)', danger: 'var(--danger)', info: 'var(--info)', ai: 'var(--ai)',
  },
  fontFamily: { sans: 'var(--font-sans)', mono: 'var(--font-mono)' },
  borderRadius: { xs: '5px', sm: '8px', field: '11px', card: '18px', pill: '999px' },
  boxShadow: { none: 'none', menu: 'var(--shadow-lg)', dialog: 'var(--shadow-xl)' },
  transitionTimingFunction: { atlas: 'cubic-bezier(.22,1,.36,1)' },
}}
```

---

## 4. Componentes

| Grupo | Componentes |
|---|---|
| core | Button, IconButton, Badge, Card, Icon, Kbd |
| forms | Input, Textarea, Select, Combobox, DatePicker, Checkbox, Radio, Switch |
| navigation | AppShell, SideNav, TopBar, PageHeader, SectionHeader, Breadcrumbs, Tabs, SegmentedControl, Menu, CommandPalette, FilterBar, Pagination, Stepper |
| data | StatCard, Money, DataTable, Avatar, KeyValueList, Timeline, AuditLog, ProvenanceTag, RestrictedValue |
| data · alquiler | UnitCard, OccupancyBar, HourSlots, InventoryChecklist |
| data · finanzas | InvoiceLines, PaymentAllocation, ExchangeRate, DepositSummary |
| feedback | Alert, Toast, Tooltip, Dialog, Drawer, EmptyState, Skeleton |
| ai | ApprovalCard, AIActionButton, ChatPanel, AgentBadge |
| documents | MessagePreview, DocumentRow |
| workflow | ReviewItem, ImportBatch |

### Reglas de uso

- **Botones**: primario = pastilla acento; secundario = pastilla con borde acento (nunca gris); ghost para terciario; IA = pastilla violeta suave. Un solo primario por vista. Un solo tamaño por fila de acciones.
- **Tarjetas**: 18px + 1px hairline + sin sombra. Título 21px sin divisor de cabecera.
- **Desplegables** (Select, Combobox, Menu, AIActionButton): mismo panel flotante 18px + `--shadow-lg`.
- **Tablas**: sin banda gris de cabecera; números a la derecha en mono; estado como badge con punto, nunca fila coloreada; seleccionada = tinte acento + borde 2px acento.
- **KPI**: máx. 4 por fila, 1 destacado (tile oscuro `--tile-1`). Toda métrica lleva comparación.
- **Pantallas**: siempre `AppShell` + `PageHeader`. Nada de shells, dropdowns o filas de detalle hechas a mano — usar AppShell, Menu, KeyValueList, UnitCard.
- **Empleado sin permisos**: totales con `RestrictedValue`, no ocultos en silencio.
- **IA**: toda acción de agente pasa por `ApprovalCard` (Aprobar / Rechazar / Modificar y aprobar) con `AgentBadge` y evidencia.

---

## 5. Contenido

- Español dominicano, registro neutro con infinitivo/imperativo: "Registrar cobro", "Convertir a contrato".
- Sentence case siempre. MAYÚSCULAS solo en overlines vía CSS.
- Vocabulario del dominio: Unidad, Inmueble, Cliente, Contrato, Reserva, Factura, Cobro, Fianza, Saldo a favor, Baja, Serie, Empresa facturadora/propietaria.
- Pago: Pendiente · Parcial · Pagado. *Vencida* es una etiqueta calculada que acompaña, no sustituye.
- Importes: `1.250,00 USD` · `27.900,00 DOP` — código detrás, punto de miles, coma decimal, 2 decimales. Tasa: `60,35 + 1,65 → 62,00`. Fechas `dd/mm/aaaa`. IDs en mono: `FAC-A-2026-00412`, `CT-0217`, `CLI-01873`.
- Separadores: ` · ` entre hechos, `→` para transiciones.
- Destructivo: dice qué pasa y pide motivo — "Anular factura — los cobros vinculados se revierten. Motivo *".
- Prohibido: emoji, signos de exclamación, adjetivos de marketing, "simplemente".

---

## 6. Marca

Sin logo suministrado. Lockup: marca cuadrada redondeada 26px en acento con "AR" + **Atlas Rent** (Geist 600 21px, "Rent" en acento) + "by Atlas Labs" en caption.

## 7. Pendiente

- Logo real (SVG) y fuentes propias si existen → `assets/` + `tokens/fonts.css`.
- Sustituir iconos CDN por `lucide-react` si el repo lo prefiere.
