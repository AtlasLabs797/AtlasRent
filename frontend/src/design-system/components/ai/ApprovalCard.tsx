import type { CSSProperties, JSX, ReactNode } from 'react';
import { Badge } from '../core/Badge';
import { Button } from '../core/Button';
import { Icon } from '../core/Icon';

/** Human approval request raised by an AI agent (bandeja de aprobaciones IA). */
export interface ApprovalCardProps {
  agent: string;
  action: string;
  reason: ReactNode;
  record: string;
  current?: ReactNode;
  proposed?: ReactNode;
  impact?: ReactNode;
  risk?: 'bajo' | 'medio' | 'alto';
  requestedAt?: string;
  origin?: string;
  onApprove?: () => void;
  onReject?: () => void;
  onEdit?: () => void;
  style?: CSSProperties;
}

function Par({ k, v }: { k: string; v: ReactNode }): JSX.Element {
  return (
    <div>
      <div className="ar-approval__k">{k}</div>
      <div className="ar-approval__v">{v}</div>
    </div>
  );
}

export function ApprovalCard({ agent, action, reason, record, current, proposed, impact, risk = 'medio', requestedAt, origin, onApprove, onReject, onEdit, style }: ApprovalCardProps): JSX.Element {
  const tono = risk === 'alto' ? 'danger' : risk === 'bajo' ? 'success' : 'warning';
  return (
    <div className="ar-approval" style={style}>
      <div className="ar-approval__head">
        <span style={{ width: 28, height: 28, borderRadius: '50%', background: 'var(--ai-solid)', color: '#fff', display: 'grid', placeItems: 'center' }}>
          <Icon name="bot" size={16} />
        </span>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ font: 'var(--type-subhead)', letterSpacing: 'var(--tracking-body)', color: 'var(--text-strong)' }}>{action}</div>
          <div style={{ font: 'var(--type-caption)', color: 'var(--ai-text)' }}>
            {agent}
            {requestedAt && <span style={{ color: 'var(--text-muted)' }}> · {requestedAt}</span>}
          </div>
        </div>
        <Badge tone={tono}>Riesgo {risk}</Badge>
      </div>
      <div className="ar-approval__grid">
        <Par k="Registro" v={<span style={{ fontFamily: 'var(--font-mono)' }}>{record}</span>} />
        <Par k="Origen" v={origin} />
        <Par k="Valor actual" v={current} />
        <Par k="Valor propuesto" v={<strong style={{ fontWeight: 600 }}>{proposed}</strong>} />
        <div style={{ gridColumn: '1 / -1' }}>
          <Par k="Motivo" v={reason} />
        </div>
        {impact && (
          <div style={{ gridColumn: '1 / -1' }}>
            <Par k="Impacto" v={impact} />
          </div>
        )}
      </div>
      <div className="ar-approval__foot">
        <Button variant="ghost" size="sm" onClick={onReject}>
          Rechazar
        </Button>
        <Button variant="secondary" size="sm" icon="pencil" onClick={onEdit}>
          Modificar y aprobar
        </Button>
        <Button size="sm" icon="check" onClick={onApprove}>
          Aprobar
        </Button>
      </div>
    </div>
  );
}
