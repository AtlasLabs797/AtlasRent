import React from 'react';
import { Icon } from '../core/Icon.jsx';
export function StatCard({ label, value, meta, icon, tone, featured, onClick, style }) {
  return <div className={'ar-card ar-stat' + (featured ? ' ar-stat--featured' : '') + (onClick ? ' is-clickable' : '')} data-tone={!featured && tone && tone !== 'default' ? tone : undefined} onClick={onClick} style={style}>
    <span className="ar-stat__label">{icon && <Icon name={icon} size={14} />}{label}</span>
    <span className="ar-stat__value">{value}</span>
    {meta && <span className="ar-stat__meta">{meta}</span>}
  </div>;
}
