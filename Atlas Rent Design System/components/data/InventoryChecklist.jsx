import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Money } from './Money.jsx';
const ST = [['ok', 'OK'], ['faltante', 'Faltante', 'warning'], ['danado', 'Dañado', 'danger']];
export function InventoryChecklist({ items = [], currency = 'USD', editable = false, onChange, frozenFrom, style }) {
  const set = (i, k, v) => onChange && onChange(items.map((it, j) => j === i ? { ...it, [k]: v } : it));
  const bad = items.filter(i => i.state && i.state !== 'ok'), total = bad.reduce((s, i) => s + (Number(i.amount) || 0), 0), checked = items.filter(i => i.state).length;
  return <div style={style}>
    {frozenFrom && <div style={{ display: 'flex', alignItems: 'center', gap: 6, font: 'var(--type-caption)', color: 'var(--text-muted)', marginBottom: 8 }}><Icon name="lock" size={13} />Copia congelada de {frozenFrom}</div>}
    {items.map((it, i) => <div key={i} className="ar-inv__row">
      <div style={{ minWidth: 0 }}><div style={{ font: 'var(--type-body-sm)', color: 'var(--text-strong)' }}>{it.name}{it.qty > 1 && <span className="mono" style={{ color: 'var(--text-subtle)' }}> × {it.qty}</span>}</div>{it.note && <div style={{ font: 'var(--type-fine)', color: 'var(--text-muted)' }}>{it.note}</div>}</div>
      {editable ? <div className="ar-mini-seg">{ST.map(([k, l, t]) => <button key={k} type="button" data-tone={t} aria-pressed={it.state === k} onClick={() => set(i, 'state', k)}>{l}</button>)}</div> : <span className={'ar-badge'} style={it.state && it.state !== 'ok' ? { background: 'var(--' + (it.state === 'danado' ? 'danger' : 'warning') + '-soft)', color: 'var(--' + (it.state === 'danado' ? 'danger' : 'warning') + ')' } : undefined}>{(ST.find(s => s[0] === it.state) || [0, 'Sin revisar'])[1]}</span>}
      <div style={{ textAlign: 'right' }}>{it.state && it.state !== 'ok' ? (editable ? <div className="ar-control" style={{ height: 36, padding: '0 10px' }}><span className="ar-control__affix" style={{ order: 2 }}>{currency === 'DOP' ? 'DOP' : 'USD'}</span><input inputMode="decimal" placeholder="0,00" value={it.amount ?? ''} onChange={e => set(i, 'amount', e.target.value)} style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontSize: 14 }} /></div> : <Money amount={-(Number(it.amount) || 0)} currency={currency} tone="danger" />) : <span style={{ color: 'var(--text-subtle)' }}>—</span>}</div>
    </div>)}
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12, paddingTop: 14, font: 'var(--type-body-sm)', color: 'var(--text-muted)' }}><span className="mono">{checked}/{items.length} revisados · {bad.length} con incidencia</span><span>Descuento propuesto a fianza <Money amount={total} currency={currency} strong tone={total ? 'danger' : 'muted'} /></span></div>
  </div>;
}