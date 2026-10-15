import type { CSSProperties, JSX, ReactNode } from 'react';

/** Record fields as overline label + value grid. Use on detail screens (cliente, unidad, contrato). */
export interface KeyValueListItem {
  label: string;
  value?: ReactNode;
  mono?: boolean;
  span?: boolean;
}

export interface KeyValueListProps {
  items: KeyValueListItem[];
  columns?: 1 | 2 | 3 | 4;
  style?: CSSProperties;
}

export function KeyValueList({ items = [], columns = 2, style }: KeyValueListProps): JSX.Element {
  return (
    <dl className="ar-kv" style={{ gridTemplateColumns: 'repeat(' + columns + ',minmax(0,1fr))', ...style }}>
      {items
        .filter(Boolean)
        .map((item, indice) => (
          <div key={indice} style={{ gridColumn: item.span ? '1 / -1' : undefined, minWidth: 0 }}>
            <dt>{item.label}</dt>
            <dd className={item.mono ? 'is-mono' : ''}>{item.value ?? '—'}</dd>
          </div>
        ))}
    </dl>
  );
}