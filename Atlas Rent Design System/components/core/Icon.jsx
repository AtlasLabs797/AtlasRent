import React from 'react';
const CDN='https://unpkg.com/lucide-static@0.470.0/icons/';
export function Icon({ name, size = 16, color, style, className = '', ...rest }) {
  return <span aria-hidden="true" className={'ar-icon ' + className} style={{ width: size, height: size, color, '--icon': `url(${CDN}${name}.svg)`, ...style }} {...rest} />;
}
