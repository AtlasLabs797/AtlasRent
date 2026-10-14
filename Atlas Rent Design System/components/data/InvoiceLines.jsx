import React from 'react';
import { Money } from './Money.jsx';
import { IconButton } from '../core/IconButton.jsx';
import { Button } from '../core/Button.jsx';
const CONCEPTS = ['Renta mensual', 'Prorrateo', 'Mora', 'Ajuste', 'Servicio', 'Cargo adicional', 'Electricidad', 'Agua', 'Descuento'];
export function InvoiceLines({ lines = [], currency = 'USD', rate, editable = false, onChange, concepts = CONCEPTS, style }) {
  const val = l => (l.concept === 'Descuento' ? -1 : 1) * (Number(l.qty) || 0) * (Number(l.price) || 0);
  const total = lines.reduce((s, l) => s + val(l), 0), disc = lines.reduce((s, l) => s + Math.min(0, val(l)), 0);
  const set = (i, k, v) => onChange && onChange(lines.map((l, j) => j === i ? { ...l, [k]: v } : l));
  return <div style={style}>
    <div style={{ overflowX: 'auto' }}><table className="ar-table ar-lines__table"><thead><tr><th>Concepto</th><th>Detalle</th><th className="is-num" style={{ width: 90 }}>Cant.</th><th className="is-num" style={{ width: 150 }}>Precio</th><th className="is-num" style={{ width: 160 }}>Importe</th>{editable && <th style={{ width: 52 }} />}</tr></thead>
      <tbody>{lines.map((l, i) => <tr key={i}>
        <td>{editable ? <select className="ar-cell-input" value={l.concept} onChange={e => set(i, 'concept', e.target.value)}>{concepts.map(c => <option key={c}>{c}</option>)}</select> : <span style={{ color: 'var(--text-strong)' }}>{l.concept}</span>}</td>
        <td>{editable ? <input className="ar-cell-input" value={l.detail || ''} placeholder="Periodo o nota" onChange={e => set(i, 'detail', e.target.value)} /> : <span style={{ color: 'var(--text-muted)' }}>{l.detail}</span>}</td>
        <td className="is-num">{editable ? <input className="ar-cell-input is-num" inputMode="decimal" value={l.qty} onChange={e => set(i, 'qty', e.target.value)} /> : l.qty}</td>
        <td className="is-num">{editable ? <input className="ar-cell-input is-num" inputMode="decimal" value={l.price} onChange={e => set(i, 'price', e.target.value)} /> : Number(l.price).toLocaleString('de-DE', { minimumFractionDigits: 2 })}</td>
        <td className="is-num"><Money amount={val(l)} currency={currency} tone={val(l) < 0 ? 'danger' : undefined} /></td>
        {editable && <td><IconButton size="sm" icon="trash-2" label="Quitar línea" onClick={() => onChange(lines.filter((_, j) => j !== i))} /></td>}
      </tr>)}</tbody></table></div>
    <div className="ar-lines__foot">
      {editable ? <Button variant="ghost" size="sm" icon="plus" onClick={() => onChange([...lines, { concept: 'Cargo adicional', detail: '', qty: 1, price: 0 }])}>Añadir línea</Button> : <span />}
      <dl className="ar-totals">
        {disc < 0 && <div><dt>Descuentos</dt><dd><Money amount={disc} currency={currency} tone="danger" /></dd></div>}
        <div className="is-total"><dt>Total</dt><dd><Money amount={total} currency={currency} strong /></dd></div>
        {rate && currency === 'USD' && <div><dt>Equivalente · {Number(rate).toFixed(2).replace('.', ',')}</dt><dd><Money amount={total * rate} currency="DOP" tone="muted" /></dd></div>}
      </dl>
    </div>
  </div>;
}