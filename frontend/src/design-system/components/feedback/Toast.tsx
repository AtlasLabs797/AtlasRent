import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

/** Transient dark confirmation, bottom-center, auto-dismiss ~4s. */
export interface ToastProps {
  tone?: 'success' | 'info' | 'danger' | 'ai';
  children?: ReactNode;
  action?: ReactNode;
  onClose?: () => void;
  style?: CSSProperties;
}

export function Toast({ tone = 'success', children, action, onClose, style }: ToastProps): JSX.Element {
  const ic = tone === 'danger' ? 'octagon-alert' : tone === 'ai' ? 'sparkles' : tone === 'info' ? 'info' : 'circle-check';
  const c = tone === 'danger' ? 'var(--crimson-300)' : tone === 'ai' ? 'var(--violet-300)' : tone === 'info' ? 'var(--cobalt-300)' : 'var(--green-300)';
  return (
    <div className="ar-toast" role="status" style={style}>
      <Icon name={ic} size={16} style={{ color: c }} />
      <span style={{ flex: 1 }}>{children}</span>
      {action}
      {onClose && (
        <button onClick={onClose} aria-label="Cerrar" style={{ background: 'none', border: 0, color: 'var(--gray-400)', cursor: 'pointer', display: 'flex' }}>
          <Icon name="x" size={14} />
        </button>
      )}
    </div>
  );
}
