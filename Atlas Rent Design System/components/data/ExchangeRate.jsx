import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { KeyValueList } from './KeyValueList.jsx';
const f = n => Number(n).toFixed(2).replace('.', ',');
export function ExchangeRate({ market, margin = 0, final, at, source, frozenIn, variant = 'inline', style }) {
  const fin = final ?? market + margin;
  if (variant === 'inline') return <span className="ar-rate" style={style}>USD/DOP <b>{f(market)}</b><span className="mono">{margin >= 0 ? '+' : '−'}{f(Math.abs(margin))}</span>→<b className="is-final">{f(fin)}</b>{frozenIn && <Icon name="lock" size={12} />}</span>;
  return <div className="ar-card" style={{ padding: 20, ...style }}>
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, marginBottom: 17 }}>
      <span className="ar-overline">Tipo de cambio USD/DOP</span>
      {frozenIn ? <span className="ar-badge ar-badge--square" style={{ gap: 4 }}><Icon name="lock" size={11} />Congelado · {frozenIn}</span> : <span className="ar-badge ar-badge--square" style={{ background: 'var(--success-soft)', color: 'var(--success)' }}>Hoy</span>}
    </div>
    <div style={{ font: 'var(--type-kpi)', fontSize: 34, color: 'var(--text-strong)', marginBottom: 17 }}>{f(fin)}<span style={{ font: 'var(--type-caption)', color: 'var(--text-subtle)', marginLeft: 8 }}>DOP por USD</span></div>
    <KeyValueList columns={3} items={[{ label: 'Mercado', value: f(market), mono: true }, { label: 'Margen', value: (margin >= 0 ? '+' : '−') + f(Math.abs(margin)), mono: true }, { label: 'Fecha', value: at, mono: true }, source && { label: 'Fuente', value: source, span: true }]} />
  </div>;
}