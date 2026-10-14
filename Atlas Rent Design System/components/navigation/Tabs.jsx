import React from 'react';
export function Tabs({ tabs = [], value, onChange, style }) {
  return <div className="ar-tabs" role="tablist" style={style}>{tabs.map(t => {
    const k = t.value ?? t.label;
    return <button key={k} role="tab" className="ar-tab" aria-selected={k === value} onClick={() => onChange && onChange(k)}>{t.label}{t.count != null && <span className="ar-tab__count">{t.count}</span>}</button>;
  })}</div>;
}
