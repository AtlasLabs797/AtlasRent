import type { CSSProperties, JSX } from 'react';
import { Badge } from '../core/Badge';
import { Button } from '../core/Button';
import { Avatar } from '../data/Avatar';

/** Bandeja de revisión row: an operation an Empleado left pending for Gerente/Admin. Shows who, type, record, date, motivo; renotifies at 30 days. */
export interface ReviewItemProps {
  preparedBy: string;
  type: string;
  title: string;
  record?: string;
  date: string;
  days?: number;
  reason?: string;
  onApprove?: () => void;
  onReject?: () => void;
  onOpen?: () => void;
  style?: CSSProperties;
}

export function ReviewItem({ preparedBy, type, title, record, date, days = 0, reason, onApprove, onReject, onOpen, style }: ReviewItemProps): JSX.Element {
  return (
    <div className="ar-review" style={style}>
      <Avatar name={preparedBy} size={40} />
      <div style={{ minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span className="ar-review__title">{title}</span>
          <Badge square>{type}</Badge>
          {days >= 30 && (
            <Badge tone="warning" dot>
              Renotificado · {days} días
            </Badge>
          )}
        </div>
        <div className="ar-review__meta">
          Preparado por {preparedBy} · <span className="mono">{date}</span>
          {record && (
            <>
              {' '}
              · <span className="mono" style={{ color: 'var(--text-strong)' }}>{record}</span>
            </>
          )}
        </div>
        {reason && <div className="ar-review__reason">{reason}</div>}
      </div>
      <div className="ar-review__actions">
        {onOpen && (
          <Button variant="ghost" size="sm" onClick={onOpen}>
            Ver
          </Button>
        )}
        <Button variant="secondary" size="sm" onClick={onReject}>
          Rechazar
        </Button>
        <Button size="sm" icon="check" onClick={onApprove}>
          Aprobar
        </Button>
      </div>
    </div>
  );
}
