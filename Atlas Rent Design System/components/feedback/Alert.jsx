import React from 'react';
import { Icon } from '../core/Icon.jsx';
const T = { info: ['info', 'info'], success: ['success', 'circle-check'], warning: ['warning', 'triangle-alert'], danger: ['danger', 'octagon-alert'], ai: ['ai', 'sparkles'] };
export function Alert({ tone = 'info', title, children, action, style }) {
  const [k, ic] = T[tone] || T.info;
  return <div className="ar-alert" style={{ background: `var(--${k}-soft)`, borderColor: `var(--${k}-border)`, ...style }}>
    <Icon name={ic} size={16} style={{ color: `var(--${k})`, marginTop: 1 }} />
    <div style={{ flex: 1, minWidth: 0 }}>{title && <div className="ar-alert__title">{title}</div>}{children && <div className="ar-alert__body">{children}</div>}</div>
    {action}
  </div>;
}
