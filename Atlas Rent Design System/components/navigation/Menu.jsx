import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function Menu({ trigger, items = [], align = 'left', tone, onSelect, style }) {
  const [open, setOpen] = React.useState(false);
  const [hi, setHi] = React.useState(-1);
  const ref = React.useRef(null);
  const live = items.map((it, i) => ({ it, i })).filter(x => !x.it.separator && !x.it.heading && !x.it.disabled);
  React.useEffect(() => {
    if (!open) return;
    const h = e => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const k = e => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', h); document.addEventListener('keydown', k);
    return () => { document.removeEventListener('mousedown', h); document.removeEventListener('keydown', k); };
  }, [open]);
  const pick = it => { setOpen(false); it.onClick && it.onClick(); onSelect && onSelect(it.value ?? it.label, it); };
  const onKey = e => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); setOpen(true); setHi(live[0]?.i ?? -1); return; }
    if (!open) return;
    const pos = live.findIndex(x => x.i === hi);
    if (e.key === 'ArrowDown') { e.preventDefault(); setHi(live[Math.min(live.length - 1, pos + 1)]?.i); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setHi(live[Math.max(0, pos - 1)]?.i); }
    else if (e.key === 'Enter') { e.preventDefault(); const x = items[hi]; x && pick(x); }
  };
  const trig = React.isValidElement(trigger) ? React.cloneElement(trigger, { onClick: () => setOpen(o => !o), onKeyDown: onKey, 'aria-haspopup': 'menu', 'aria-expanded': open }) : trigger;
  return <div className="ar-pop" ref={ref} style={{ display: 'inline-flex', ...style }}>
    {trig}
    {open && <ul className={'ar-menu ar-menu--actions' + (align === 'right' ? ' ar-menu--right' : '')} role="menu">
      {items.map((it, i) => it.separator ? <li key={'s' + i} className="ar-menu__sep" role="separator" />
        : it.heading ? <li key={'h' + i} className="ar-menu__label" role="presentation">{it.heading}</li>
        : <li key={(it.value ?? it.label) + i} role="menuitem" aria-disabled={it.disabled || undefined} data-tone={it.danger ? 'danger' : (it.tone || tone)} data-hi={i === hi}
            onMouseEnter={() => !it.disabled && setHi(i)} onMouseDown={e => { e.preventDefault(); if (!it.disabled) pick(it); }}>
            {it.icon && <Icon name={it.icon} size={15} />}<span className="ar-menu__text">{it.label}</span>{it.meta && <span className="ar-menu__meta">{it.meta}</span>}
          </li>)}
    </ul>}
  </div>;
}
