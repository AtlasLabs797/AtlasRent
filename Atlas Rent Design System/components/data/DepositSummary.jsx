import React from 'react';
import { Money } from './Money.jsx';
import { Badge } from '../core/Badge.jsx';
import { KeyValueList } from './KeyValueList.jsx';
export function DepositSummary({ required = 0, count = 1, collected = 0, currency = 'USD', contract, status = 'retenida', deductions = [], returned, returnedAt, method, style }) {
  const pend = Math.max(0, required - collected), disc = deductions.reduce((s, d) => s + (Number(d.amount) || 0), 0), toReturn = collected - disc;
  return <div className="ar-card" style={style}>
    <div className="ar-card__head"><div><h3 className="ar-card__title">Fianza</h3>{contract && <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>{contract}</div>}</div><Badge status={status} /></div>
    <div className="ar-card__body" style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <KeyValueList columns={3} items={[{ label: 'Exigido · ' + count + (count === 1 ? ' fianza' : ' fianzas'), value: <Money amount={required} currency={currency} /> }, { label: 'Cobrado', value: <Money amount={collected} currency={currency} /> }, { label: 'Pendiente', value: <Money amount={pend} currency={currency} tone={pend > 0 ? 'danger' : 'muted'} /> }]} />
      {(deductions.length > 0 || returned != null) && <div style={{ paddingTop: 17, borderTop: '1px solid var(--border-subtle)' }}>
        <div className="ar-overline" style={{ marginBottom: 10 }}>Liquidación</div>
        <dl className="ar-totals">
          {deductions.map((d, i) => <div key={i}><dt>{d.reason}</dt><dd><Money amount={-d.amount} currency={currency} tone="danger" /></dd></div>)}
          <div className="is-total"><dt>{returned != null ? 'Devuelto' : 'A devolver'}</dt><dd><Money amount={returned ?? toReturn} currency={currency} strong /></dd></div>
          {returnedAt && <div><dt>Fecha · forma</dt><dd style={{ font: 'var(--type-body-sm)', color: 'var(--text-muted)' }}>{returnedAt}{method ? ' · ' + method : ''}</dd></div>}
        </dl>
      </div>}
    </div>
  </div>;
}