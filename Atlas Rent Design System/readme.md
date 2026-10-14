# Atlas Rent Design System
Design system for **Atlas Rent**, an internal web app (Atlas Labs family) that replaces Factusol for managing ~150 medium/long-term rental units plus daily/hourly spaces (oficinas, salas) in the Dominican Republic: clientes, inmuebles/unidades, cotizaciones, reservas, contratos con renovación automática, facturación recurrente/manual por series, cobros, saldos a favor, fianzas, inventarios, USD/DOP con tipo de cambio congelado, WhatsApp/PDF, informes, roles (Administrador / Gerente / Empleado), auditoría, and **agent-native AI** (agentes con permisos, bandeja de aprobaciones IA, MCP).

## Sources
- `Atlas Rent/especificacion_app_gestion_alquileres.txt` — functional/technical spec (v. consolidada, 08/10/2026). Local mounted folder; the only file supplied. No UI code, Figma, logo or screenshots.
- Parent **Atlas Labs** design system (bound at `_ds/atlas-labs-…`). Foundations reconciled to it in Oct 2026: tokens, radii, type ladder, flat elevation and motion now match; only the accent differs.
- Spec recommends Next.js + React + Tailwind frontend; tokens map cleanly to a Tailwind theme.

## Index
- `styles.css` — entry point (imports only) → `tokens/` fonts, colors, typography, spacing, effects, base, components (ar-* classes), components-app (shell, finance, rentals, AI, workflow), components-ext (menus, search, skeleton, shell layout).
- `guidelines/` — foundation specimen cards (Colors, Type, Spacing, Effects, Brand).
- `components/` — React primitives, one folder per concern, each with a card.
- `ui_kits/atlas-rent/` — click-through web app (Dashboard, Unidades, Facturas + Registrar cobro, Aprobaciones IA).
- `thumbnail.html`, `SKILL.md`.

## Final pass — Oct 2026
One grammar everywhere: 18px hairline cards with no header divider and 21px titles; every button, chip, search and segmented control a pill; every dropdown (Select, Combobox, Menu, AIActionButton) the same 18px floating panel with `--shadow-lg`; hover = `--surface-sunken`, press = scale. Featured KPI is a dark tile (`--tile-1`) instead of a pink tint. Tables lose the grey header band and align to the 24px card padding. Sidebar collapses to a 72px rail under 1100px and hides under 720px. No hand-rolled shells, dropdowns or detail rows in screens — use AppShell, Menu, KeyValueList and UnitCard. `@startingPoint` tags removed (templates replace them).

## Components
- core: **Button**, **IconButton**, **Badge**, **Card**, **Icon**, **Kbd**
- shell: **AppShell** (SideNav + TopBar + content column), **CommandPalette** (⌘K global search), **SectionHeader**, **Menu** (row/header action menus)
- forms: **Combobox** (cliente por nombre, RNC o nº)
- feedback: **Skeleton**
- forms: **Input**, **Select** (custom dropdown), **DatePicker** (typed or calendar), **Checkbox**, **Radio**, **Switch**
- data: **StatCard**, **Money**, **DataTable**
- feedback: **Alert**, **Toast**, **Tooltip**, **Dialog**
- navigation: **Tabs**, **SegmentedControl**
- ai: **ApprovalCard**, **AIActionButton**, **ChatPanel**, **AgentBadge**
- navigation (shell): **SideNav**, **TopBar**, **PageHeader**, **Breadcrumbs**, **FilterBar**, **Pagination**, **Stepper**
- forms: **Textarea**
- data (records): **Avatar**, **KeyValueList**, **Timeline**, **AuditLog**, **ProvenanceTag**, **RestrictedValue**
- data (rentals): **UnitCard**, **OccupancyBar**, **HourSlots**, **InventoryChecklist**
- data (finance): **InvoiceLines**, **PaymentAllocation**, **ExchangeRate**, **DepositSummary**
- feedback: **Drawer**, **EmptyState**
- documents: **MessagePreview**, **DocumentRow**
- workflow: **ReviewItem**, **ImportBatch**

Oct 2026 expansion, each mapped to the spec: HourSlots (§6.2 reservas por horas, no solape), InvoiceLines (§10.5), PaymentAllocation (§13.5–13.6 saldo a favor), ExchangeRate (§15 congelado), DepositSummary (§14), InventoryChecklist (§8.3), AuditLog + ProvenanceTag (§2.4, §26), RestrictedValue (§17.2 empleado sin totales), ReviewItem (§21), ImportBatch + Stepper (§25), ChatPanel + AgentBadge (§22–23), MessagePreview/DocumentRow (§18–19). App classes live in `tokens/components-app.css`.

No component inventory existed in the source; this is an authored standard set sized to the spec. Intentional, domain-driven additions: **Money** (USD/DOP formatting + frozen rate), **StatCard** (dashboard KPIs), **ApprovalCard** and **AIActionButton** (agent-native requirements §22–23), **Icon** (Lucide wrapper).

---
## CONTENT FUNDAMENTALS
- **Language:** Spanish (Dominican business register). UI copy in *usted*-neutral imperative/infinitive: "Registrar cobro", "Generar facturas periódicas", "Convertir a contrato". Never "tú" chatty copy.
- **Casing:** Sentence case everywhere — buttons, titles, tabs ("Bandeja de revisión", not "Bandeja De Revisión"). UPPERCASE only for overlines (RESUMEN · OCTUBRE 2026).
- **Vocabulary is the spec's:** Unidad, Inmueble, Cliente, Contrato, Reserva, Factura, Cobro, Fianza, Saldo a favor, Baja, Serie, Empresa facturadora/propietaria. Unit states: Disponible · Reservado · Ocupado · En mantenimiento · Fuera de servicio. Payment: Pendiente · Parcial · Pagado; *Vencida* is a computed tag shown alongside, never instead.
- **Numbers:** `1.250,00 USD` / `27.900,00 DOP` — código de moneda detrás (USD / DOP), punto de miles, coma decimal, 2 decimales, tabular figures. Rates: `60,35 + 1,65 → 62,00`. Dates `dd/mm/aaaa`. IDs in mono: `FAC-A-2026-00412`, `CT-0217`, `CLI-01873`.
- **Tone:** factual, calm, operational. State the fact + the object: "Reserva con más de 7 días — Torre Coral · 2C". No exclamation marks, no emoji, no marketing adjectives.
- **AI copy** names the agent and shows evidence: "Agente de cobros · Transferencia Banco Popular ref. 88231 coincide con cliente e importe pendiente." Actions: Aprobar / Rechazar / Modificar y aprobar.
- **Destructive copy** says what happens and asks for a motivo: "Anular factura — los cobros vinculados se revierten. Motivo *".
- Separators: middle dot ` · ` between facts; arrow `→` for transitions of value/state.

## VISUAL FOUNDATIONS
Atlas Rent is an **Atlas Labs product**: it inherits the parent system's Apple-grammar foundation (Aug 2026 retheme) one-to-one and changes only the accent channel. Neutrals, type, spacing, radii, elevation and motion are the Atlas Labs values.
- **Accent — Hibisco `#AD2275`** (`--hibisco-600`; hover 500 `#C7378A`, press 700 `#8A1A5D`, on dark 300 `#E98DBD`). Chosen because it is the free hue in the family: Labs Action Blue, Balance cobalt, Connect ember, Peak teal (the old Atlas Rent teal collided with Peak) and AI violet are all taken. 6.4:1 on white. Used only for the primary action, active nav, focus ring, selected rows and the featured KPI — never as a status colour.
- **Neutrals:** Atlas Labs warm grey (`--gray-25…950`, ink `#1D1D1F`, parchment `#F5F5F7`). Legacy `--ink-*` names alias into it.
- **Status:** Atlas Labs green / amber / crimson / cobalt as bg-border-text triplets; violet reserved for AI in every app. Ocupado is neutral ink, not accent.
- **Type:** Geist + Geist Mono, weights 300/400/600/700 (no 500). 17px body at −0.022em, 14 label/caption, 12 overline +0.08em, headings 40/28/21. Every amount, rate and ID in Geist Mono tabular; featured KPI 44px mono 600.
- **Spacing:** Atlas Labs 4px scale (8/12/17/24 do the work); cards pad 24; page gutter 32; rows 56 (compact 44); controls 32/44/52; sidebar 264; topbar 64.
- **Radii:** 5 / 8 / 11 (fields) / **18 cards, panels, dialogs** / pill for every button, badge, segmented control and search field.
- **Elevation:** flat. Cards = white + 1px hairline + no shadow. Only menus/toasts (`--shadow-lg`) and dialogs (`--shadow-xl`) float.
- **Buttons:** primary = accent pill; secondary = accent-outline pill (never grey); ghost for tertiary; AI = violet soft pill. Sizes 32 / 44 / 52; one size per action row.
- **Overlays:** dropdowns and menus 18px + `--shadow-lg`, fade + 4px drop; dialogs and ⌘K 18px + `--shadow-xl`, fade + 8px rise.
- **Chrome:** light frosted sidebar and topbar (white 72% + saturate(180%) blur(20px)), active nav = accent tint. Scrim 48% + 4px blur.
- **Motion:** `cubic-bezier(.22,1,.36,1)`, 100/120/180/240/420ms; hover changes surface, press is `scale(.95)`; respects reduced motion.
- **Data:** numbers right-aligned in mono; status as dotted pill badge; selected row = accent tint + 2px accent edge; max four KPI tiles per row, one featured.
- **Imagery:** none. Flat colour only.

## ICONOGRAPHY
- **Lucide 0.470.0** via CDN (same as Atlas Labs), rendered by `<Icon name>` as a currentColor mask. 14px in dense cells, 16 default/nav, never above 20 in app UI.
- Icons inherit text colour: tertiary at rest, accent when active.
- No emoji, no unicode glyph icons (except `·` and `→` as text separators).

## Brand / logo
No logo supplied. Lockup = 26px accent rounded-square mark "AR" + **"Atlas Rent"** in Geist 600 21px (Rent in accent) + "by Atlas Labs" caption — the same pattern as the Atlas Labs SideNav brand.

## Fonts
Geist and Geist Mono load from Google Fonts (`tokens/fonts.css`); no binaries were supplied.
