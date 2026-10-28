import { useEffect, useRef, useState, type CSSProperties, type JSX, type KeyboardEvent } from 'react';
import { Icon } from '../core/Icon';

/** Searchable record picker (cliente por nombre, RNC o nº; unidad; contrato). Filters accent-insensitively over label, sub and meta. */
export interface ComboboxOption {
  value: string;
  label: string;
  /** Secondary line, e.g. "RNC 1-31-45678-2 · Santo Domingo" */
  sub?: string;
  /** Mono ID shown right, e.g. "CLI-01873" */
  meta?: string;
}

export interface ComboboxProps {
  label?: string;
  hint?: string;
  error?: string;
  required?: boolean;
  placeholder?: string;
  options: ComboboxOption[];
  value?: string | null;
  onChange?: (value: string | null, option: ComboboxOption | null) => void;
  /** Shows "Crear «query»" when nothing matches exactly */
  onCreate?: (query: string) => void;
  createLabel?: string;
  icon?: string;
  emptyText?: string;
  disabled?: boolean;
  id?: string;
  style?: CSSProperties;
}

const norm = (s: unknown) =>
  String(s ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase();

export function Combobox({
  label,
  hint,
  error,
  required,
  placeholder = 'Buscar',
  options = [],
  value,
  onChange,
  onCreate,
  createLabel = 'Crear',
  icon = 'search',
  emptyText = 'Sin coincidencias',
  disabled,
  id,
  style,
}: ComboboxProps): JSX.Element {
  const fid = id || (label ? 'cb-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const [interno, setInterno] = useState<string | null>(null);
  const actual = value !== undefined ? value : interno;
  const seleccion = options.find((o) => o.value === actual);
  const [q, setQ] = useState('');
  const [abierto, setAbierto] = useState(false);
  const [resaltado, setResaltado] = useState(0);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener('mousedown', fuera);
    return () => document.removeEventListener('mousedown', fuera);
  }, [abierto]);

  const lista = options
    .filter((o) => !q || norm(o.label + ' ' + (o.sub || '') + ' ' + (o.meta || '')).includes(norm(q)))
    .slice(0, 50);
  const puedeCrear = Boolean(onCreate && q.trim() && !lista.some((o) => norm(o.label) === norm(q)));
  const total = lista.length + (puedeCrear ? 1 : 0);
  const elegir = (o: ComboboxOption) => {
    setInterno(o.value);
    setQ('');
    setAbierto(false);
    onChange?.(o.value, o);
  };
  const crear = () => {
    setAbierto(false);
    onCreate?.(q.trim());
    setQ('');
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setAbierto(true);
      setResaltado((i) => Math.min(total - 1, i + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setResaltado((i) => Math.max(0, i - 1));
    } else if (e.key === 'Enter' && abierto) {
      e.preventDefault();
      const o = lista[resaltado];
      if (resaltado < lista.length) {
        if (o) elegir(o);
      } else if (puedeCrear) crear();
    } else if (e.key === 'Escape') setAbierto(false);
  };

  return (
    <div className="ar-field" style={style} ref={ref}>
      {label && (
        <label className="ar-label" htmlFor={fid}>
          {label}
          {required && <em> *</em>}
        </label>
      )}
      <div className="ar-pop">
        <div className={'ar-control' + (error ? ' ar-control--error' : '') + (disabled ? ' ar-control--disabled' : '')}>
          {icon && <Icon name={icon} size={15} />}
          <input
            id={fid}
            role="combobox"
            aria-expanded={abierto}
            aria-autocomplete="list"
            autoComplete="off"
            disabled={disabled}
            placeholder={seleccion ? '' : placeholder}
            value={abierto ? q : seleccion ? seleccion.label : q}
            onFocus={() => {
              setAbierto(true);
              setResaltado(0);
            }}
            onChange={(e) => {
              setQ(e.target.value);
              setAbierto(true);
              setResaltado(0);
            }}
            onKeyDown={alTeclear}
          />
          {seleccion && seleccion.meta && !abierto && <span className="ar-control__affix">{seleccion.meta}</span>}
          {seleccion && !disabled && (
            <button
              type="button"
              className="ar-control__btn"
              aria-label="Quitar selección"
              onClick={() => {
                setInterno(null);
                onChange?.(null, null);
              }}
            >
              <Icon name="x" size={14} />
            </button>
          )}
        </div>
        {abierto && (
          <ul className="ar-menu ar-menu--rich" role="listbox">
            {lista.map((o, i) => (
              <li
                key={o.value}
                role="option"
                aria-selected={o.value === actual}
                data-hi={i === resaltado}
                onMouseEnter={() => setResaltado(i)}
                onMouseDown={(e) => {
                  e.preventDefault();
                  elegir(o);
                }}
              >
                <span className="ar-opt">
                  <span className="ar-opt__title">{o.label}</span>
                  {o.sub && <span className="ar-opt__sub">{o.sub}</span>}
                </span>
                {o.meta && <span className="ar-menu__meta">{o.meta}</span>}
                {o.value === actual && <Icon name="check" size={14} />}
              </li>
            ))}
            {lista.length === 0 && !puedeCrear && <li className="ar-menu__empty">{emptyText}</li>}
            {puedeCrear && (
              <li
                className="ar-menu__create"
                data-hi={resaltado === lista.length}
                onMouseEnter={() => setResaltado(lista.length)}
                onMouseDown={(e) => {
                  e.preventDefault();
                  crear();
                }}
              >
                <Icon name="plus" size={14} />
                <span className="ar-menu__text">
                  {createLabel} «{q.trim()}»
                </span>
              </li>
            )}
          </ul>
        )}
      </div>
      {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
    </div>
  );
}
