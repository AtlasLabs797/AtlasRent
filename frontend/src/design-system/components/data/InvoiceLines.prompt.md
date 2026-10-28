Factura manual, edición de factura definitiva (Gerente/Admin) and duplicar factura. Invoice number is never editable — keep it in the header, not here. Electricidad/Agua go on separate invoices.
```jsx
<Card padded={false}><InvoiceLines editable lines={lines} onChange={setLines} rate={62}/></Card>
```