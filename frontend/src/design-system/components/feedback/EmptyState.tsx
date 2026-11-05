import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

/** Honest empty state: what the surface is for + next useful action. Two lines max. */
export interface EmptyStateProps {
  icon?: string;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
  style?: CSSProperties;
}

export function EmptyState({ icon = 'inbox', title, children, action, style }: EmptyStateProps): JSX.Element {
  return (
    <div className="ar-empty" style={style}>
      <span className="ar-empty__mark">
        <Icon name={icon} size={20} />
      </span>
      <h3 className="ar-empty__title">{title}</h3>
      {children && <p className="ar-empty__body">{children}</p>}
      {action && <div style={{ marginTop: 10 }}>{action}</div>}
    </div>
  );
}