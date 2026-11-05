import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';
import { Money } from './Money';

/** Exit review of a contract's frozen inventory copy: each item OK / Faltante / Dañado + importe a descontar. No photos by design. */
export interface InventoryItem {
  name: string;
  qty?: number;
  state?: 'ok' | 'faltante' | 'danado';
  amount?: number | string;
  note?: string;
}
export interface InventoryChecklistProps {
  items: InventoryItem[];
  currency?: 'USD' | 'DOP';
  editable?: boolean;
  onChange?: (items: InventoryItem[]) => void;
  /** e.g. "inventario base · 01/03/2025" */
  frozenFrom?: string;
  style?: CSSProperties;
}

const ESTADOS: Array<[InventoryItem['state'] & string, string, ('warning' | 'danger')?]> = [
  ['ok', 'OK'],
  ['faltante', 'Faltante', 'warning'],
  ['danado', 'Dañado', 'danger'],
];

export function InventoryChecklist({ items = [], currency = 'USD', editable = false, onChange, frozenFrom, style }: InventoryChecklistProps): JSX.Element {
  const poner = (i: number, k: keyof InventoryItem, v: string) => onChange?.(items.map((it, j) => (j === i ? { ...it, [k]: v } : it)));
  const malos = items.filter((i) => i.state && i.state !== 'ok');
  const total = malos.reduce((s, i) => s + (Number(i.amount) || 0), 0);
  const revisados = items.filter((i) => i.state).length;
  return (
    <div style={style}>
      {frozenFrom && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, font: 'var(--type-caption)', color: 'var(--text-muted)', marginBottom: 8 }}>
          <Icon name="lock" size={13} />
          Copia congelada de {frozenFrom}
        </div>
      )}
      {items.map((it, i) => {
        const incidencia = it.state && it.state !== 'ok';
        return (
          <div key={i} className="ar-inv__row">
            <div style={{ minWidth: 0 }}>
              <div style={{ font: 'var(--type-body-sm)', color: 'var(--text-strong)' }}>
                {it.name}
                {(it.qty ?? 0) > 1 && <span className="mono" style={{ color: 'var(--text-subtle)' }}> × {it.qty}</span>}
              </div>
              {it.note && <div style={{ font: 'var(--type-fine)', color: 'var(--text-muted)' }}>{it.note}</div>}
            </div>
            {editable ? (
              <div className="ar-mini-seg">
                {ESTADOS.map(([k, l, t]) => (
                  <button key={k} type="button" data-tone={t} aria-pressed={it.state === k} onClick={() => poner(i, 'state', k)}>
                    {l}
                  </button>
                ))}
              </div>
            ) : (
              <span
                className="ar-badge"
                style={incidencia ? { background: 'var(--' + (it.state === 'danado' ? 'danger' : 'warning') + '-soft)', color: 'var(--' + (it.state === 'danado' ? 'danger' : 'warning') + ')' } : undefined}
              >
                {ESTADOS.find((s) => s[0] === it.state)?.[1] ?? 'Sin revisar'}
              </span>
            )}
            <div style={{ textAlign: 'right' }}>
              {incidencia ? (
                editable ? (
                  <div className="ar-control" style={{ height: 36, padding: '0 10px' }}>
                    <span className="ar-control__affix" style={{ order: 2 }}>
                      {currency === 'DOP' ? 'DOP' : 'USD'}
                    </span>
                    <input
                      inputMode="decimal"
                      placeholder="0,00"
                      value={it.amount ?? ''}
                      onChange={(e) => poner(i, 'amount', e.target.value)}
                      style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontSize: 14 }}
                    />
                  </div>
                ) : (
                  <Money amount={-(Number(it.amount) || 0)} currency={currency} tone="danger" />
                )
              ) : (
                <span style={{ color: 'var(--text-subtle)' }}>—</span>
              )}
            </div>
          </div>
        );
      })}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12, paddingTop: 14, font: 'var(--type-body-sm)', color: 'var(--text-muted)' }}>
        <span className="mono">
          {revisados}/{items.length} revisados · {malos.length} con incidencia
        </span>
        <span>
          Descuento propuesto a fianza <Money amount={total} currency={currency} strong tone={total ? 'danger' : 'muted'} />
        </span>
      </div>
    </div>
  );
}
