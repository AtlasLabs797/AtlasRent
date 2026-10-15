Main list view for clientes, unidades, facturas, cobros. Put inside <Card padded={false}>.
```jsx
<DataTable columns={[{key:'num',label:'Factura'},{key:'total',label:'Importe',align:'right',render:r=><Money amount={r.total}/>}]} rows={rows} />
```
Right-align money columns. IDs render in Geist Mono.
