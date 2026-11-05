import { Fragment, useEffect, useRef, useState, type JSX, type KeyboardEvent } from 'react';
import { Icon } from '../core/Icon';
import { Kbd } from '../core/Kbd';

/** Global search + quick actions (⌘K / Ctrl K). One place to jump to any cliente, unidad, factura or contrato, or run an action. */
export interface CommandItem {
  label: string;
  value?: string;
  sub?: string;
  /** Mono ID, e.g. FAC-A-2026-00412 */
  meta?: string;
  icon?: string;
  tone?: 'ai';
}
export interface CommandGroup {
  label: string;
  items: CommandItem[];
}
export interface CommandPaletteProps {
  open: boolean;
  onOpenChange?: (open: boolean) => void;
  groups: CommandGroup[];
  onSelect?: (value: string, item: CommandItem) => void;
  placeholder?: string;
  emptyText?: string;
  /** Bind ⌘K / Ctrl K and Esc. Default true */
  hotkey?: boolean;
  /** Render in place without scrim (specimens) */
  inline?: boolean;
}

const norm = (s: unknown) =>
  String(s ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase();

export function CommandPalette({
  open,
  onOpenChange,
  groups = [],
  onSelect,
  placeholder = 'Buscar cliente, unidad, factura o acción',
  emptyText = 'Sin resultados. Prueba con el RNC, el nº de factura o el nombre de la unidad.',
  hotkey = true,
  inline,
}: CommandPaletteProps): JSX.Element | null {
  const [q, setQ] = useState('');
  const [resaltado, setResaltado] = useState(0);
  const entrada = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!hotkey) return;
    const tecla = (e: globalThis.KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        onOpenChange?.(!open);
      } else if (e.key === 'Escape' && open) onOpenChange?.(false);
    };
    document.addEventListener('keydown', tecla);
    return () => document.removeEventListener('keydown', tecla);
  }, [open, hotkey, onOpenChange]);
  useEffect(() => {
    if (open) {
      setQ('');
      setResaltado(0);
      setTimeout(() => {
        if (entrada.current && !inline) entrada.current.focus();
      }, 0);
    }
  }, [open, inline]);

  if (!open && !inline) return null;
  const filtrados = groups
    .map((g) => ({ ...g, items: g.items.filter((it) => !q || norm(it.label + ' ' + (it.sub || '') + ' ' + (it.meta || '')).includes(norm(q))) }))
    .filter((g) => g.items.length);
  const plano = filtrados.flatMap((g) => g.items);
  const elegir = (it: CommandItem) => {
    onSelect?.(it.value ?? it.label, it);
    onOpenChange?.(false);
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setResaltado((i) => Math.min(plano.length - 1, i + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setResaltado((i) => Math.max(0, i - 1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const it = plano[resaltado];
      if (it) elegir(it);
    }
  };
  let n = -1;
  const panel = (
    <div className={'ar-cmd' + (inline ? ' ar-cmd--inline' : '')} role="dialog" aria-label="Búsqueda global" onClick={(e) => e.stopPropagation()}>
      <div className="ar-cmd__input">
        <Icon name="search" size={18} />
        <input
          ref={entrada}
          value={q}
          placeholder={placeholder}
          onChange={(e) => {
            setQ(e.target.value);
            setResaltado(0);
          }}
          onKeyDown={alTeclear}
        />
        <Kbd>esc</Kbd>
      </div>
      {plano.length === 0 ? (
        <div className="ar-cmd__empty">{emptyText}</div>
      ) : (
        <ul className="ar-cmd__list" role="listbox">
          {filtrados.map((g) => (
            <Fragment key={g.label}>
              <li className="ar-overline ar-cmd__group" role="presentation">
                {g.label}
              </li>
              {g.items.map((it) => {
                n++;
                const i = n;
                return (
                  <li
                    key={g.label + (it.value ?? it.label)}
                    role="option"
                    aria-selected={i === resaltado}
                    className="ar-cmd__item"
                    data-hi={i === resaltado}
                    data-tone={it.tone}
                    onMouseEnter={() => setResaltado(i)}
                    onClick={() => elegir(it)}
                  >
                    <span className="ar-cmd__icon">
                      <Icon name={it.icon || 'arrow-right'} size={16} />
                    </span>
                    <span className="ar-cmd__text">
                      <b>{it.label}</b>
                      {it.sub && <span>{it.sub}</span>}
                    </span>
                    {it.meta && <span className="ar-cmd__meta">{it.meta}</span>}
                  </li>
                );
              })}
            </Fragment>
          ))}
        </ul>
      )}
      <div className="ar-cmd__foot">
        <span>
          <Kbd>↑</Kbd>
          <Kbd>↓</Kbd>navegar
        </span>
        <span>
          <Kbd>↵</Kbd>abrir
        </span>
        <span>
          <Kbd>⌘</Kbd>
          <Kbd>K</Kbd>cerrar
        </span>
      </div>
    </div>
  );
  return inline ? panel : (
    <div className="ar-cmd-scrim" onClick={() => onOpenChange?.(false)}>
      {panel}
    </div>
  );
}
