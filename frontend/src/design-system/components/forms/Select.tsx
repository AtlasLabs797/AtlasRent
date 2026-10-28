import { useEffect, useRef, useState, type CSSProperties, type JSX, type KeyboardEvent } from 'react';
import { Icon } from '../core/Icon';

/** Custom Atlas dropdown (listbox popover, keyboard accessible). onChange receives { target: { value, name } }. */
export interface SelectProps {
  label?: string;
  hint?: string;
  error?: string;
  required?: boolean;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
  name?: string;
  value?: string;
  defaultValue?: string;
  onChange?: (e: { target: { value: string; name?: string } }) => void;
  options?: Array<string | { value: string; label: string }>;
  style?: CSSProperties;
}

export function Select({
  label,
  hint,
  error,
  required,
  options = [],
  placeholder,
  disabled,
  id,
  style,
  value,
  defaultValue,
  onChange,
  name,
}: SelectProps): JSX.Element {
  const fid = id || (label ? 's-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const opts = options.map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
  const [interno, setInterno] = useState(defaultValue ?? (placeholder ? '' : (opts[0]?.value ?? '')));
  const actual = value !== undefined ? value : interno;
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

  const elegir = (v: string) => {
    setInterno(v);
    setAbierto(false);
    onChange?.({ target: { value: v, name } });
  };
  const seleccion = opts.find((o) => String(o.value) === String(actual));
  const abrir = () => {
    if (disabled) return;
    setResaltado(Math.max(0, opts.findIndex((o) => String(o.value) === String(actual))));
    setAbierto(true);
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (disabled) return;
    if (e.key === 'Escape') setAbierto(false);
    else if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (abierto) setResaltado((i) => Math.min(opts.length - 1, i + 1));
      else abrir();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (abierto) setResaltado((i) => Math.max(0, i - 1));
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      if (abierto) {
        const o = opts[resaltado];
        if (o) elegir(o.value);
      } else abrir();
    }
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
        <button
          type="button"
          id={fid}
          disabled={disabled}
          aria-haspopup="listbox"
          aria-expanded={abierto}
          onClick={() => (abierto ? setAbierto(false) : abrir())}
          onKeyDown={alTeclear}
          className={
            'ar-control ar-select' +
            (error ? ' ar-control--error' : '') +
            (disabled ? ' ar-control--disabled' : '') +
            (abierto ? ' is-open' : '')
          }
        >
          <span className={'ar-select__value' + (seleccion ? '' : ' is-placeholder')}>
            {seleccion ? seleccion.label : placeholder || ''}
          </span>
          <Icon name="chevron-down" size={15} />
        </button>
        {name && <input type="hidden" name={name} value={actual} />}
        {abierto && (
          <ul className="ar-menu" role="listbox">
            {opts.map((o, i) => (
              <li
                key={o.value}
                role="option"
                aria-selected={String(o.value) === String(actual)}
                data-hi={i === resaltado}
                onMouseEnter={() => setResaltado(i)}
                onMouseDown={(e) => {
                  e.preventDefault();
                  elegir(o.value);
                }}
              >
                <span>{o.label}</span>
                {String(o.value) === String(actual) && <Icon name="check" size={14} />}
              </li>
            ))}
          </ul>
        )}
      </div>
      {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
    </div>
  );
}
