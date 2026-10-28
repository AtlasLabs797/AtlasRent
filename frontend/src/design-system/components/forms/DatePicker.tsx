import { useEffect, useRef, useState, type ChangeEvent, type CSSProperties, type JSX } from 'react';
import { Icon } from '../core/Icon';

/** Text date field (dd/mm/aaaa, typed by hand) with a calendar dropdown. Value is ISO yyyy-mm-dd. */
export interface DatePickerProps {
  label?: string;
  hint?: string;
  error?: string;
  required?: boolean;
  disabled?: boolean;
  id?: string;
  name?: string;
  placeholder?: string;
  value?: string;
  defaultValue?: string;
  min?: string;
  max?: string;
  style?: CSSProperties;
  onChange?: (e: { target: { value: string; name?: string } }) => void;
}

const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
const DIAS = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];
const p2 = (n: number) => String(n).padStart(2, '0');
const aIso = (d: Date) => d.getFullYear() + '-' + p2(d.getMonth() + 1) + '-' + p2(d.getDate());
const deIso = (s: string | undefined) => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || '');
  return m ? new Date(+m[1]!, +m[2]! - 1, +m[3]!) : null;
};
const formato = (s: string | undefined) => {
  const d = deIso(s);
  return d ? p2(d.getDate()) + '/' + p2(d.getMonth() + 1) + '/' + d.getFullYear() : '';
};
const leer = (t: string) => {
  const m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(t.trim());
  if (!m) return null;
  const d = new Date(+m[3]!, +m[2]! - 1, +m[1]!);
  return d.getMonth() === +m[2]! - 1 ? aIso(d) : null;
};

export function DatePicker({
  label,
  hint,
  error,
  required,
  disabled,
  id,
  style,
  value,
  defaultValue,
  onChange,
  name,
  min,
  max,
  placeholder = 'dd/mm/aaaa',
}: DatePickerProps): JSX.Element {
  const fid = id || (label ? 'd-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  const [interno, setInterno] = useState(defaultValue || '');
  const actual = value !== undefined ? value : interno;
  const [texto, setTexto] = useState(formato(actual));
  const [abierto, setAbierto] = useState(false);
  const [vista, setVista] = useState<Date>(() => deIso(actual) || new Date());
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setTexto(formato(actual));
  }, [actual]);
  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setAbierto(false);
        setTexto(formato(actual));
      }
    };
    document.addEventListener('mousedown', fuera);
    return () => document.removeEventListener('mousedown', fuera);
  }, [abierto, actual]);

  const fijar = (iso: string) => {
    setInterno(iso);
    setTexto(formato(iso));
    onChange?.({ target: { value: iso, name } });
  };
  const valida = (iso: string) => (!min || iso >= min) && (!max || iso <= max);
  const alEscribir = (e: ChangeEvent<HTMLInputElement>) => {
    const t = e.target.value;
    setTexto(t);
    const iso = leer(t);
    if (iso && valida(iso)) {
      fijar(iso);
      setVista(deIso(iso)!);
    }
  };
  const alSalir = () => {
    const iso = leer(texto);
    if (iso && valida(iso)) fijar(iso);
    else if (!texto.trim()) fijar('');
    else setTexto(formato(actual));
  };
  const y = vista.getFullYear();
  const m = vista.getMonth();
  const previos = (new Date(y, m, 1).getDay() + 6) % 7;
  const dias = new Date(y, m + 1, 0).getDate();
  const celdas: Array<number | null> = [...Array<null>(previos).fill(null), ...Array.from({ length: dias }, (_, i) => i + 1)];
  const hoy = aIso(new Date());
  const mover = (n: number) => setVista(new Date(y, m + n, 1));

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
          <input
            id={fid}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            disabled={disabled}
            placeholder={placeholder}
            value={texto}
            onChange={alEscribir}
            onBlur={alSalir}
            onFocus={() => !disabled && setAbierto(true)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') setAbierto(false);
              if (e.key === 'Enter') {
                alSalir();
                setAbierto(false);
              }
            }}
          />
          <button type="button" className="ar-control__btn" aria-label="Abrir calendario" disabled={disabled} onClick={() => setAbierto((o) => !o)}>
            <Icon name="calendar-days" size={15} />
          </button>
        </div>
        {abierto && (
          <div className="ar-menu ar-cal">
            <div className="ar-cal__head">
              <button type="button" aria-label="Mes anterior" onClick={() => mover(-1)}>
                <Icon name="chevron-left" size={15} />
              </button>
              <strong>
                {MESES[m]} {y}
              </strong>
              <button type="button" aria-label="Mes siguiente" onClick={() => mover(1)}>
                <Icon name="chevron-right" size={15} />
              </button>
            </div>
            <div className="ar-cal__grid">
              {DIAS.map((d) => (
                <span key={d} className="ar-cal__dow">
                  {d}
                </span>
              ))}
              {celdas.map((d, i) => {
                if (!d) return <span key={'e' + i} />;
                const iso = y + '-' + p2(m + 1) + '-' + p2(d);
                return (
                  <button
                    type="button"
                    key={iso}
                    disabled={!valida(iso)}
                    data-sel={iso === actual}
                    data-today={iso === hoy}
                    onClick={() => {
                      fijar(iso);
                      setAbierto(false);
                    }}
                  >
                    {d}
                  </button>
                );
              })}
            </div>
            <div className="ar-cal__foot">
              <button
                type="button"
                onClick={() => {
                  fijar(hoy);
                  setVista(new Date());
                  setAbierto(false);
                }}
              >
                Hoy
              </button>
              {actual && (
                <button
                  type="button"
                  onClick={() => {
                    fijar('');
                    setAbierto(false);
                  }}
                >
                  Borrar
                </button>
              )}
            </div>
          </div>
        )}
      </div>
      {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
    </div>
  );
}
