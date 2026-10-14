import React from 'react';
import { Icon } from '../core/Icon.jsx';
const MESES = ['enero','febrero','marzo','abril','mayo','junio','julio','agosto','septiembre','octubre','noviembre','diciembre'];
const DIAS = ['L','M','X','J','V','S','D'];
const p2 = n => String(n).padStart(2, '0');
const toIso = d => d.getFullYear() + '-' + p2(d.getMonth() + 1) + '-' + p2(d.getDate());
const fromIso = s => { const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || ''); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; };
const fmt = s => { const d = fromIso(s); return d ? p2(d.getDate()) + '/' + p2(d.getMonth() + 1) + '/' + d.getFullYear() : ''; };
const parse = t => { const m = /^(\d{1,2})[\/.\-](\d{1,2})[\/.\-](\d{4})$/.exec(t.trim()); if (!m) return null; const d = new Date(+m[3], +m[2] - 1, +m[1]); return d.getMonth() === +m[2] - 1 ? toIso(d) : null; };
export function DatePicker({ label, hint, error, required, disabled, id, style, value, defaultValue, onChange, name, min, max, placeholder = 'dd/mm/aaaa' }) {
  const fid = id || (label ? 'd-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const [inner, setInner] = React.useState(defaultValue || '');
  const cur = value !== undefined ? value : inner;
  const [text, setText] = React.useState(fmt(cur));
  const [open, setOpen] = React.useState(false);
  const [view, setView] = React.useState(() => fromIso(cur) || new Date());
  const ref = React.useRef(null);
  React.useEffect(() => { setText(fmt(cur)); }, [cur]);
  React.useEffect(() => {
    if (!open) return;
    const h = e => { if (ref.current && !ref.current.contains(e.target)) { setOpen(false); setText(fmt(cur)); } };
    document.addEventListener('mousedown', h); return () => document.removeEventListener('mousedown', h);
  }, [open, cur]);
  const commit = iso => { setInner(iso); setText(fmt(iso)); onChange && onChange({ target: { value: iso, name } }); };
  const ok = iso => (!min || iso >= min) && (!max || iso <= max);
  const onText = e => {
    let t = e.target.value; setText(t);
    const iso = parse(t);
    if (iso && ok(iso)) { commit(iso); setView(fromIso(iso)); }
  };
  const blur = () => { const iso = parse(text); if (iso && ok(iso)) commit(iso); else if (!text.trim()) commit(''); else setText(fmt(cur)); };
  const y = view.getFullYear(), m = view.getMonth();
  const lead = (new Date(y, m, 1).getDay() + 6) % 7, days = new Date(y, m + 1, 0).getDate();
  const cells = [...Array(lead).fill(null), ...Array.from({ length: days }, (_, i) => i + 1)];
  const today = toIso(new Date());
  const nav = n => setView(new Date(y, m + n, 1));
  return <div className="ar-field" style={style} ref={ref}>
    {label && <label className="ar-label" htmlFor={fid}>{label}{required && <em> *</em>}</label>}
    <div className="ar-pop">
      <div className={'ar-control' + (error ? ' ar-control--error' : '') + (disabled ? ' ar-control--disabled' : '')}>
        <input id={fid} type="text" inputMode="numeric" autoComplete="off" disabled={disabled} placeholder={placeholder} value={text} onChange={onText} onBlur={blur}
          onFocus={() => !disabled && setOpen(true)} onKeyDown={e => { if (e.key === 'Escape') setOpen(false); if (e.key === 'Enter') { blur(); setOpen(false); } }} />
        <button type="button" className="ar-control__btn" aria-label="Abrir calendario" disabled={disabled} onClick={() => setOpen(o => !o)}><Icon name="calendar-days" size={15} /></button>
      </div>
      {open && <div className="ar-menu ar-cal">
        <div className="ar-cal__head">
          <button type="button" aria-label="Mes anterior" onClick={() => nav(-1)}><Icon name="chevron-left" size={15} /></button>
          <strong>{MESES[m]} {y}</strong>
          <button type="button" aria-label="Mes siguiente" onClick={() => nav(1)}><Icon name="chevron-right" size={15} /></button>
        </div>
        <div className="ar-cal__grid">
          {DIAS.map(d => <span key={d} className="ar-cal__dow">{d}</span>)}
          {cells.map((d, i) => { if (!d) return <span key={'e' + i} />; const iso = y + '-' + p2(m + 1) + '-' + p2(d);
            return <button type="button" key={iso} disabled={!ok(iso)} data-sel={iso === cur} data-today={iso === today} onClick={() => { commit(iso); setOpen(false); }}>{d}</button>; })}
        </div>
        <div className="ar-cal__foot"><button type="button" onClick={() => { commit(today); setView(new Date()); setOpen(false); }}>Hoy</button>{cur && <button type="button" onClick={() => { commit(''); setOpen(false); }}>Borrar</button>}</div>
      </div>}
    </div>
    {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
  </div>;
}
