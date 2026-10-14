import React from 'react';
import { Input } from '../forms/Input.jsx';
import { Icon } from '../core/Icon.jsx';
import { Button } from '../core/Button.jsx';
export function FilterBar({ searchPlaceholder = 'Buscar', search, onSearch, filters = [], onRemove, onClear, children, style }) {
  return <div className="ar-filterbar" style={style}>
    <Input icon="search" placeholder={searchPlaceholder} value={search} onChange={onSearch ? e => onSearch(e.target.value) : undefined} style={{ flex: '1 1 240px' }} />
    {children}
    {filters.length > 0 && <div className="ar-filterbar__chips">{filters.map(f => <span key={f.key} className="ar-chip"><span>{f.label}</span>{f.value}<button type="button" aria-label={'Quitar ' + f.label} onClick={() => onRemove && onRemove(f.key)}><Icon name="x" size={12} /></button></span>)}
      <Button variant="ghost" size="sm" onClick={onClear}>Limpiar filtros</Button></div>}
  </div>;
}