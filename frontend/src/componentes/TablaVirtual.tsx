import { useRef, type JSX } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import type { DataTableColumn } from '../design-system/components/data/DataTable';

export interface TablaVirtualProps<Fila> {
  columns: DataTableColumn[];
  rows: Fila[];
  /** Campo único de la fila; por defecto `id`. */
  rowKey?: string;
  onRowClick?: (fila: Fila) => void;
  /** Alto visible de la tabla en px. El cuerpo hace scroll dentro; la cabecera queda fija. */
  alto?: number;
  /** Alto de fila: el de `DataTable` (44px). */
  altoFila?: number;
  empty?: string;
}

/**
 * `DataTable` con virtualización (TanStack Virtual) para listas largas: ~150 unidades caben en un
 * `DataTable` normal, pero facturas, cobros y auditoría llegan a miles de filas. Mismas columnas y
 * mismas clases `ar-table` que el DS. Se virtualiza con filas separadoras (arriba y abajo) para
 * conservar una `<table>` real y su semántica de accesibilidad; para listas de menos de ~200 filas
 * usa `DataTable` directamente.
 */
export function TablaVirtual<Fila extends Record<string, unknown>>({
  columns,
  rows,
  rowKey = 'id',
  onRowClick,
  alto = 480,
  altoFila = 44,
  empty = 'Sin resultados',
}: TablaVirtualProps<Fila>): JSX.Element {
  const contenedor = useRef<HTMLDivElement>(null);
  const virtualizador = useVirtualizer({
    count: rows.length,
    getScrollElement: () => contenedor.current,
    estimateSize: () => altoFila,
    overscan: 8,
  });
  const items = virtualizador.getVirtualItems();
  const arriba = items.length > 0 ? items[0]!.start : 0;
  const abajo = items.length > 0 ? virtualizador.getTotalSize() - items[items.length - 1]!.end : 0;

  return (
    <div ref={contenedor} style={{ height: alto, overflow: 'auto' }}>
      <table className="ar-table">
        <thead style={{ position: 'sticky', top: 0, zIndex: 1, background: 'var(--surface-card)' }}>
          <tr>
            {columns.map((columna) => (
              <th key={columna.key} className={columna.align === 'right' ? 'is-num' : ''} style={{ width: columna.width }}>
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
            <>
              {arriba > 0 && (
                <tr aria-hidden="true">
                  <td colSpan={columns.length} style={{ height: arriba, padding: 0, border: 0 }} />
                </tr>
              )}
              {items.map((item) => {
                const fila = rows[item.index]!;
                return (
                  <tr
                    key={String(fila[rowKey] ?? item.index)}
                    data-index={item.index}
                    className={onRowClick ? 'is-clickable' : ''}
                    style={{ height: altoFila }}
                    onClick={onRowClick ? () => onRowClick(fila) : undefined}
                  >
                    {columns.map((columna) => (
                      <td key={columna.key} className={columna.align === 'right' ? 'is-num' : ''}>
                        {columna.render ? columna.render(fila) : (fila[columna.key] as string | number | undefined)}
                      </td>
                    ))}
                  </tr>
                );
              })}
              {abajo > 0 && (
                <tr aria-hidden="true">
                  <td colSpan={columns.length} style={{ height: abajo, padding: 0, border: 0 }} />
                </tr>
              )}
            </>
          )}
        </tbody>
      </table>
    </div>
  );
}
