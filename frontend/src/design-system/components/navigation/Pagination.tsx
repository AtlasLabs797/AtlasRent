import type { CSSProperties, JSX } from 'react';
import { IconButton } from '../core/IconButton';

/** Table footer pagination: "1–25 de 150" + compact page list. */
export interface PaginationProps {
  page?: number;
  pageSize?: number;
  total?: number;
  onChange?: (page: number) => void;
  style?: CSSProperties;
}

export function Pagination({ page = 1, pageSize = 25, total = 0, onChange, style }: PaginationProps): JSX.Element {
  const paginas = Math.max(1, Math.ceil(total / pageSize));
  const desde = total ? (page - 1) * pageSize + 1 : 0;
  const hasta = Math.min(total, page * pageSize);
  const lista: Array<number | '…'> = [];
  for (let p = 1; p <= paginas; p++) {
    if (p === 1 || p === paginas || Math.abs(p - page) <= 1) lista.push(p);
    else if (lista[lista.length - 1] !== '…') lista.push('…');
  }
  const ir = (p: number) => {
    if (p >= 1 && p <= paginas) onChange?.(p);
  };
  return (
    <div className="ar-pager" style={style}>
      <span className="mono">
        {desde}–{hasta} de {total}
      </span>
      <div className="ar-pager__pages">
        <IconButton size="sm" icon="chevron-left" label="Anterior" disabled={page <= 1} onClick={() => ir(page - 1)} />
        {lista.map((p, i) =>
          p === '…' ? (
            <span key={'e' + i} style={{ padding: '0 4px' }}>
              …
            </span>
          ) : (
            <button key={p} type="button" className="ar-pager__page" aria-current={p === page ? 'page' : undefined} onClick={() => ir(p)}>
              {p}
            </button>
          ),
        )}
        <IconButton size="sm" icon="chevron-right" label="Siguiente" disabled={page >= paginas} onClick={() => ir(page + 1)} />
      </div>
    </div>
  );
}
