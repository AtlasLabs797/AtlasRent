# Atlas Rent — web app UI kit
Internal back-office for ~150 rental units (spec: `Atlas Rent/especificacion_app_gestion_alquileres.txt`). No existing UI code was supplied, so these screens are **designed from the functional spec**, not recreated from production.

Screens (click-through in `index.html`, sidebar routes):
- **Inicio** (`Dashboard.jsx`) — KPI tiles USD/DOP/consolidado, ocupación bar, facturas vencidas, alertas persistentes.
- **Inmuebles y unidades** (`Units.jsx`) — status tabs, list/grid toggle, detail panel with contextual IA menu.
- **Facturas** (`Invoices.jsx`) — series/estado filters, Cobro + Vencida badges, "Registrar cobro" dialog → toast.
- **Aprobaciones IA** (`Approvals.jsx`) — agent approval cards (Aprobar / Rechazar / Modificar y aprobar).
- `Shell.jsx` — Sidebar (dark ink), Topbar (search, FX rate, Chat IA), PageHead.
Other sidebar routes render a labelled placeholder.
