Every Atlas Rent screen sits in AppShell. Don't hand-roll the sidebar/topbar flex layout.
```jsx
<AppShell nav={{ groups, active: route, onNavigate: go, user }} topbar={{ rate: { market: 60.35, margin: 1.65 }, notifications: 4, onOpenSearch: () => setCmd(true) }}>
  <PageHeader eyebrow="Finanzas" title="Facturas" description="Octubre 2026 · Series A, B, C" />
  …
</AppShell>
```
Use `narrow` for alta/edición forms and settings.
