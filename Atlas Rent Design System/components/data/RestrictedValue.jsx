import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function RestrictedValue({ allowed = false, children, label = 'Solo gerente y administrador', style }) {
  if (allowed) return <>{children}</>;
  return <span className="ar-restricted" title={label} style={style}><Icon name="lock" size={12} />{label}</span>;
}