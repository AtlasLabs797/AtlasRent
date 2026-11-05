import type { CSSProperties, JSX, ReactNode } from 'react';
import { Icon } from '../core/Icon';

/** Vertical history: contract lifecycle, renovaciones, incrementos de renta, envíos. */
export interface TimelineProps {
  items: { title: ReactNode; body?: ReactNode; time?: string; icon?: string; tone?: 'success' | 'warning' | 'danger' | 'info' | 'accent' | 'ai' }[];
  style?: CSSProperties;
}

const TONOS = {
  success: ['var(--success-soft)', 'var(--success)'],
  warning: ['var(--warning-soft)', 'var(--warning)'],
  danger: ['var(--danger-soft)', 'var(--danger)'],
  info: ['var(--info-soft)', 'var(--info)'],
  accent: ['var(--accent-soft)', 'var(--accent-text)'],
  ai: ['var(--ai-soft)', 'var(--ai-text)'],
} as const;

export function Timeline({ items = [], style }: TimelineProps): JSX.Element {
  return (
    <ol className="ar-timeline" style={style}>
      {items.map((it, i) => {
        const t = it.tone ? TONOS[it.tone] : undefined;
        return (
          <li key={i} className="ar-tl">
            <span className="ar-tl__dot" style={t ? { background: t[0], color: t[1] } : undefined}>
              <Icon name={it.icon || 'circle'} size={12} />
            </span>
            <div style={{ minWidth: 0 }}>
              <div className="ar-tl__title">{it.title}</div>
              {it.body && <div className="ar-tl__body">{it.body}</div>}
            </div>
            {it.time && <span className="ar-tl__time">{it.time}</span>}
          </li>
        );
      })}
    </ol>
  );
}
