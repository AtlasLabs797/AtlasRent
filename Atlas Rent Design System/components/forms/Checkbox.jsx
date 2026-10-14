import React from 'react';
export function Checkbox({ label, style, ...rest }) {
  return <label className="ar-check" style={style}><input type="checkbox" {...rest} />{label}</label>;
}
