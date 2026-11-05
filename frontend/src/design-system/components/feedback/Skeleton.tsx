import type { CSSProperties, JSX } from 'react';

/** Loading placeholder with the system's only looping shimmer. Mirror the final layout; never show a spinner for lists or KPIs. */
export interface SkeletonProps {
  width?: number | string;
  height?: number;
  radius?: number | string;
  circle?: boolean;
  /** Stack of text lines; last one is 60% */
  lines?: number;
  style?: CSSProperties;
}

export function Skeleton({ width = '100%', height = 14, radius, circle, lines = 1, style }: SkeletonProps): JSX.Element {
  const uno = (w: number | string, k: number) => (
    <span
      key={k}
      aria-hidden="true"
      className={'ar-skel' + (circle ? ' ar-skel--circle' : '')}
      style={{ width: circle ? height : w, height, borderRadius: radius, ...(lines === 1 ? style : null) }}
    />
  );
  if (lines === 1) return uno(width, 0);
  return (
    <span className="ar-skel-stack" role="status" aria-label="Cargando" style={style}>
      {Array.from({ length: lines }, (_, i) => uno(i === lines - 1 ? '60%' : width, i))}
    </span>
  );
}
