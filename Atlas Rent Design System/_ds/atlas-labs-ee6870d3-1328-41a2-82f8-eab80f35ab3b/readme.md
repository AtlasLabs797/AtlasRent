# Atlas Labs Design System

**Atlas Labs is the parent company, not an app.** It builds **internal software for businesses** — the operations, finance and people tools a company runs on rather than sells — and ships that software as separate products: **Atlas Balance** (tesorería) and **Atlas Connect** (CRM y operaciones), with more to come. This system is the shared design language across those products: one shell, one neutral foundation, one motion signature, and a single per-app accent channel, so each product reads as the same maker without looking like the same app.

The vibe is a **gallery, then a console**: the brand surfaces breathe like a product page and the app surfaces stay precise, but both run on the same tokens. Premium comes from restraint — hairline borders, **no shadows**, tabular figures, one confident blue, and a lot of nothing.

> **August 2026 — Apple-grammar retheme.** The system was re-tuned end to end against an Apple web analysis: Action Blue replaces indigo, body moves 14 → **17px**, cards go 12 → **18px**, buttons become **pills**, the shadow ladder is **flat** (depth comes from surface change and hairlines), and full-bleed light/dark **bands** are now the section rhythm. Everything below reflects the new state; component APIs did not change.

## Sources used

Distilled from the two shipping Atlas design systems, which were themselves lifted from the product codebases:

| Source | What was taken |
| --- | --- |
| **Atlas Balance DS** (`github.com/AtlasLabs797/AtlasBalance`) | Density and information hierarchy, the 252/72 sidebar + 68px topbar shell, the KPI/featured-card pattern, money and audit conventions, the `cubic-bezier(.22,1,.36,1)` "premium" easing, the cool near-white surface ladder, Spanish operational voice. |
| **Atlas Connect DS** (`github.com/AtlasLabs797/AtlasConnect`) | The eyebrow → title → one-line description page rhythm, generous radii, soft/low shadow discipline, the fixed task-state vocabulary, the **violet AI channel**, Lucide iconography, "honest empty state" copy. |

**Reconciled, not merged blindly** — where the two disagreed, one won and it is written down:

- **Type:** both were rejected. Balance's three-family stack (Hind Madurai + National Park + Atlas Mono) and Connect's Inter are replaced by **Geist + Geist Mono** — one family for everything structural, one mono for every number. Substitution, flagged below.
- **Accent:** neither product blue nor product orange leads the parent brand. Atlas Labs is **Action Blue** (`--action-600 #0066CC`, focus `#0071E3`, on dark `#2997FF`); Balance keeps cobalt `#285BD9` and Connect keeps ember `#FF4D24` as `data-app` overrides. The retired indigo survives as `data-app="indigo-legacy"`.
- **Radii:** the Apple grammar — 0 full-bleed bands / 5 xs / 8 dark utility / 11 pearl capsule / **18 cards, panels, modals** / pill for every button, chip and search field. Nothing in between.
- **Density:** Apple wins. **17px body**, 56px rows (44 compact), 44px controls, 32px page gutter, 80px band padding.
- **Language:** Spanish first, English mirrored. Specimens and kits are in Spanish because the products are.

No logo files, font binaries or icon assets were supplied, so the brand mark is typographic and fonts/icons load from public CDNs. Nothing was drawn or invented as if it were a real asset.

---

## Index

| Path | What it is |
| --- | --- |
| `styles.css` | The one file consumers link. `@import` list only. |
| `tokens/` | `typography.css` `colors.css` `spacing.css` `radius.css` `elevation.css` `motion.css` `semantic.css` `themes.css` `base.css` |
| `components/` | React primitives grouped `core/` `forms/` `navigation/` `data/` `feedback/` `ai/`, plus `components.css` (primitives), `shell.css` (app frame + page scaffolding) and `advanced.css` (overlays, pickers, data views, chat, charts) |
| `guidelines/` | Foundation specimen cards — Type, Colors, Spacing, Brand (incl. **Bandas y ritmo**), Charts |
| `ui_kits/atlas-labs-apple/` | The four reimagined surfaces in one page: marca, launcher, acceso, consola |
| `templates/` | Starting-point screens consuming projects copy |
| `thumbnail.html` | Homepage tile · `SKILL.md` Agent-Skills wrapper |

### Components

`core/` **Button**, **IconButton**, **Icon**, **Card**, **Badge**, **Tag** ·
`forms/` **Field**, **Input**, **Textarea**, **Select**, **Checkbox**, **Radio**, **Switch**, **Combobox**, **MultiSelect**, **DateRangePicker** ·
`navigation/` **AppShell**, **SideNav**, **TopBar**, **Tabs**, **SegmentedControl**, **Menu**, **CommandPalette**, **FilterBar**, **Breadcrumbs**, **Pagination**, **Stepper** ·
`data/` **Table**, **DataGrid**, **StatCard**, **Avatar** (+ **AvatarStack**), **SignedAmount**, **Timeline**, **KanbanBoard** ·
`feedback/` **Dialog**, **Drawer**, **Toast**, **Tooltip**, **EmptyState**, **NotificationList** ·
`ai/` **ChatPanel**, **AiFace** (the animated blob mascot mark — idle/listening/thinking, replaces the static `bot` icon in the AI channel)

Each has a sibling `.d.ts` (props contract) and `.prompt.md` (what, when, rules, example). Every group directory carries at least one `*.card.html` specimen.

**Charts are tokens + recipes, not components.** `--chart-1…10`, `--chart-grid`, `--chart-axis`, `--chart-area-*` plus the `.atl-chart`, `.atl-bars`, `.atl-donut`, `.atl-spark`, `.atl-track` classes. Copy the SVG/CSS patterns from `guidelines/charts-recipes.card.html`; no chart library ships in the bundle.

### Per-app identity

Set `data-app="balance" | "connect" | "peak"` on `<html>` or any container (`AppShell` takes `app`) for a **product's** screens. `data-app="atlas"` is not a fourth product — it's the parent-brand Action Blue, used for the Atlas Labs maker mark, cross-product surfaces (e.g. a launcher that lists Balance/Connect), and as the fallback when no product context applies. `data-app` swaps **only** the accent ramp; neutrals, type, spacing, radii, elevation and motion never change — that is what keeps the family readable. `data-theme="dark"` is an independent axis with full parity. Adding a product = one block in `tokens/themes.css`.

**The AI channel is exempt:** the assistant is violet (`--ai-*`) in every app, so "this was generated" looks the same everywhere.

---

## CONTENT FUNDAMENTALS

**Voice:** direct, operational, calm — a well-written runbook, not a product tour. It talks to someone who already knows treasury or operations and uses this tool all day.

**Language & person.** Spanish (Spain) first, informal **tú**: "Introduce el código", "Revisa el email y la contraseña". Imperative verbs for actions: *Importar, Exportar, Revisar, Guardar, Entrar*. English mirrors it one-to-one; never mix languages in one screen.

**Casing.** Sentence case everywhere, buttons included. UPPERCASE only as a *typographic* treatment (11px overlines, table headers, KPI labels) — write them in sentence case and let CSS uppercase them. Never Title Case.

**Length.** Buttons 1–3 words, verb first. Page description one sentence. Help text one clause. Empty states two lines: what the surface is for, then the next useful action.

**Numbers & money.** Spanish formatting, always: `1.284.560,12 €` — dot thousands, comma decimals, true minus `−`, tabular mono. **The € symbol always goes after the number, never before** — `1.284.560,12 €`, not `€1.284.560,12`. Positives green, negatives red, **coloured by meaning not by sign** (a rising cost is red). Percentages carry an explicit sign. **A metric without a comparison is not shipped**: "1.284.560,12 € · +3,4% vs. cierre de julio".

**Time.** Relative under a day (`hace 12m`, `4h`), absolute above it (`14 ago`, `2026-08-14`). ISO in record fields, human ranges in prose.

**Errors and consequences.** Name the system and the fact: "Santander devolvió 502. Reintentando en 30s." State the consequence before the action in dialogs: "El acceso se concede de inmediato y caduca en 30 días." No apologies, no exclamation marks, never "Vaya".

**Confirmations.** Past tense plus what happens next: "3 solicitudes aprobadas — se ha avisado a los solicitantes."

**Status vocabulary** (reuse verbatim): Pendiente · En progreso · En revisión · Completada · Cancelada · Bloqueada. Financial: Conciliado · Pendiente · Sin mapear.

**Trust language is matter-of-fact:** "Acceso privado con auditoría", "Cada acceso queda registrado". Stated, never boasted.

**Forbidden:** emoji anywhere, exclamation marks, "simplemente", "solo tienes que", "potente", "sin fricción", marketing cadence, placeholder Latin.

| Bien | No así |
| --- | --- |
| No hay nada que revisar | ¡No se encontraron resultados! 🎉 |
| Las solicitudes nuevas aparecen aquí en menos de un minuto. | Las solicitudes aparecerán pronto. |
| Saldo bajo · Santander operativa · 4.120,08 € bajo el umbral de 10.000 € | Alerta de saldo |
| Santander devolvió 502. Reintentando en 30s. | Algo ha ido mal. Inténtalo de nuevo. |

---

## VISUAL FOUNDATIONS

**Colour.** A warm-neutral grey ramp (`--gray-25 … --gray-950`, ink `#1D1D1F`, parchment `#F5F5F7`) carries every surface, border and text colour; colour is information, not decoration. Three dark tile surfaces (`--tile-1/2/3`) carry the dark bands; micro-steps in lightness separate two dark bands without a border. One accent per app for the single primary action, the active nav item, focus rings, selected rows — nothing else. Five status families (success green, warning amber, danger crimson, info cobalt, neutral grey) as bg/border/text triplets. Money has its own pair (`--amount-positive/negative`). Ten categorical chart colours in fixed order. Violet is reserved for AI.

**Type.** Geist for everything structural, on the Apple ladder: 56 hero, 40 band title, 28 section and lead, 24 airy lead (the rare weight 300), 21 sub-title, **17 body (the default)**, 14 label and caption, 12 overline and fine print, 10 micro-legal. Tracking runs *tighter as size drops* — −0.005em at display, **−0.022em at 17px body** — and opens to +0.08em for overlines. Geist Mono with tabular figures for money, IBANs, IDs, timestamps and every right-aligned column; the KPI numeral is 44px mono semibold. **Weight ladder is 300 / 400 / 600 / 700 — 500 is deliberately absent**, mid-weight reads at 600.

**Spacing & layout.** 4px scale; 8 / 12 / 17 / 24 do most of the work. Cards pad 24 (compact 20), fields 12×20, page gutter 32, **bands 80**. Fixed chrome: 44px global nav (black), 52px frosted sub-nav, 264px sidebar (72px rail, bottom nav under 720px), 64px topbar, content max 1440 (**980 on text-heavy surfaces**), drawer 560, AI panel 420. Rows 44 / compact 36; controls 28 / 34 / 40. Four sanctioned grids: `--kpi` (2fr 1fr 1fr), `--split`, `--detail`, `--2/3/4`.

**Backgrounds.** Flat colour only. No photography, no illustration, no decorative gradients, no textures. The only gradients are functional: the chart area fill and skeleton shimmer. **Bands are the section rhythm:** full-bleed, zero radius, stacked edge to edge — white ↔ parchment ↔ `--tile-1/2/3` — and the change of surface *is* the divider. Never put a border or a shadow between two bands.

**Borders.** 1px, always, three intents: `--border-subtle` inside a card (row dividers), `--border-default` the card's own edge, `--border-strong` hover and controls at rest.

**Shadows.** Effectively none. `xs` / `sm` / `card` / `md` / `control` / `inset-top` all resolve to `none`; cards, buttons, inputs and KPIs are flat with a hairline. Only **detached layers** keep a soft shadow (`lg` menus and toasts, `xl` modals and drawers) because they float over the page. `--shadow-product` (`rgba(0,0,0,.22) 3px 5px 30px`) is the one real shadow in the system and belongs to imagery resting on a surface — never to a card, a button or text.

**Radii.** 0 bands, 5 xs, 8 dark utility buttons, 11 pearl capsules, 12 fields, **18 cards, panels and modals**, 999 for every button, chip, badge, tag and avatar. Card = 18px + 1px hairline + no shadow: the most recognisable Atlas shape. Two button grammars only — the **blue pill** for action and the **compact dark rect** (`--radius-sm`) for nav utility.

**Transparency & blur.** Sparingly, only for layering: modal/drawer scrim at 48% with 4px blur; the **frosted sub-nav** and the sticky bottom bar sit on parchment at 80% with `saturate(180%) blur(20px)`; topbar and bottom nav on an 88% surface `color-mix`. No frosted cards.

**Motion.** One signature easing: `cubic-bezier(.22,1,.36,1)`. Durations 100 / 120 / 180 / 240 / 420ms. Nothing lifts any more — hover changes surface or border colour; **press is `scale(.95)`** on every control; focus is a 3px Action Blue ring at 28% alpha plus a border step. Entrances fade + 8px rise (modals) or 24px slide (drawers). Nothing animates on load, lists never stagger, the only looping motion is the alert dot pulse and the AI typing dots. Everything respects `prefers-reduced-motion`.

**Disabled.** 45% opacity, `not-allowed`, sunken field background.

**Imagery.** There is none, by design. Avatars fall back to initials on a neutral chip. If a product needs imagery, keep it cool-toned, low-contrast, un-filtered.

**Data display.** Numbers right-aligned in tabular mono; status as a dotted badge, never a coloured row; selected rows take the accent tint with a 2px accent edge; max four KPI tiles per row and one `featured`; charts max five series.

---

## ICONOGRAPHY

- **Set:** [Lucide](https://lucide.dev) 0.470.0 — both source systems converge on it (Balance's custom glyphs match Lucide's geometry). 1.5–2px stroke, rounded caps, 24px grid.
- **Delivery:** no local sprite. `Icon` masks the CDN SVG (`unpkg.com/lucide-static@0.470.0/icons/<name>.svg`) with `background-color: currentColor`, so every glyph inherits text colour. **This is a substitution** — no icon assets were supplied.
- **Sizes:** 13–14px in dense cells and small buttons, 15–16px default and in nav, 18–20px in headers and empty-state marks. Never above 20px in app UI.
- **Colour:** `--text-tertiary` at rest, `--text-primary` on hover, `--accent-text` when active, status colours only inside badges, toasts and alert rows.
- **Rules:** icons accompany labels; icon-only controls are `IconButton` with a label and usually a `Tooltip`. Nav items always take an icon; buttons only when the icon clarifies the verb. Leading icons only — trailing is reserved for direction (`chevron-down`, `arrow-right`).
- **Emoji: never.** Unicode as icons: never, except `⌘ ⇧ ↵` inside keyboard hints, set in mono.
- **Common names:** `layout-dashboard` `landmark` `scroll-text` `upload` `download` `building-2` `users` `shield-check` `settings` `bell` `bot` `search` `filter` `calendar-days` `columns-3` `list` `plus` `x` `check` `chevron-down` `chevron-right` `chevron-left` `ellipsis` `arrow-up-right` `arrow-down-right` `arrow-right` `circle-check` `triangle-alert` `circle-alert` `info` `file-text` `trash-2` `log-out` `moon` `sun`.

### Substitutions — needs your input

| Role | Using | Why |
| --- | --- | --- |
| UI / display | **Geist** (Google Fonts) | No brand font supplied. Stands in for SF Pro Display / SF Pro Text: same neutral grotesque skeleton, takes the negative tracking well. `system-ui, -apple-system` follows in the stack, so Apple platforms resolve to the real SF Pro. |
| Numerics / code | **Geist Mono** (Google Fonts) | Tabular figures, same skeleton as the sans, so mixed rows align. |
| Icons | **Lucide** via CDN | No icon assets supplied; matches both source systems. |
| Logo | Typographic wordmark + two-letter mark | No logo files exist. Nothing was drawn. |

Fonts load from the Google CSS API, so the closure declares **no `@font-face` rules and ships no binaries**. If Atlas Labs has real typefaces or a logo, send the `woff2` / SVG files: they self-host in `assets/` with a `tokens/fonts.css` and no other file changes.
