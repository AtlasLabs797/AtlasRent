import React from 'react';
import { Icon } from '../core/Icon.jsx';
const norm = s => String(s ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
export function Combobox({ label, hint, error, required, placeholder = 'Buscar', options = [], value, onChange, onCreate, createLabel = 'Crear', icon = 'search', emptyText = 'Sin coincidencias', disabled, id, style, onSearch }) {
  const fid = id || (label ? 'cb-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const [inner, setInner] = React.useState(null);
  const cur = value !== undefined ? value : inner;
  const sel = options.find(o => o.value === cur);
  const [q, setQ] = React.useState('');
  const [open, setOpen] = React.useState(false);
  const [hi, setHi] = React.useState(0);
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (!open) return;
    const h = e => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener('mousedown', h); return () => document.removeEventListener('mousedown', h);
  }, [open]);
  const list = options.filter(o => onSearch || !q || norm(o.label + ' ' + (o.sub || '') + ' ' + (o.meta || '')).includes(norm(q))).slice(0, 50);
  const canCreate = onCreate && q.trim() && !list.some(o => norm(o.label) === norm(q));
  const n = list.length + (canCreate ? 1 : 0);
  const pick = o => { setInner(o.value); setQ(''); onSearch && onSearch(''); setOpen(false); onChange && onChange(o.value, o); };
  const create = () => { setOpen(false); onCreate(q.trim()); setQ(''); };
  const onKey = e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setHi(i => Math.min(n - 1, i + 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setHi(i => Math.max(0, i - 1)); }
    else if (e.key === 'Enter' && open) { e.preventDefault(); hi < list.length ? list[hi] && pick(list[hi]) : canCreate && create(); }
    else if (e.key === 'Escape') setOpen(false);
  };
  return <div className="ar-field" style={style} ref={ref}>
    {label && <label className="ar-label" htmlFor={fid}>{label}{required && <em> *</em>}</label>}
    <div className="ar-pop">
      <div className={'ar-control' + (error ? ' ar-control--error' : '') + (disabled ? ' ar-control--disabled' : '')}>
        {icon && <Icon name={icon} size={15} />}
        <input id={fid} role="combobox" aria-expanded={open} aria-autocomplete="list" autoComplete="off" disabled={disabled}
          placeholder={sel ? '' : placeholder} value={open ? q : (sel ? sel.label : q)}
          onFocus={() => { setOpen(true); setHi(0); }} onChange={e => { setQ(e.target.value); setOpen(true); setHi(0); onSearch && onSearch(e.target.value); }} onKeyDown={onKey} />
        {sel && sel.meta && !open && <span className="ar-control__affix">{sel.meta}</span>}
        {sel && !disabled && <button type="button" className="ar-control__btn" aria-label="Quitar selección" onClick={() => { setInner(null); onChange && onChange(null, null); }}><Icon name="x" size={14} /></button>}
      </div>
      {open && <ul className="ar-menu ar-menu--rich" role="listbox">
        {list.map((o, i) => <li key={o.value} role="option" aria-selected={o.value === cur} data-hi={i === hi} onMouseEnter={() => setHi(i)} onMouseDown={e => { e.preventDefault(); pick(o); }}>
          <span className="ar-opt"><span className="ar-opt__title">{o.label}</span>{o.sub && <span className="ar-opt__sub">{o.sub}</span>}</span>
          {o.meta && <span className="ar-menu__meta">{o.meta}</span>}{o.value === cur && <Icon name="check" size={14} />}
        </li>)}
        {list.length === 0 && !canCreate && <li className="ar-menu__empty">{emptyText}</li>}
        {canCreate && <li className="ar-menu__create" data-hi={hi === list.length} onMouseEnter={() => setHi(list.length)} onMouseDown={e => { e.preventDefault(); create(); }}><Icon name="plus" size={14} /><span className="ar-menu__text">{createLabel} «{q.trim()}»</span></li>}
      </ul>}
    </div>
    {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
  </div>;
}
