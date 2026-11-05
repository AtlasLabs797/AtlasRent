import type { CSSProperties, JSX } from 'react';
import { AgentBadge, type AgentBadgeProps } from '../ai/AgentBadge';
import { Badge } from '../core/Badge';
import { Button } from '../core/Button';

/** Migration lote: ID, file, entity, agent, counts (correctos/avisos/errores) and status. Final import always needs human approval. */
export interface ImportBatchProps {
  id: string;
  file?: string;
  entity?: string;
  date?: string;
  by?: AgentBadgeProps['agent'];
  counts?: { total?: number; ok?: number; warnings?: number; errors?: number };
  status?: 'simulado' | 'aprobacion' | 'importado' | 'errores';
  onReport?: () => void;
  onApprove?: () => void;
  style?: CSSProperties;
}

const ESTADOS = {
  simulado: ['info', 'Simulado'],
  aprobacion: ['warning', 'Pendiente de aprobación'],
  importado: ['success', 'Importado'],
  errores: ['danger', 'Con errores'],
} as const;

export function ImportBatch({ id, file, entity, date, by = 'migracion', counts = {}, status = 'simulado', onReport, onApprove, style }: ImportBatchProps): JSX.Element {
  const { total = 0, ok = 0, warnings = 0, errors = 0 } = counts;
  const [t, l] = ESTADOS[status] ?? ESTADOS.simulado;
  const tramo = (n: number, c: string) => n > 0 && <div style={{ flex: n, background: c }} />;
  const cifras: Array<[string, number, ('success' | 'warning' | 'danger')?]> = [
    ['Registros', total],
    ['Correctos', ok, 'success'],
    ['Avisos', warnings, 'warning'],
    ['Errores', errors, 'danger'],
  ];
  return (
    <div className="ar-card" style={style}>
      <div className="ar-card__head">
        <div style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <h3 className="ar-card__title mono" style={{ fontFamily: 'var(--font-mono)' }}>
              {id}
            </h3>
            <Badge tone={t} dot>
              {l}
            </Badge>
          </div>
          <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)', marginTop: 2 }}>
            {entity} · {file} · <span className="mono">{date}</span>
          </div>
        </div>
        <AgentBadge agent={by} />
      </div>
      <div className="ar-card__body" style={{ display: 'flex', flexDirection: 'column', gap: 17 }}>
        <div className="ar-batch__bar">
          {tramo(ok, 'var(--success)')}
          {tramo(warnings, 'var(--amber-500)')}
          {tramo(errors, 'var(--danger)')}
        </div>
        <div className="ar-batch__counts">
          {cifras.map(([k, v, c]) => (
            <div key={k}>
              <div className="ar-overline">{k}</div>
              <div className="mono" style={{ font: 'var(--type-mono-lg)', fontWeight: 600, marginTop: 4, color: c && v ? 'var(--' + c + ')' : 'var(--text-strong)' }}>
                {v}
              </div>
            </div>
          ))}
        </div>
      </div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '14px 20px', borderTop: '1px solid var(--border-subtle)' }}>
        <Button variant="ghost" size="sm" icon="file-text" onClick={onReport}>
          Ver informe
        </Button>
        {status === 'aprobacion' && (
          <Button size="sm" icon="check" onClick={onApprove} disabled={errors > 0}>
            Aprobar importación
          </Button>
        )}
      </div>
    </div>
  );
}
