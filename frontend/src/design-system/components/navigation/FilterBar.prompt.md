Spec §16.5 filters: fecha, empresa facturadora/propietaria, inmueble, unidad, cliente, contrato, moneda, estado, serie. Put 2–4 Selects as children; everything else lives in chips.
```jsx
<Card padded={false}><FilterBar searchPlaceholder="Nº factura, cliente o unidad" filters={[{key:'serie',label:'Serie',value:'A · Renta'}]}><Select options={['Moneda','USD','DOP']}/></FilterBar><DataTable …/></Card>
```