import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';

/** Identity chip for one of the six agents. Always violet: the AI channel is the same in every Atlas app. */
export interface AgentBadgeProps {
  agent?: 'cobros' | 'contratos' | 'facturacion' | 'operaciones' | 'supervisor' | 'migracion';
  label?: string;
  style?: CSSProperties;
}

const AGENTES: Record<string, [string, string]> = {
  cobros: ['wallet', 'Agente de cobros'],
  contratos: ['scroll-text', 'Agente de contratos'],
  facturacion: ['file-text', 'Agente de facturación'],
  operaciones: ['calendar-check', 'Agente de operaciones'],
  supervisor: ['eye', 'Agente supervisor'],
  migracion: ['database', 'Agente de migración'],
};

export function AgentBadge({ agent = 'cobros', label, style }: AgentBadgeProps): JSX.Element {
  const [ic, l] = AGENTES[agent] ?? ['bot', agent];
  return (
    <span className="ar-badge" style={{ background: 'var(--ai-soft)', color: 'var(--ai-text)', borderColor: 'var(--ai-border)', ...style }}>
      <Icon name={ic} size={12} />
      {label || l}
    </span>
  );
}
