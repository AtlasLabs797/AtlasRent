import React from 'react';
import { Icon } from '../core/Icon.jsx';
const TONE = { success: ['var(--success-soft)', 'var(--success)'], warning: ['var(--warning-soft)', 'var(--warning)'], danger: ['var(--danger-soft)', 'var(--danger)'], info: ['var(--info-soft)', 'var(--info)'], accent: ['var(--accent-soft)', 'var(--accent-text)'], ai: ['var(--ai-soft)', 'var(--ai-text)'] };
export function Timeline({ items = [], style }) {
  return <ol className="ar-timeline" style={style}>{items.map((it, i) => { const t = TONE[it.tone]; return <li key={i} className="ar-tl">
    <span className="ar-tl__dot" style={t ? { background: t[0], color: t[1] } : undefined}><Icon name={it.icon || 'circle'} size={12} /></span>
    <div style={{ minWidth: 0 }}><div className="ar-tl__title">{it.title}</div>{it.body && <div className="ar-tl__body">{it.body}</div>}</div>
    {it.time && <span className="ar-tl__time">{it.time}</span>}
  </li>; })}</ol>;
}