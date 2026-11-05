import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

const TONOS = {
  info: ['info', 'info'],
  success: ['success', 'circle-check'],
  warning: ['warning', 'triangle-alert'],
  danger: ['danger', 'octagon-alert'],
  ai: ['ai', 'sparkles'],
} as const;

/**
 * Inline persistent alert (e.g. reserva >7 días, fianza pendiente).
 */
export interface AlertProps {
  tone?: 'info' | 'success' | 'warning' | 'danger' | 'ai';
  title?: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  style?: CSSProperties;
}

export function Alert({ tone = 'info', title, children, action, style }: AlertProps): JSX.Element {
  const [clave, icono] = TONOS[tone] ?? TONOS.info;
  return (
    <div className="ar-alert" style={{ background: `var(--${clave}-soft)`, borderColor: `var(--${clave}-border)`, ...style }}>
      <Icon name={icono} size={16} style={{ color: `var(--${clave})`, marginTop: 1 }} />
      <div style={{ flex: 1, minWidth: 0 }}>
        {title && <div className="ar-alert__title">{title}</div>}
        {children && <div className="ar-alert__body">{children}</div>}
      </div>
      {action}
    </div>
  );
}