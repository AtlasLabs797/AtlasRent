import type { CSSProperties, JSX } from 'react';
import { Button } from '../core/Button';
import { IconButton } from '../core/IconButton';
import { Money } from './Money';

/** Editable invoice line items: concepto, detalle, cantidad, precio, importe + totals and DOP equivalent at the frozen rate. Descuento lines subtract. Solo vista previa: la API calcula los importes. */
export interface InvoiceLine {
  concept: string;
  detail?: string;
  qty: number | string;
  price: number | string;
}
export interface InvoiceLinesProps {
  lines: InvoiceLine[];
  currency?: 'USD' | 'DOP';
  rate?: number;
  editable?: boolean;
  onChange?: (lines: InvoiceLine[]) => void;
  concepts?: string[];
  style?: CSSProperties;
}

const CONCEPTOS = ['Renta mensual', 'Prorrateo', 'Mora', 'Ajuste', 'Servicio', 'Cargo adicional', 'Electricidad', 'Agua', 'Descuento'];

export function InvoiceLines({ lines = [], currency = 'USD', rate, editable = false, onChange, concepts = CONCEPTOS, style }: InvoiceLinesProps): JSX.Element {
  const valor = (l: InvoiceLine) => (l.concept === 'Descuento' ? -1 : 1) * (Number(l.qty) || 0) * (Number(l.price) || 0);
  const total = lines.reduce((s, l) => s + valor(l), 0);
  const descuentos = lines.reduce((s, l) => s + Math.min(0, valor(l)), 0);
  const poner = (i: number, k: keyof InvoiceLine, v: string) => onChange?.(lines.map((l, j) => (j === i ? { ...l, [k]: v } : l)));
  return (
    <div style={style}>
      <div style={{ overflowX: 'auto' }}>
        <table className="ar-table ar-lines__table">
          <thead>
            <tr>
              <th>Concepto</th>
              <th>Detalle</th>
              <th className="is-num" style={{ width: 90 }}>Cant.</th>
              <th className="is-num" style={{ width: 150 }}>Precio</th>
              <th className="is-num" style={{ width: 160 }}>Importe</th>
              {editable && <th style={{ width: 52 }} />}
            </tr>
          </thead>
          <tbody>
            {lines.map((l, i) => (
              <tr key={i}>
                <td>
                  {editable ? (
                    <select className="ar-cell-input" value={l.concept} onChange={(e) => poner(i, 'concept', e.target.value)}>
                      {concepts.map((c) => (
                        <option key={c}>{c}</option>
                      ))}
                    </select>
                  ) : (
                    <span style={{ color: 'var(--text-strong)' }}>{l.concept}</span>
                  )}
                </td>
                <td>
                  {editable ? (
                    <input className="ar-cell-input" value={l.detail || ''} placeholder="Periodo o nota" onChange={(e) => poner(i, 'detail', e.target.value)} />
                  ) : (
                    <span style={{ color: 'var(--text-muted)' }}>{l.detail}</span>
                  )}
                </td>
                <td className="is-num">{editable ? <input className="ar-cell-input is-num" inputMode="decimal" value={l.qty} onChange={(e) => poner(i, 'qty', e.target.value)} /> : l.qty}</td>
                <td className="is-num">
                  {editable ? (
                    <input className="ar-cell-input is-num" inputMode="decimal" value={l.price} onChange={(e) => poner(i, 'price', e.target.value)} />
                  ) : (
                    Number(l.price).toLocaleString('de-DE', { minimumFractionDigits: 2 })
                  )}
                </td>
                <td className="is-num">
                  <Money amount={valor(l)} currency={currency} tone={valor(l) < 0 ? 'danger' : undefined} />
                </td>
                {editable && (
                  <td>
                    <IconButton size="sm" icon="trash-2" label="Quitar línea" onClick={() => onChange?.(lines.filter((_, j) => j !== i))} />
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="ar-lines__foot">
        {editable ? (
          <Button variant="ghost" size="sm" icon="plus" onClick={() => onChange?.([...lines, { concept: 'Cargo adicional', detail: '', qty: 1, price: 0 }])}>
            Añadir línea
          </Button>
        ) : (
          <span />
        )}
        <dl className="ar-totals">
          {descuentos < 0 && (
            <div>
              <dt>Descuentos</dt>
              <dd>
                <Money amount={descuentos} currency={currency} tone="danger" />
              </dd>
            </div>
          )}
          <div className="is-total">
            <dt>Total</dt>
            <dd>
              <Money amount={total} currency={currency} strong />
            </dd>
          </div>
          {rate && currency === 'USD' && (
            <div>
              <dt>Equivalente · {Number(rate).toFixed(2).replace('.', ',')}</dt>
              <dd>
                <Money amount={total * rate} currency="DOP" tone="muted" />
              </dd>
            </div>
          )}
        </dl>
      </div>
    </div>
  );
}
