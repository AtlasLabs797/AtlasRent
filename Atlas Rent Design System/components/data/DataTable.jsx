import React from 'react';
export function DataTable({ columns = [], rows = [], rowKey = 'id', onRowClick, selectedKey, empty = 'Sin resultados', style }) {
  return <div style={{ overflowX: 'auto', ...style }}><table className="ar-table">
    <thead><tr>{columns.map(c => <th key={c.key} className={c.align === 'right' ? 'is-num' : ''} style={{ width: c.width }}>{c.label}</th>)}</tr></thead>
    <tbody>{rows.length === 0 ? <tr><td colSpan={columns.length} style={{ textAlign: 'center', color: 'var(--text-muted)', height: 88 }}>{empty}</td></tr> :
      rows.map((r, i) => <tr key={r[rowKey] ?? i} className={onRowClick ? 'is-clickable' : ''} data-selected={selectedKey != null && r[rowKey] === selectedKey} onClick={onRowClick ? () => onRowClick(r) : undefined}>
        {columns.map(c => <td key={c.key} className={c.align === 'right' ? 'is-num' : ''}>{c.render ? c.render(r) : r[c.key]}</td>)}
      </tr>)}</tbody>
  </table></div>;
}
