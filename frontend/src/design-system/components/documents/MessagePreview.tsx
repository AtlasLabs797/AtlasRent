import type { CSSProperties, JSX } from 'react';
import { Badge } from '../core/Badge';
import { Icon } from '../core/Icon';

/** Preview + delivery state of an outgoing WhatsApp/email message: recordatorio consolidado, envío de factura/recibo/cotización. */
export interface MessagePreviewProps {
  to: string;
  phone?: string;
  channel?: 'WhatsApp' | 'Email';
  template?: string;
  text: string;
  attachments?: { name: string; size?: string }[];
  status?: 'programado' | 'enviado' | 'entregado' | 'leido' | 'error';
  time?: string;
  style?: CSSProperties;
}

const ESTADOS = {
  programado: ['neutral', 'clock', 'Programado'],
  enviado: ['neutral', 'check', 'Enviado'],
  entregado: ['info', 'check-check', 'Entregado'],
  leido: ['success', 'check-check', 'Leído'],
  error: ['danger', 'circle-alert', 'Error de envío'],
} as const;

export function MessagePreview({ to, phone, channel = 'WhatsApp', template, text, attachments = [], status = 'programado', time, style }: MessagePreviewProps): JSX.Element {
  const [t, ic, l] = ESTADOS[status] ?? ESTADOS.programado;
  return (
    <div className="ar-msgprev" style={style}>
      <div className="ar-msgprev__head">
        <Icon name={channel === 'Email' ? 'mail' : 'message-circle'} size={16} style={{ color: 'var(--text-muted)' }} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ font: 'var(--type-label)', color: 'var(--text-strong)' }}>{to}</div>
          <div className="mono" style={{ font: 'var(--type-fine)', fontFamily: 'var(--font-mono)', color: 'var(--text-subtle)' }}>
            {channel}
            {phone && ' · ' + phone}
          </div>
        </div>
        {template && <Badge square>{template}</Badge>}
      </div>
      <div className="ar-msgprev__body">
        {/* El texto es dato no confiable (AGENTS §6): React lo escapa; nunca innerHTML. */}
        <div className="ar-msgprev__bubble">{text}</div>
        {attachments.map((a) => (
          <div key={a.name} className="ar-attach">
            <Icon name="file-text" size={16} style={{ color: 'var(--danger)' }} />
            <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{a.name}</span>
            {a.size && (
              <span className="mono" style={{ color: 'var(--text-subtle)' }}>
                {a.size}
              </span>
            )}
          </div>
        ))}
      </div>
      <div className="ar-msgprev__foot">
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, color: 'var(--' + (t === 'neutral' ? 'text-subtle' : t) + ')' }}>
          <Icon name={ic} size={13} />
          {l}
        </span>
        {time && <span className="mono">{time}</span>}
      </div>
    </div>
  );
}
