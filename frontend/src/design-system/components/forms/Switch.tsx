import { useState, type CSSProperties, type JSX, type ReactNode } from 'react';

/** On/off toggle for settings that apply immediately. */
export interface SwitchProps {
  checked?: boolean;
  defaultChecked?: boolean;
  onChange?: (v: boolean) => void;
  label?: ReactNode;
  tone?: 'accent' | 'ai';
  disabled?: boolean;
  style?: CSSProperties;
}

export function Switch({
  checked,
  defaultChecked = false,
  onChange,
  label,
  tone = 'accent',
  disabled,
  style,
}: SwitchProps): JSX.Element {
  const [interno, setInterno] = useState(defaultChecked);
  const on = checked ?? interno;
  const alternar = () => {
    if (disabled) return;
    setInterno(!on);
    onChange?.(!on);
  };
  return (
    <label className={'ar-switch' + (tone === 'ai' ? ' ar-switch--ai' : '')} style={{ opacity: disabled ? 0.5 : 1, ...style }}>
      <button type="button" role="switch" aria-checked={on} onClick={alternar} disabled={disabled}>
        <span />
      </button>
      {label}
    </label>
  );
}
