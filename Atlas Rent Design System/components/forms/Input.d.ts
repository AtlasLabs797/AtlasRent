/**
 * Labelled text input with optional icon, prefix/suffix (currency) and error.
 */
export interface InputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'prefix'> {
  label?: string; hint?: string; error?: string; required?: boolean;
  icon?: string; prefix?: string; suffix?: string; style?: React.CSSProperties;
}
export declare function Input(props: InputProps): JSX.Element;
