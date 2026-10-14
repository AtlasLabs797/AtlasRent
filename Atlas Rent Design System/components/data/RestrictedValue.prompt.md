Wrap aggregated totals only (facturado, cobrado, pendiente totals, fianzas retenidas). Individual invoice amounts stay visible to Empleado. Permissions are enforced in the backend — this only explains the gap.
```jsx
<StatCard label="Cobrado" value={<RestrictedValue allowed={role!=='empleado'}>162.075 USD</RestrictedValue>}/>
```