import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function Avatar({ name = '', kind = 'user', size = 32, style }) {
  const ini = name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]).join('').toUpperCase();
  return <span className={'ar-avatar' + (kind !== 'user' ? ' ar-avatar--' + kind : '')} title={name} style={{ width: size, height: size, fontSize: Math.round(size * 0.38), ...style }}>
    {kind === 'agent' ? <Icon name="bot" size={Math.round(size * 0.5)} /> : kind === 'system' ? <Icon name="cog" size={Math.round(size * 0.5)} /> : ini}
  </span>;
}