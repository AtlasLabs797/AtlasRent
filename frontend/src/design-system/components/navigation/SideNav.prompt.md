Primary navigation for every Atlas Rent screen. Groups follow the spec: **Operación** (Inicio, Inmuebles y unidades, Clientes, Cotizaciones, Reservas, Contratos), **Finanzas** (Facturas, Cobros, Fianzas, Informes), **Control** (Aprobaciones IA, Bandeja de revisión, Auditoría, Migración).
- Active item = accent tint. Counts: `countTone="ai"` only on Aprobaciones IA, `danger` for vencidas.
- Hide items the role can't use (Empleado: no Cobros/Informes) instead of disabling them.
```jsx
<SideNav active="facturas" onNavigate={go} user={{name:'Carmen Reyes',role:'Gerente'}} groups={[{label:'Finanzas',items:[{key:'facturas',icon:'file-text',label:'Facturas'}]}]}/>
```