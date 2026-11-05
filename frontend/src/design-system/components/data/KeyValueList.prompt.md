IDs, documents (RNC/Cédula), IBAN-like numbers and medidores take `mono`. Missing values render "—".
```jsx
<KeyValueList columns={3} items={[{label:'Nº cliente',value:'01873',mono:true},{label:'Tipo',value:'Empresa'},{label:'RNC',value:'1-31-45678-9',mono:true}]}/>
```