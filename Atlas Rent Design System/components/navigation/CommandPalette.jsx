import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Kbd } from '../core/Kbd.jsx';
const norm = s => String(s ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
export function CommandPalette({ open, onOpenChange, groups = [], onSelect, placeholder = 'Buscar cliente, unidad, factura o acción', emptyText = 'Sin resultados. Prueba con el RNC, el nº de factura o el nombre de la unidad.', hotkey = true, inline }) {
  const [q, setQ] = React.useState('');
  const [hi, setHi] = React.useState(0);
  const inputRef = React.useRef(null);
  React.useEffect(() => {
    if (!hotkey) return;
    const k = e => { if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); onOpenChange && onOpenChange(!open); } else if (e.key === 'Escape' && open) onOpenChange && onOpenChange(false); };
    document.addEventListener('keydown', k); return () => document.removeEventListener('keydown', k);
  }, [open, hotkey, onOpenChange]);
  React.useEffect(() => { if (open) { setQ(''); setHi(0); setTimeout(() => inputRef.current && !inline && inputRef.current.focus(), 0); } }, [open]);
  if (!open && !inline) return null;
  const filtered = groups.map(g => ({ ...g, items: g.items.filter(it => !q || norm(it.label + ' ' + (it.sub || '') + ' ' + (it.meta || '')).includes(norm(q))) })).filter(g => g.items.length);
  const flat = filtered.flatMap(g => g.items);
  const pick = it => { onSelect && onSelect(it.value ?? it.label, it); onOpenChange && onOpenChange(false); };
  const onKey = e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setHi(i => Math.min(flat.length - 1, i + 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setHi(i => Math.max(0, i - 1)); }
    else if (e.key === 'Enter') { e.preventDefault(); flat[hi] && pick(flat[hi]); }
  };
  let n = -1;
  const panel = <div className={'ar-cmd' + (inline ? ' ar-cmd--inline' : '')} role="dialog" aria-label="Búsqueda global" onClick={e => e.stopPropagation()}>
    <div className="ar-cmd__input"><Icon name="search" size={18} /><input ref={inputRef} value={q} placeholder={placeholder} onChange={e => { setQ(e.target.value); setHi(0); }} onKeyDown={onKey} /><Kbd>esc</Kbd></div>
    {flat.length === 0 ? <div className="ar-cmd__empty">{emptyText}</div> :
      <ul className="ar-cmd__list" role="listbox">{filtered.map(g => <React.Fragment key={g.label}>
        <li className="ar-overline ar-cmd__group" role="presentation">{g.label}</li>
        {g.items.map(it => { n++; const i = n; return <li key={g.label + (it.value ?? it.label)} role="option" aria-selected={i === hi} className="ar-cmd__item" data-hi={i === hi} data-tone={it.tone} onMouseEnter={() => setHi(i)} onClick={() => pick(it)}>
          <span className="ar-cmd__icon"><Icon name={it.icon || 'arrow-right'} size={16} /></span>
          <span className="ar-cmd__text"><b>{it.label}</b>{it.sub && <span>{it.sub}</span>}</span>
          {it.meta && <span className="ar-cmd__meta">{it.meta}</span>}
        </li>; })}
      </React.Fragment>)}</ul>}
    <div className="ar-cmd__foot"><span><Kbd>↑</Kbd><Kbd>↓</Kbd>navegar</span><span><Kbd>↵</Kbd>abrir</span><span><Kbd>⌘</Kbd><Kbd>K</Kbd>cerrar</span></div>
  </div>;
  return inline ? panel : <div className="ar-cmd-scrim" onClick={() => onOpenChange && onOpenChange(false)}>{panel}</div>;
}
