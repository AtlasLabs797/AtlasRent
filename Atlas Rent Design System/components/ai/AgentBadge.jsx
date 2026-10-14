import React from 'react';
import { Icon } from '../core/Icon.jsx';
const A = { cobros: ['wallet', 'Agente de cobros'], contratos: ['scroll-text', 'Agente de contratos'], facturacion: ['file-text', 'Agente de facturación'], operaciones: ['calendar-check', 'Agente de operaciones'], supervisor: ['eye', 'Agente supervisor'], migracion: ['database', 'Agente de migración'] };
export function AgentBadge({ agent = 'cobros', label, style }) {
  const [ic, l] = A[agent] || ['bot', agent];
  return <span className="ar-badge" style={{ background: 'var(--ai-soft)', color: 'var(--ai-text)', borderColor: 'var(--ai-border)', ...style }}><Icon name={ic} size={12} />{label || l}</span>;
}