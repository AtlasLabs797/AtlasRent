import { cloneElement, isValidElement, useEffect, useRef, useState, type CSSProperties, type JSX, type KeyboardEvent, type ReactElement, type ReactNode } from 'react';
import { Icon } from '../core/Icon';

/** Action menu anchored to a trigger (row "…" actions, "Más" in page headers, AI actions). Keyboard: ↓/↑, ↵, Esc. */
export interface MenuItem {
  label?: ReactNode;
  value?: string;
  icon?: string;
  meta?: string;
  danger?: boolean;
  disabled?: boolean;
  tone?: 'ai';
  onClick?: () => void;
  separator?: boolean;
  heading?: string;
}

export interface MenuProps {
  /** A Button or IconButton element; Menu wires onClick/aria-expanded onto it */
  trigger: ReactElement;
  items: MenuItem[];
  align?: 'left' | 'right';
  /** Applies to every item; 'ai' tints icons violet */
  tone?: 'ai';
  onSelect?: (value: string, item: MenuItem) => void;
  style?: CSSProperties;
}

export function Menu({ trigger, items = [], align = 'left', tone, onSelect, style }: MenuProps): JSX.Element {
  const [abierto, setAbierto] = useState(false);
  const [resaltado, setResaltado] = useState(-1);
  const ref = useRef<HTMLDivElement>(null);
  const vivos = items.map((it, i) => ({ it, i })).filter((x) => !x.it.separator && !x.it.heading && !x.it.disabled);

  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setAbierto(false);
    };
    const tecla = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') setAbierto(false);
    };
    document.addEventListener('mousedown', fuera);
    document.addEventListener('keydown', tecla);
    return () => {
      document.removeEventListener('mousedown', fuera);
      document.removeEventListener('keydown', tecla);
    };
  }, [abierto]);

  const elegir = (it: MenuItem) => {
    setAbierto(false);
    it.onClick?.();
    onSelect?.(it.value ?? String(it.label), it);
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (!abierto && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      setAbierto(true);
      setResaltado(vivos[0]?.i ?? -1);
      return;
    }
    if (!abierto) return;
    const pos = vivos.findIndex((x) => x.i === resaltado);
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setResaltado(vivos[Math.min(vivos.length - 1, pos + 1)]?.i ?? -1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setResaltado(vivos[Math.max(0, pos - 1)]?.i ?? -1);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const x = items[resaltado];
      if (x) elegir(x);
    }
  };
  const disparador = isValidElement(trigger)
    ? cloneElement(trigger as ReactElement<Record<string, unknown>>, {
        onClick: () => setAbierto((o) => !o),
        onKeyDown: alTeclear,
        'aria-haspopup': 'menu',
        'aria-expanded': abierto,
      })
    : trigger;

  return (
    <div className="ar-pop" ref={ref} style={{ display: 'inline-flex', ...style }}>
      {disparador}
      {abierto && (
        <ul className={'ar-menu ar-menu--actions' + (align === 'right' ? ' ar-menu--right' : '')} role="menu">
          {items.map((it, i) =>
            it.separator ? (
              <li key={'s' + i} className="ar-menu__sep" role="separator" />
            ) : it.heading ? (
              <li key={'h' + i} className="ar-menu__label" role="presentation">
                {it.heading}
              </li>
            ) : (
              <li
                key={(it.value ?? String(it.label)) + i}
                role="menuitem"
                aria-disabled={it.disabled || undefined}
                data-tone={it.danger ? 'danger' : (it.tone || tone)}
                data-hi={i === resaltado}
                onMouseEnter={() => !it.disabled && setResaltado(i)}
                onMouseDown={(e) => {
                  e.preventDefault();
                  if (!it.disabled) elegir(it);
                }}
              >
                {it.icon && <Icon name={it.icon} size={15} />}
                <span className="ar-menu__text">{it.label}</span>
                {it.meta && <span className="ar-menu__meta">{it.meta}</span>}
              </li>
            ),
          )}
        </ul>
      )}
    </div>
  );
}
