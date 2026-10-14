import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function Select({ label, hint, error, required, options = [], placeholder, disabled, id, style, value, defaultValue, onChange, name }) {
  const fid = id || (label ? 's-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const opts = options.map(o => typeof o === 'string' ? { value: o, label: o } : o);
  const [inner, setInner] = React.useState(defaultValue ?? (placeholder ? '' : opts[0]?.value ?? ''));
  const cur = value !== undefined ? value : inner;
  const [open, setOpen] = React.useState(false);
  const [hi, setHi] = React.useState(0);
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (!open) return;
    const h = e => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener('mousedown', h); return () => document.removeEventListener('mousedown', h);
  }, [open]);
  const pick = v => { setInner(v); setOpen(false); onChange && onChange({ target: { value: v, name } }); };
  const sel = opts.find(o => String(o.value) === String(cur));
  const openMenu = () => { if (disabled) return; setHi(Math.max(0, opts.findIndex(o => String(o.value) === String(cur)))); setOpen(true); };
  const onKey = e => {
    if (disabled) return;
    if (e.key === 'Escape') setOpen(false);
    else if (e.key === 'ArrowDown') { e.preventDefault(); open ? setHi(i => Math.min(opts.length - 1, i + 1)) : openMenu(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); open && setHi(i => Math.max(0, i - 1)); }
    else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open ? pick(opts[hi].value) : openMenu(); }
  };
  return <div className="ar-field" style={style} ref={ref}>
    {label && <label className="ar-label" htmlFor={fid}>{label}{required && <em> *</em>}</label>}
    <div className="ar-pop">
      <button type="button" id={fid} disabled={disabled} aria-haspopup="listbox" aria-expanded={open} onClick={() => open ? setOpen(false) : openMenu()} onKeyDown={onKey}
        className={'ar-control ar-select' + (error ? ' ar-control--error' : '') + (disabled ? ' ar-control--disabled' : '') + (open ? ' is-open' : '')}>
        <span className={'ar-select__value' + (sel ? '' : ' is-placeholder')}>{sel ? sel.label : placeholder || ''}</span>
        <Icon name="chevron-down" size={15} />
      </button>
      {name && <input type="hidden" name={name} value={cur} />}
      {open && <ul className="ar-menu" role="listbox">
        {opts.map((o, i) => <li key={o.value} role="option" aria-selected={String(o.value) === String(cur)} data-hi={i === hi} onMouseEnter={() => setHi(i)} onMouseDown={e => { e.preventDefault(); pick(o.value); }}>
          <span>{o.label}</span>{String(o.value) === String(cur) && <Icon name="check" size={14} />}
        </li>)}
      </ul>}
    </div>
    {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
  </div>;
}
