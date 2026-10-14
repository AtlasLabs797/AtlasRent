/**
 * Primary action control.
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'ai';
  size?: 'sm' | 'md' | 'lg';
  /** Lucide icon name shown before the label */ icon?: string;
  iconRight?: string;
  children?: React.ReactNode;
}
export declare function Button(props: ButtonProps): JSX.Element;
