import React from 'react';
export function KeyValueList({ items = [], columns = 2, style }) {
  return <dl className="ar-kv" style={{ gridTemplateColumns: 'repeat(' + columns + ',minmax(0,1fr))', ...style }}>{items.filter(Boolean).map((it, i) => <div key={i} style={{ gridColumn: it.span ? '1 / -1' : undefined, minWidth: 0 }}><dt>{it.label}</dt><dd className={it.mono ? 'is-mono' : ''}>{it.value ?? '—'}</dd></div>)}</dl>;
}