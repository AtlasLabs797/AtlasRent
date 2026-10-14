import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function Input({ label, hint, error, required, icon, prefix, suffix, disabled, id, style, ...rest }) {
  const fid = id || (label ? 'f-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  return <div className="ar-field" style={style}>
    {label && <label className="ar-label" htmlFor={fid}>{label}{required && <em> *</em>}</label>}
    <div className={'ar-control' + (error ? ' ar-control--error' : '') + (disabled ? ' ar-control--disabled' : '') + (icon === 'search' && !label ? ' ar-control--search' : '')}>
      {icon && <Icon name={icon} size={15} />}{prefix && <span className="ar-control__affix">{prefix}</span>}
      <input id={fid} disabled={disabled} {...rest} />
      {suffix && <span className="ar-control__affix">{suffix}</span>}
    </div>
    {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
  </div>;
}
