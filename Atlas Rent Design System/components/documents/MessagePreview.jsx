import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Badge } from '../core/Badge.jsx';
const ST = { programado: ['neutral', 'clock', 'Programado'], enviado: ['neutral', 'check', 'Enviado'], entregado: ['info', 'check-check', 'Entregado'], leido: ['success', 'check-check', 'Leído'], error: ['danger', 'circle-alert', 'Error de envío'] };
export function MessagePreview({ to, phone, channel = 'WhatsApp', template, text, attachments = [], status = 'programado', time, style }) {
  const [t, ic, l] = ST[status] || ST.programado;
  return <div className="ar-msgprev" style={style}>
    <div className="ar-msgprev__head"><Icon name={channel === 'Email' ? 'mail' : 'message-circle'} size={16} style={{ color: 'var(--text-muted)' }} /><div style={{ flex: 1, minWidth: 0 }}><div style={{ font: 'var(--type-label)', color: 'var(--text-strong)' }}>{to}</div><div className="mono" style={{ font: 'var(--type-fine)', fontFamily: 'var(--font-mono)', color: 'var(--text-subtle)' }}>{channel}{phone && ' · ' + phone}</div></div>{template && <Badge square>{template}</Badge>}</div>
    <div className="ar-msgprev__body"><div className="ar-msgprev__bubble">{text}</div>{attachments.map(a => <div key={a.name} className="ar-attach"><Icon name="file-text" size={16} style={{ color: 'var(--danger)' }} /><span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{a.name}</span>{a.size && <span className="mono" style={{ color: 'var(--text-subtle)' }}>{a.size}</span>}</div>)}</div>
    <div className="ar-msgprev__foot"><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, color: 'var(--' + (t === 'neutral' ? 'text-subtle' : t) + ')' }}><Icon name={ic} size={13} />{l}</span>{time && <span className="mono">{time}</span>}</div>
  </div>;
}