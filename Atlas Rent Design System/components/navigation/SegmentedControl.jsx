import React from 'react';
export function SegmentedControl({ options = [], value, onChange, style }) {
  return <div className="ar-seg" style={style}>{options.map(o => {
    const k = typeof o === 'string' ? o : o.value; const l = typeof o === 'string' ? o : o.label;
    return <button key={k} aria-pressed={k === value} onClick={() => onChange && onChange(k)}>{l}</button>;
  })}</div>;
}
