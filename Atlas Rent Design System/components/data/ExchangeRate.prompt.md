Once an invoice is issued its rate is frozen — always pass `frozenIn` on issued documents so nobody expects it to update.
```jsx
<ExchangeRate market={60.35} margin={1.65}/>
<ExchangeRate variant="panel" market={60.35} margin={1.65} at="08/10/2026 08:00" frozenIn="FAC-A-2026-00412"/>
```