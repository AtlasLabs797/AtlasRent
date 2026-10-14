import React from 'react';
export function Switch({ checked, defaultChecked = false, onChange, label, tone = 'accent', disabled, style }) {
  const [inner, setInner] = React.useState(defaultChecked);
  const on = checked ?? inner;
  const toggle = () => { if (disabled) return; setInner(!on); onChange && onChange(!on); };
  return <label className={'ar-switch' + (tone === 'ai' ? ' ar-switch--ai' : '')} style={{ opacity: disabled ? .5 : 1, ...style }}>
    <button type="button" role="switch" aria-checked={on} onClick={toggle} disabled={disabled}><span /></button>{label}
  </label>;
}
