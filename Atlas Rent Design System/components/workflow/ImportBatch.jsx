import React from 'react';
import { Badge } from '../core/Badge.jsx';
import { Button } from '../core/Button.jsx';
import { AgentBadge } from '../ai/AgentBadge.jsx';
const S = { simulado: ['info', 'Simulado'], aprobacion: ['warning', 'Pendiente de aprobación'], importado: ['success', 'Importado'], errores: ['danger', 'Con errores'] };
export function ImportBatch({ id, file, entity, date, by = 'migracion', counts = {}, status = 'simulado', onReport, onApprove, style }) {
  const { total = 0, ok = 0, warnings = 0, errors = 0 } = counts; const [t, l] = S[status] || S.simulado;
  const seg = (n, c) => n > 0 && <div style={{ flex: n, background: c }} />;
  return <div className="ar-card" style={style}>
    <div className="ar-card__head"><div style={{ minWidth: 0 }}><div style={{ display: 'flex', alignItems: 'center', gap: 8 }}><h3 className="ar-card__title mono" style={{ fontFamily: 'var(--font-mono)' }}>{id}</h3><Badge tone={t} dot>{l}</Badge></div><div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)', marginTop: 2 }}>{entity} · {file} · <span className="mono">{date}</span></div></div><AgentBadge agent={by} /></div>
    <div className="ar-card__body" style={{ display: 'flex', flexDirection: 'column', gap: 17 }}>
      <div className="ar-batch__bar">{seg(ok, 'var(--success)')}{seg(warnings, 'var(--amber-500)')}{seg(errors, 'var(--danger)')}</div>
      <div className="ar-batch__counts">{[['Registros', total], ['Correctos', ok, 'success'], ['Avisos', warnings, 'warning'], ['Errores', errors, 'danger']].map(([k, v, c]) => <div key={k}><div className="ar-overline">{k}</div><div className="mono" style={{ font: 'var(--type-mono-lg)', fontWeight: 600, marginTop: 4, color: c && v ? 'var(--' + c + ')' : 'var(--text-strong)' }}>{v}</div></div>)}</div>
    </div>
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '14px 20px', borderTop: '1px solid var(--border-subtle)' }}><Button variant="ghost" size="sm" icon="file-text" onClick={onReport}>Ver informe</Button>{status === 'aprobacion' && <Button size="sm" icon="check" onClick={onApprove} disabled={errors > 0}>Aprobar importación</Button>}</div>
  </div>;
}