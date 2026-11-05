Business date field (vencimiento, inicio de contrato, fecha de cobro). Typed by hand as dd/mm/aaaa or picked from the calendar; the value is always ISO `yyyy-mm-dd` (maps to `DateOnly` / `date`).
```jsx
<DatePicker label="Vencimiento" name="vencimiento" value={venc} min={emision} onChange={e => setVenc(e.target.value)} required />
```
Use `min`/`max` (ISO) for ranges such as fin ≥ inicio. Never for timestamps: those are shown read-only in mono. Errors go in `error`, not in a toast.
