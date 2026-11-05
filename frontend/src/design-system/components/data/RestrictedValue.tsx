import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

/** Role gate for aggregated financial totals: Empleado sees a lock chip instead of the figure. */
export interface RestrictedValueProps {
  allowed?: boolean;
  children?: ReactNode;
  label?: string;
  style?: CSSProperties;
}

export function RestrictedValue({ allowed = false, children, label = 'Solo gerente y administrador', style }: RestrictedValueProps): JSX.Element {
  if (allowed) return <>{children}</>;
  return (
    <span className="ar-restricted" title={label} style={style}>
      <Icon name="lock" size={12} />
      {label}
    </span>
  );
}
