import type { CSSProperties, JSX, ReactNode } from 'react';
import { Breadcrumbs } from './Breadcrumbs';

/** Page rhythm: eyebrow (module) → 40px title → one-line description, actions right. Optional breadcrumbs above. */
export interface PageHeaderProps {
  eyebrow?: string;
  title: ReactNode;
  description?: ReactNode;
  breadcrumbs?: { label: string; onClick?: () => void }[];
  children?: ReactNode;
  style?: CSSProperties;
}

export function PageHeader({ eyebrow, title, description, breadcrumbs, children, style }: PageHeaderProps): JSX.Element {
  return (
    <div style={style}>
      {breadcrumbs && <Breadcrumbs items={breadcrumbs} />}
      <div className="ar-pagehead">
        <div style={{ minWidth: 0 }}>
          {eyebrow && <div className="ar-overline ar-pagehead__eyebrow">{eyebrow}</div>}
          <h1 className="ar-pagehead__title">{title}</h1>
          {description && <p className="ar-pagehead__desc">{description}</p>}
        </div>
        {children && <div className="ar-pagehead__actions">{children}</div>}
      </div>
    </div>
  );
}