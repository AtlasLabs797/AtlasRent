import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function Breadcrumbs({ items = [], style }) {
  return <nav aria-label="Ruta" className="ar-crumbs" style={style}>{items.map((it, i) => {
    const last = i === items.length - 1;
    return <React.Fragment key={i}>{last ? <span aria-current="page">{it.label}</span> : <button type="button" onClick={it.onClick}>{it.label}</button>}{!last && <Icon name="chevron-right" size={12} style={{ color: 'var(--text-subtle)' }} />}</React.Fragment>;
  })}</nav>;
}