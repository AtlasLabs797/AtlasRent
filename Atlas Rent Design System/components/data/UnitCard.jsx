import React from 'react';
import { Badge } from '../core/Badge.jsx';
import { Money } from './Money.jsx';
import { Icon } from '../core/Icon.jsx';
export function UnitCard({ name, property, type, status = 'disponible', rent, currency = 'USD', period = 'mes', modalities = [], client, until, alert, selected, onClick, style }) {
  const Tag = onClick ? 'button' : 'div';
  return <Tag type={onClick ? 'button' : undefined} className="ar-card ar-unit" data-selected={selected ? 'true' : undefined} onClick={onClick} style={style}>
    <div className="ar-unit__top"><div style={{ minWidth: 0 }}><div className="ar-unit__name">{name}</div><div className="ar-unit__sub">{[property, type].filter(Boolean).join(' · ')}</div></div><Badge status={status} /></div>
    {modalities.length > 0 && <div className="ar-unit__mods">{modalities.map(m => <span key={m} className="ar-mod">{m}</span>)}</div>}
    {alert && <div style={{ display: 'flex', alignItems: 'center', gap: 6, font: 'var(--type-caption)', color: 'var(--warning)' }}><Icon name="triangle-alert" size={13} />{alert}</div>}
    <div className="ar-unit__foot">
      <div style={{ minWidth: 0 }}><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>{client ? 'Cliente' : 'Sin contrato'}</div><div style={{ font: 'var(--type-body-sm)', color: 'var(--text-strong)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{client || '—'}{until && <span style={{ color: 'var(--text-muted)' }}> · hasta {until}</span>}</div></div>
      {rent != null && <div style={{ textAlign: 'right', flex: 'none' }}><Money amount={rent} currency={currency} strong /><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>/ {period}</div></div>}
    </div>
  </Tag>;
}