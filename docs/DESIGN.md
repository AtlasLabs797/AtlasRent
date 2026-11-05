# DESIGN.md — Atlas Rent Design System

Fuente única de verdad para construir la UI de **Atlas Rent** (producto de la familia Atlas Labs). Léelo antes de escribir cualquier pantalla. Si algo no está aquí, no se inventa: se pregunta.

---

## 1. Dónde está el design system y cómo se usa

Los tokens, componentes y fichas de diseño están en:

**`C:\Proyectos\Atlas Rent Dev\Atlas Rent Design System`** (en el repo: `Atlas Rent Design System/`)

```
Atlas Rent Design System/
  DESIGN.md  readme.md  SKILL.md   ← reglas del DS (este archivo parte de su DESIGN.md)
  styles.css              ← único CSS que se enlaza (solo @import de tokens/)
  tokens/                 ← fonts, colors, typography, spacing, effects, base, components*.css
  components/             ← primitivas React (.jsx + .d.ts + .prompt.md por componente)
    core/ forms/ navigation/ data/ feedback/ ai/ documents/ workflow/
  guidelines/             ← fichas visuales de fundamentos (HTML)
  ui_kits/atlas-rent/     ← app de referencia navegable (composición de pantallas; no es código de producción)
  uploads/                ← material de origen (no copiar al frontend)
  _ds/  _ds_bundle.js  _ds_manifest.json  _adherence.oxlintrc.json   ← artefactos generados y DS padre Atlas Labs; no canónicos
```

Antes de usar un componente, lee su `.d.ts` (contrato de props) y su `.prompt.md` (cuándo usarlo). Para componer una pantalla, mira primero `ui_kits/atlas-rent/`.

**Prioridad:** en tokens y componentes manda el design system; en cómo se integran con el stack (abajo) manda este archivo. Si discrepan, anótalo en `CHANGELOG.md` y pregunta.

### Integración en el frontend (React 19 + TypeScript + Vite, CSS nativo)

Se hace en F1 y se documenta en `docs/DOC_TECNICA.md`:

1. Copiar `styles.css`, `tokens/` y `components/` a `frontend/src/design-system/` e importar `styles.css` una sola vez en `main.tsx`. No copiar `_ds*`, `uploads/` ni `ui_kits/`.
2. Portar los `.jsx` a `.tsx` usando los `.d.ts` como contrato. Los `.d.ts` usan `JSX.Element`, que React 19 ya no expone como global: sustituir por `React.JSX.Element` o `ReactNode`.
3. `Icon.jsx` carga los SVG de Lucide desde un CDN (unpkg) por `mask`: reescribirlo con `lucide-react` (dependencia del stack) manteniendo la API `<Icon name="…" />`. Nada de recursos de red en tiempo de ejecución.
4. Fuentes Geist / Geist Mono: autoalojadas con `@fontsource/geist-sans` y `@fontsource/geist-mono` (pesos 300/400/600/700; mono 400/600), importadas en `main.tsx`. En la copia del frontend, `tokens/fonts.css` deja de importar Google Fonts y queda vacío: `--font-sans` ya incluye "Geist Sans", el nombre de familia que registra `@fontsource/geist-sans`.
5. Tema claro/oscuro: mecanismo en §3.1 y en `docs/DOC_TECNICA.md` §3.1.
6. Sin Tailwind, Bootstrap, shadcn ni CSS-in-JS. Clases del DS con prefijo `ar-` (BEM) y variables CSS de `tokens/`.
7. Están portados los 59 componentes del DS (`frontend/src/design-system/components/**`, cada uno con su `.prompt.md`) y se ven en `/componentes`, en claro y oscuro. Dos ajustes respecto al original: `Money` acepta `amount` como string decimal (se formatea sin coma flotante) y `Icon` lleva un mapa de alias porque lucide-react 1.x renombró `trash-2`, `building-2` y `file-signature`. `InvoiceLines`, `PaymentAllocation`, `DepositSummary` e `InventoryChecklist` suman con `Number` solo para vista previa: la API es quien calcula (AGENTS §6). Los ajustes de `Money` e `Icon` están solo en la copia del frontend: pendiente replicarlos en `Atlas Rent Design System/` (§1.8).
8. Cuando se cambie algo del DS (tokens o componentes), se cambia primero en `Atlas Rent Design System/` y después se copia al frontend, para que las dos copias no diverjan.

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

---

### 3.1 Temas claro y oscuro

Paridad completa (D31 de la especificación). Los tokens del tema oscuro están en `Atlas Rent Design System/tokens/themes.css`, basados en el tema oscuro de la familia Atlas Labs.

- **Mecanismo:** `<html data-theme="light|dark">`, fijado siempre por la app. Preferencia del usuario: Claro / Oscuro / Sistema (por defecto Sistema), en un control de la `TopBar`; se guarda en ese navegador. "Sistema" se resuelve con `matchMedia('(prefers-color-scheme: dark)')` antes del primer pintado, sin parpadeo.
- **Regla de oro:** ningún componente ni pantalla usa colores fuera de los tokens semánticos (`--surface-*`, `--text-*`, `--border-*`, `--accent*`, estados, `--amount-*`, `--unit-*`). Nada de hex, `rgba()` ni rampas (`--gray-500`, `--green-300`…) en código de pantallas. Así el oscuro funciona sin tocar cada vista.
- **Fondos con texto blanco** (botón peligro, contadores, avatar de agente): usar `--danger-solid` y `--ai-solid`, no `--danger` ni `--ai`. En oscuro, `--danger`, `--success`, `--warning`, `--info` y `--ai` pasan a tonos claros pensados para texto e indicadores sobre fondo oscuro.
- **Superficies en oscuro:** fondo app `#000000`, tarjeta `#1D1D1F`, hundido `#151517`. `--surface-inverse` (toasts, tooltips, KPI destacado, slot ocupado) sigue siendo una superficie oscura con texto blanco, un paso más clara que la tarjeta (`#3A3A3C`).
- **Acento en oscuro:** sólido `hibisco-500`, texto y enlaces `hibisco-300`. Sigue siendo el único acento.
- **Contraste mínimo** (WCAG 2.2 AA) en ambos temas: texto 4,5:1; texto grande, iconos, bordes de controles y anillo de foco 3:1.
- **Gráficos (Recharts):** colores desde tokens CSS leídos en tiempo de ejecución, nunca hex fijos en el componente.
- **PDF:** siempre en claro, independientemente del tema del usuario (§7).

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
- **Búsqueda remota** (AUD-11): `Combobox` con la prop `onSearch` no filtra en local; la pantalla consulta la API con debounce de 300 ms y conserva la opción ya elegida. Es lo que usan los selectores de cliente y de contrato (cartera de más de 200).
- **Detalle de auditoría**: tarjeta bajo la tabla, no modal; `datos` como JSON formateado en `<pre>`, nunca como HTML.

---

### Patrón: calendario de disponibilidad (`frontend/src/modulos/calendario/`)

Cuadrícula unidad × día (no es un componente del DS). Filas virtualizadas con TanStack Virtual, primera columna y cabecera fijas. Una casilla por día, solo con tokens de estado de unidad: libre `--success-soft`, contrato `--unit-ocupado`, reserva `--unit-reservado`, mantenimiento `--unit-mantenimiento`, fuera de servicio `--surface-sunken` con borde `--unit-fuera`. El estado no depende solo del color: lleva letra (C, R, D, M). Un único medio día reservado se pinta a medias. Cada casilla es un botón con `aria-label` "unidad, fecha: estado" y abre un `Drawer` con el detalle.

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

## 7. Documentos PDF (factura, proforma, recibo, estado de cuenta, cotización, contrato)

Diseño base **[Supuesto, A2]**: se ajusta cuando el responsable aporte los ejemplos actuales por serie (F0-06). Se genera con QuestPDF, por código, sin HTML.

- **Página:** Carta (8,5 × 11 in), márgenes 18 mm, siempre en claro (fondo blanco, texto `#1D1D1F`).
- **Fuentes:** Geist (texto) y Geist Mono (importes, fechas, números de documento), incrustadas en el PDF. Cuerpo 9,5 pt; títulos 600.
- **Cabecera:** a la izquierda, lockup de Atlas Rent (o el logo de la empresa facturadora cuando exista) y datos de la empresa facturadora (nombre, RNC, dirección, teléfono, email). A la derecha, tipo de documento en grande, número en mono, fecha de emisión y vencimiento, comprobante fiscal si lo tiene y moneda del documento.
- **Cliente:** bloque con nombre, documento (completo, D16), dirección y contacto; unidad o contrato asociado si aplica.
- **Líneas:** tabla sin bandas de color; cabecera en overline gris; descripción a la izquierda, cantidades e importes a la derecha en mono; separadores de 0,5 pt.
- **Totales:** a la derecha: subtotal, ITBIS, retenciones, total en 600 y, si hay cobros, pagado y pendiente. Importes con el formato de §5 (`1.250,00 USD`).
- **Color:** solo el acento en la marca y una línea fina bajo la cabecera. Estados (Pagada, Anulada) como texto, nunca como bloque de color; "ANULADA" se imprime como marca de agua gris.
- **Pie:** condiciones de pago, cuentas bancarias de la serie, referencia de e-factura externa si tiene valor, "Página x de y".
- Los textos de usuario (descripciones, notas) se escriben como texto plano; nunca se interpretan como formato.

## 8. Pendiente

- Logo real (SVG) y fuentes propias si existen → `assets/` + `tokens/fonts.css`.
- Ajustar el diseño de PDF con los ejemplos por serie de F0-06.
- `--surface-sunken` en oscuro (`#151517`) apenas se distingue del fondo de la barra superior: el control de tema (`SegmentedControl`) pierde su pista. Revisar el token en el DS.
- Los componentes con textos fijos en español (`Dialog` "Cerrar", `Pagination` "de", `ApprovalCard`, etc.) no están en `i18n/textos.ts`: cuando se active el inglés habrá que parametrizarlos.
- Los desplegables (`Select`, `Combobox`, `DatePicker`, `Menu`) cierran con clic fuera y Escape pero `Dialog` y `Drawer` no atrapan el foco; `ConfirmarDestructivo` solo añade Escape.
- La lista de fianzas (Cliente, Unidad y siete columnas de importes) no cabe a 1280 px: Saldo y Estado quedan fuera de la vista. Igual que la tabla de cuentas de 11 columnas: hace falta un criterio de columnas prioritarias.
- La prop `onSearch` de `Combobox` está en el frontend y en `Atlas Rent Design System/components/forms/`; revisar que el DS la documente en su muestra.
- Accesibilidad: el DS cubre contraste, `:focus-visible` e iconos `aria-hidden`. Roles ARIA y navegación por teclado se verifican al portar cada componente (F1-13).
