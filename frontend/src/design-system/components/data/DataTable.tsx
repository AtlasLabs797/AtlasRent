import type { CSSProperties, JSX, ReactNode } from 'react';

/** Dense data table: 36px header, 44px rows, hairline separators. */
export interface DataTableColumn {
  key: string;
  label: ReactNode;
  align?: 'left' | 'right';
  width?: number | string;
  render?: (row: any) => ReactNode;
}

export interface DataTableProps {
  columns: DataTableColumn[];
  rows: any[];
  rowKey?: string;
  onRowClick?: (row: any) => void;
  selectedKey?: any;
  empty?: ReactNode;
  style?: CSSProperties;
}

export function DataTable({
  columns = [],
  rows = [],
  rowKey = 'id',
  onRowClick,
  selectedKey,
  empty = 'Sin resultados',
  style,
}: DataTableProps): JSX.Element {
  return (
    <div style={{ overflowX: 'auto', ...style }}>
      <table className="ar-table">
        <thead>
          <tr>
            {columns.map((columna) => (
              <th
                key={columna.key}
                className={columna.align === 'right' ? 'is-num' : ''}
                style={{ width: columna.width }}
              >
                {columna.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} style={{ textAlign: 'center', color: 'var(--text-muted)', height: 88 }}>
                {empty}
              </td>
            </tr>
          ) : (
            rows.map((fila, indice) => (
              <tr
                key={fila[rowKey] ?? indice}
                className={onRowClick ? 'is-clickable' : ''}
                data-selected={selectedKey != null && fila[rowKey] === selectedKey}
                onClick={onRowClick ? () => onRowClick(fila) : undefined}
              >
                {columns.map((columna) => (
                  <td key={columna.key} className={columna.align === 'right' ? 'is-num' : ''}>
                    {columna.render ? columna.render(fila) : fila[columna.key]}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}