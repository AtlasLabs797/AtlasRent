import type { CSSProperties, InputHTMLAttributes, JSX, ReactNode } from 'react';

/** Radio with inline label; group via shared `name`. */
export interface RadioProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: ReactNode;
  style?: CSSProperties;
}

export function Radio({ label, style, ...rest }: RadioProps): JSX.Element {
  return (
    <label className="ar-check" style={style}>
      <input type="radio" {...rest} />
      {label}
    </label>
  );
}
