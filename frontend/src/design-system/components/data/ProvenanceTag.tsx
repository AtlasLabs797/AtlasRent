import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';

/** Origin of a record: Importado de Factusol · Manual · Automático · Agente IA · Excel · API. */
export interface ProvenanceTagProps {
  origin?: 'factusol' | 'manual' | 'auto' | 'agente' | 'excel' | 'api';
  label?: string;
  style?: CSSProperties;
}

const ORIGENES = {
  factusol: ['archive', 'Factusol'],
  manual: ['pencil', 'Manual'],
  auto: ['refresh-cw', 'Automático'],
  agente: ['bot', 'Agente IA'],
  excel: ['file-spreadsheet', 'Excel'],
  api: ['plug', 'API'],
} as const;

export function ProvenanceTag({ origin = 'manual', label, style }: ProvenanceTagProps): JSX.Element {
  const [ic, l] = ORIGENES[origin] ?? ORIGENES.manual;
  return (
    <span
      className="ar-badge ar-badge--square"
      style={{ height: 22, padding: '0 8px', gap: 4, font: 'var(--type-fine)', ...(origin === 'agente' ? { background: 'var(--ai-soft)', color: 'var(--ai-text)' } : null), ...style }}
    >
      <Icon name={ic} size={11} />
      {label || l}
    </span>
  );
}
