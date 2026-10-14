import React from 'react';
import { Money } from './Money.jsx';
import { Button } from '../core/Button.jsx';
const n = v => Number(v) || 0;
export function PaymentAllocation({ amount = 0, currency = 'USD', invoices = [], onChange, style }) {
  const applied = invoices.reduce((s, i) => s + n(i.applied), 0), credit = amount - applied;
  const auto = () => { let left = amount; onChange && onChange(invoices.map(i => { const a = Math.max(0, Math.min(n(i.pending), left)); left -= a; return { ...i, applied: a }; })); };
  const set = (k, v) => onChange && onChange(invoices.map((i, j) => j === k ? { ...i, applied: v } : i));
  return <div style={style}>
    <div className="ar-alloc__head"><div><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>Importe recibido</div><Money amount={amount} currency={currency} strong style={{ fontSize: 21 }} /></div><Button variant="ghost" size="sm" icon="list-ordered" onClick={auto}>Aplicar a la más antigua</Button></div>
    <div style={{ marginTop: 6 }}>{invoices.map((i, k) => { const rest = n(i.pending) - n(i.applied); return <div key={i.id} className="ar-alloc__row">
      <div style={{ minWidth: 0 }}><div style={{ font: 'var(--type-mono)', color: 'var(--text-strong)' }}>{i.id}</div><div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>{[i.concept, i.period].filter(Boolean).join(' · ')}</div></div>
      <div style={{ textAlign: 'right' }}><Money amount={i.pending} currency={currency} tone={i.overdue ? 'danger' : undefined} /><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>{rest <= 0 ? 'Quedará pagada' : 'Quedan ' + rest.toLocaleString('de-DE', { minimumFractionDigits: 2 })}</div></div>
      <div className="ar-control" style={{ height: 40, padding: '0 12px' }}><span className="ar-control__affix" style={{ order: 2 }}>{currency === 'DOP' ? 'DOP' : 'USD'}</span><input inputMode="decimal" value={i.applied ?? ''} placeholder="0,00" onChange={e => set(k, e.target.value)} style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontSize: 14 }} /></div>
    </div>; })}</div>
    <dl className="ar-totals" style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border-default)' }}>
      <div><dt>Aplicado a facturas</dt><dd><Money amount={applied} currency={currency} /></dd></div>
      <div className="is-total"><dt>{credit < 0 ? 'Excede el importe recibido' : 'Saldo a favor'}</dt><dd><Money amount={credit} currency={currency} strong tone={credit < 0 ? 'danger' : credit > 0 ? 'success' : 'muted'} /></dd></div>
    </dl>
  </div>;
}