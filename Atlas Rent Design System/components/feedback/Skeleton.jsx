import React from 'react';
export function Skeleton({ width = '100%', height = 14, radius, circle, lines = 1, style }) {
  const one = (w, k) => <span key={k} aria-hidden="true" className={'ar-skel' + (circle ? ' ar-skel--circle' : '')} style={{ width: circle ? height : w, height, borderRadius: radius, ...(lines === 1 ? style : null) }} />;
  if (lines === 1) return one(width, 0);
  return <span className="ar-skel-stack" role="status" aria-label="Cargando" style={style}>{Array.from({ length: lines }, (_, i) => one(i === lines - 1 ? '60%' : width, i))}</span>;
}
