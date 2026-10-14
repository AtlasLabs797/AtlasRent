import React from 'react';
import { IconButton } from '../core/IconButton.jsx';
export function Pagination({ page = 1, pageSize = 25, total = 0, onChange, style }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = total ? (page - 1) * pageSize + 1 : 0, to = Math.min(total, page * pageSize);
  const list = []; for (let p = 1; p <= pages; p++) if (p === 1 || p === pages || Math.abs(p - page) <= 1) list.push(p); else if (list[list.length - 1] !== '…') list.push('…');
  const go = p => onChange && p >= 1 && p <= pages && onChange(p);
  return <div className="ar-pager" style={style}>
    <span className="mono">{from}–{to} de {total}</span>
    <div className="ar-pager__pages">
      <IconButton size="sm" icon="chevron-left" label="Anterior" disabled={page <= 1} onClick={() => go(page - 1)} />
      {list.map((p, i) => p === '…' ? <span key={'e' + i} style={{ padding: '0 4px' }}>…</span> : <button key={p} type="button" className="ar-pager__page" aria-current={p === page ? 'page' : undefined} onClick={() => go(p)}>{p}</button>)}
      <IconButton size="sm" icon="chevron-right" label="Siguiente" disabled={page >= pages} onClick={() => go(page + 1)} />
    </div>
  </div>;
}