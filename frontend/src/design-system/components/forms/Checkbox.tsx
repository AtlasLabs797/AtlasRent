import type { CSSProperties, InputHTMLAttributes, JSX, ReactNode } from 'react';

/** Checkbox with inline label. */
export interface CheckboxProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: ReactNode;
  style?: CSSProperties;
}

export function Checkbox({ label, style, ...rest }: CheckboxProps): JSX.Element {
  return (
    <label className="ar-check" style={style}>
      <input type="checkbox" {...rest} />
      {label}
    </label>
  );
}
