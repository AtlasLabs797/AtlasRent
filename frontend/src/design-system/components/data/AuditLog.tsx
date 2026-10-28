import type { CSSProperties, JSX, ReactNode } from 'react';
import { Avatar } from './Avatar';
import { ProvenanceTag, type ProvenanceTagProps } from './ProvenanceTag';

/** Audit trail rows: actor (user/agent/system), acción, registro, valor anterior → nuevo, motivo, aprobación, origen. */
export interface AuditEntry {
  actor: string;
  actorType?: 'user' | 'agent' | 'system';
  action: string;
  record?: string;
  field?: string;
  before?: ReactNode;
  after?: ReactNode;
  reason?: string;
  approval?: string;
  at: string;
  origin?: ProvenanceTagProps['origin'];
}
export interface AuditLogProps {
  entries: AuditEntry[];
  style?: CSSProperties;
}

export function AuditLog({ entries = [], style }: AuditLogProps): JSX.Element {
  return (
    <div style={style}>
      {entries.map((e, i) => (
        <div key={i} className="ar-audit">
          <Avatar name={e.actor} kind={e.actorType || 'user'} size={32} />
          <div style={{ minWidth: 0 }}>
            <div className="ar-audit__line">
              <b>{e.actor}</b> {e.action}{' '}
              {e.record && (
                <span className="mono" style={{ color: 'var(--text-strong)' }}>
                  {e.record}
                </span>
              )}
              {e.field && <span style={{ color: 'var(--text-muted)' }}> · {e.field}</span>}
            </div>
            {(e.before != null || e.after != null) && (
              <div className="ar-diff">
                {e.before != null && <del>{e.before}</del>}
                <span style={{ color: 'var(--text-subtle)' }}>→</span>
                {e.after != null && <ins>{e.after}</ins>}
              </div>
            )}
            {e.reason && (
              <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)', marginTop: 6 }}>
                Motivo: {e.reason}
                {e.approval && <span> · Aprobado por {e.approval}</span>}
              </div>
            )}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
            <span className="mono" style={{ font: 'var(--type-fine)', fontFamily: 'var(--font-mono)', color: 'var(--text-subtle)', whiteSpace: 'nowrap' }}>
              {e.at}
            </span>
            {e.origin && <ProvenanceTag origin={e.origin} />}
          </div>
        </div>
      ))}
    </div>
  );
}
