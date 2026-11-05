import type { CSSProperties, JSX, ReactNode } from 'react';

/** White surface with 1px hairline border and 18px radius, no shadow. */
export interface CardProps {
  title?: ReactNode;
  actions?: ReactNode;
  padded?: boolean;
  children?: ReactNode;
  style?: CSSProperties;
  className?: string;
}

export function Card({ title, actions, children, padded = true, style, className = '' }: CardProps): JSX.Element {
  return (
    <section className={'ar-card ' + className} style={style}>
      {(title || actions) && (
        <header className="ar-card__head">
          <h3 className="ar-card__title">{title}</h3>
          {actions && <div style={{ display: 'flex', gap: 8 }}>{actions}</div>}
        </header>
      )}
      {padded ? <div className="ar-card__body">{children}</div> : children}
    </section>
  );
}