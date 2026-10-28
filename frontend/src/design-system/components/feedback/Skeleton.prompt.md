Shape the skeleton like the content that will land: a KPI tile is overline (12px) + numeral (34px) + meta (14px); a table row is 56px. Shimmer stops under reduced motion.
```jsx
<Card><Skeleton width={90} height={12} /><Skeleton width="70%" height={34} style={{ margin: '12px 0' }} /><Skeleton lines={2} /></Card>
```
