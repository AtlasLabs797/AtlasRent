import React from 'react';
export function SectionHeader({ eyebrow, title, description, children, style }) {
  return <div className="ar-sechead" style={style}>
    <div style={{ minWidth: 0 }}>{eyebrow && <div className="ar-overline">{eyebrow}</div>}{title && <h2 className="ar-sechead__title">{title}</h2>}{description && <p className="ar-sechead__desc">{description}</p>}</div>
    {children && <div className="ar-sechead__actions">{children}</div>}
  </div>;
}
