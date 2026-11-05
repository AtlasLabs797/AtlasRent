import type { CSSProperties, JSX, ReactNode } from 'react';
import { Button } from '../core/Button';
import { Icon } from '../core/Icon';
import { Input } from '../forms/Input';

/** List toolbar: search + filter selects (children) + removable chips for active filters. Sits flush at the top of a padded={false} Card. */
export interface FilterBarProps {
  searchPlaceholder?: string;
  search?: string;
  onSearch?: (q: string) => void;
  filters?: { key: string; label: string; value: ReactNode }[];
  onRemove?: (key: string) => void;
  onClear?: () => void;
  children?: ReactNode;
  style?: CSSProperties;
}

export function FilterBar({ searchPlaceholder = 'Buscar', search, onSearch, filters = [], onRemove, onClear, children, style }: FilterBarProps): JSX.Element {
  return (
    <div className="ar-filterbar" style={style}>
      <Input icon="search" placeholder={searchPlaceholder} value={search} onChange={onSearch ? (e) => onSearch(e.target.value) : undefined} style={{ flex: '1 1 240px' }} />
      {children}
      {filters.length > 0 && (
        <div className="ar-filterbar__chips">
          {filters.map((f) => (
            <span key={f.key} className="ar-chip">
              <span>{f.label}</span>
              {f.value}
              <button type="button" aria-label={'Quitar ' + f.label} onClick={() => onRemove?.(f.key)}>
                <Icon name="x" size={12} />
              </button>
            </span>
          ))}
          <Button variant="ghost" size="sm" onClick={onClear}>
            Limpiar filtros
          </Button>
        </div>
      )}
    </div>
  );
}
