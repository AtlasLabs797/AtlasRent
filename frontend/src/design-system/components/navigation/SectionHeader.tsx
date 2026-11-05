import type { CSSProperties, JSX, ReactNode } from 'react';

/** Heading for a block inside a page (below PageHeader): overline and/or 21px title + right-aligned controls. */
export interface SectionHeaderProps {
  eyebrow?: ReactNode;
  title?: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  style?: CSSProperties;
}

export function SectionHeader({ eyebrow, title, description, children, style }: SectionHeaderProps): JSX.Element {
  return (
    <div className="ar-sechead" style={style}>
      <div style={{ minWidth: 0 }}>
        {eyebrow && <div className="ar-overline">{eyebrow}</div>}
        {title && <h2 className="ar-sechead__title">{title}</h2>}
        {description && <p className="ar-sechead__desc">{description}</p>}
      </div>
      {children && <div className="ar-sechead__actions">{children}</div>}
    </div>
  );
}
