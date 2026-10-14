Use instead of Select when the list is long or a record has to be found by several keys (clientes, unidades, contratos). Each option shows name, a sub line and the mono ID.
```jsx
<Combobox label="Cliente" required placeholder="Nombre, RNC o nº cliente" options={[
  { value: 'CLI-01873', label: 'Inversiones Marlin SRL', sub: 'RNC 1-31-45678-2', meta: 'CLI-01873' },
]} onCreate={q => abrirAltaCliente(q)} createLabel="Crear cliente" />
```
Select is for ≤ 8 fixed options (forma de pago, serie). Combobox for anything searchable.
