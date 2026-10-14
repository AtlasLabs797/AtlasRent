import React from 'react';
export function Radio({ label, style, ...rest }) {
  return <label className="ar-check" style={style}><input type="radio" {...rest} />{label}</label>;
}
