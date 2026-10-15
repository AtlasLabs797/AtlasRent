import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

/**
 * KPI tile for dashboards.
 */
export interface StatCardProps {
  label: string;
  value: ReactNode;
  meta?: ReactNode;
  icon?: string;
  tone?: 'default' | 'success' | 'warning' | 'danger';
  /** Dark tile (--tile-1) with 44px mono KPI and accent-on-dark label. Max one per row, usually span 2. */
  featured?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}

export function StatCard({ label, value, meta, icon, tone, featured, onClick, style }: StatCardProps): JSX.Element {
  return (
    <div
      className={'ar-card ar-stat' + (featured ? ' ar-stat--featured' : '') + (onClick ? ' is-clickable' : '')}
      data-tone={!featured && tone && tone !== 'default' ? tone : undefined}
      onClick={onClick}
      style={style}
    >
      <span className="ar-stat__label">
        {icon && <Icon name={icon} size={14} />}
        {label}
      </span>
      <span className="ar-stat__value">{value}</span>
      {meta && <span className="ar-stat__meta">{meta}</span>}
    </div>
  );
}