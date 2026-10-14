import React from 'react';
import { Icon } from './Icon.jsx';
export function IconButton({ icon, label, variant = 'ghost', size = 'md', className = '', ...rest }) {
  return <button type="button" aria-label={label} title={label} className={`ar-btn ar-btn--icon ar-btn--${variant} ar-btn--${size} ${className}`} {...rest}><Icon name={icon} size={size === 'sm' ? 14 : 16} /></button>;
}
