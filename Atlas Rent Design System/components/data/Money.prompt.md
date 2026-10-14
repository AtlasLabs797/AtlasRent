Render every monetary value through Money so USD/DOP formatting and alignment stay consistent.
```jsx
<Money amount={450} currency="USD" rate={62} />
<Money amount={27900} currency="DOP" />
```
Currency codes USD and DOP, shown after the number. Always 2 decimals, dot thousands, comma decimal.
