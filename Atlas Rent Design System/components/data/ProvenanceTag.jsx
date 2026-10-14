import React from 'react';
import { Icon } from '../core/Icon.jsx';
const O = { factusol: ['archive', 'Factusol'], manual: ['pencil', 'Manual'], auto: ['refresh-cw', 'Automático'], agente: ['bot', 'Agente IA'], excel: ['file-spreadsheet', 'Excel'], api: ['plug', 'API'] };
export function ProvenanceTag({ origin = 'manual', label, style }) {
  const [ic, l] = O[origin] || O.manual;
  return <span className="ar-badge ar-badge--square" style={{ height: 22, padding: '0 8px', gap: 4, font: 'var(--type-fine)', ...(origin === 'agente' ? { background: 'var(--ai-soft)', color: 'var(--ai-text)' } : null), ...style }}><Icon name={ic} size={11} />{label || l}</span>;
}