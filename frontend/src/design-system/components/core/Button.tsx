import type { ButtonHTMLAttributes, JSX, ReactNode } from 'react';
import { Icon } from './Icon';

/**
 * Primary action control.
 */
export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'ai';
  size?: 'sm' | 'md' | 'lg';
  /** Lucide icon name shown before the label */
  icon?: string;
  iconRight?: string;
  children?: ReactNode;
}

export function Button({
  variant = 'primary',
  size = 'md',
  icon,
  iconRight,
  children,
  className = '',
  type = 'button',
  ...rest
}: ButtonProps): JSX.Element {
  const is = size === 'sm' ? 14 : 16;
  return (
    <button type={type} className={`ar-btn ar-btn--${variant} ar-btn--${size} ${className}`} {...rest}>
      {icon && <Icon name={icon} size={is} />}
      {children}
      {iconRight && <Icon name={iconRight} size={is} />}
    </button>
  );
}