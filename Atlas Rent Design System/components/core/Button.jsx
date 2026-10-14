import React from 'react';
import { Icon } from './Icon.jsx';
export function Button({ variant = 'primary', size = 'md', icon, iconRight, children, className = '', type = 'button', ...rest }) {
  const is = size === 'sm' ? 14 : 16;
  return <button type={type} className={`ar-btn ar-btn--${variant} ar-btn--${size} ${className}`} {...rest}>
    {icon && <Icon name={icon} size={is} />}{children}{iconRight && <Icon name={iconRight} size={is} />}
  </button>;
}
