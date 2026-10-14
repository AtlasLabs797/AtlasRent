import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Badge } from '../core/Badge.jsx';
import { Button } from '../core/Button.jsx';
export function ApprovalCard({ agent, action, reason, record, current, proposed, impact, risk = 'medio', requestedAt, origin, onApprove, onReject, onEdit, style }) {
  const rt = risk === 'alto' ? 'danger' : risk === 'bajo' ? 'success' : 'warning';
  const KV = ({ k, v }) => <div><div className="ar-approval__k">{k}</div><div className="ar-approval__v">{v}</div></div>;
  return <div className="ar-approval" style={style}>
    <div className="ar-approval__head">
      <span style={{ width: 28, height: 28, borderRadius: '50%', background: 'var(--ai)', color: '#fff', display: 'grid', placeItems: 'center' }}><Icon name="bot" size={16} /></span>
      <div style={{ flex: 1, minWidth: 0 }}><div style={{ font: 'var(--type-subhead)', letterSpacing: 'var(--tracking-body)', color: 'var(--text-strong)' }}>{action}</div><div style={{ font: 'var(--type-caption)', color: 'var(--ai-text)' }}>{agent}{requestedAt && <span style={{ color: 'var(--text-muted)' }}> · {requestedAt}</span>}</div></div>
      <Badge tone={rt}>Riesgo {risk}</Badge>
    </div>
    <div className="ar-approval__grid">
      <KV k="Registro" v={<span style={{ fontFamily: 'var(--font-mono)' }}>{record}</span>} />
      <KV k="Origen" v={origin} />
      <KV k="Valor actual" v={current} />
      <KV k="Valor propuesto" v={<strong style={{ fontWeight: 600 }}>{proposed}</strong>} />
      <div style={{ gridColumn: '1 / -1' }}><KV k="Motivo" v={reason} /></div>
      {impact && <div style={{ gridColumn: '1 / -1' }}><KV k="Impacto" v={impact} /></div>}
    </div>
    <div className="ar-approval__foot">
      <Button variant="ghost" size="sm" onClick={onReject}>Rechazar</Button>
      <Button variant="secondary" size="sm" icon="pencil" onClick={onEdit}>Modificar y aprobar</Button>
      <Button size="sm" icon="check" onClick={onApprove}>Aprobar</Button>
    </div>
  </div>;
}
