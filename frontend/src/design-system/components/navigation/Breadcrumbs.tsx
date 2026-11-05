import { Fragment, type CSSProperties, type JSX } from 'react';
import { Icon } from '../core/Icon';

/** Hierarchy trail for detail screens: Inmuebles › Torre Coral › 4B › CT-0217. */
export interface BreadcrumbsProps {
  items: { label: string; onClick?: () => void }[];
  style?: CSSProperties;
}

export function Breadcrumbs({ items = [], style }: BreadcrumbsProps): JSX.Element {
  return (
    <nav aria-label="Ruta" className="ar-crumbs" style={style}>
      {items.map((item, indice) => {
        const ultimo = indice === items.length - 1;
        return (
          <Fragment key={indice}>
            {ultimo ? (
              <span aria-current="page">{item.label}</span>
            ) : (
              <button type="button" onClick={item.onClick}>
                {item.label}
              </button>
            )}
            {!ultimo && <Icon name="chevron-right" size={12} style={{ color: 'var(--text-subtle)' }} />}
          </Fragment>
        );
      })}
    </nav>
  );
}