import type { CSSProperties, JSX } from 'react';
import { Button } from '../core/Button';
import { Money } from './Money';

/** Applies one cobro across open invoices of the same contract. Default = oldest first; remainder becomes saldo a favor; over-allocation is flagged. Solo vista previa: la API valida la asignación. */
export interface AllocationInvoice {
  id: string;
  concept?: string;
  period?: string;
  pending: number;
  applied?: number | string;
  overdue?: boolean;
}
export interface PaymentAllocationProps {
  amount: number;
  currency?: 'USD' | 'DOP';
  invoices: AllocationInvoice[];
  onChange?: (invoices: AllocationInvoice[]) => void;
  style?: CSSProperties;
}

const n = (v: unknown) => Number(v) || 0;

export function PaymentAllocation({ amount = 0, currency = 'USD', invoices = [], onChange, style }: PaymentAllocationProps): JSX.Element {
  const aplicado = invoices.reduce((s, i) => s + n(i.applied), 0);
  const aFavor = amount - aplicado;
  const auto = () => {
    let resto = amount;
    onChange?.(
      invoices.map((i) => {
        const a = Math.max(0, Math.min(n(i.pending), resto));
        resto -= a;
        return { ...i, applied: a };
      }),
    );
  };
  const poner = (k: number, v: string) => onChange?.(invoices.map((i, j) => (j === k ? { ...i, applied: v } : i)));
  return (
    <div style={style}>
      <div className="ar-alloc__head">
        <div>
          <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>Importe recibido</div>
          <Money amount={amount} currency={currency} strong style={{ fontSize: 21 }} />
        </div>
        <Button variant="ghost" size="sm" icon="list-ordered" onClick={auto}>
          Aplicar a la más antigua
        </Button>
      </div>
      <div style={{ marginTop: 6 }}>
        {invoices.map((i, k) => {
          const resto = n(i.pending) - n(i.applied);
          return (
            <div key={i.id} className="ar-alloc__row">
              <div style={{ minWidth: 0 }}>
                <div style={{ font: 'var(--type-mono)', color: 'var(--text-strong)' }}>{i.id}</div>
                <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>{[i.concept, i.period].filter(Boolean).join(' · ')}</div>
              </div>
              <div style={{ textAlign: 'right' }}>
                <Money amount={i.pending} currency={currency} tone={i.overdue ? 'danger' : undefined} />
                <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>
                  {resto <= 0 ? 'Quedará pagada' : 'Quedan ' + resto.toLocaleString('de-DE', { minimumFractionDigits: 2 })}
                </div>
              </div>
              <div className="ar-control" style={{ height: 40, padding: '0 12px' }}>
                <span className="ar-control__affix" style={{ order: 2 }}>
                  {currency === 'DOP' ? 'DOP' : 'USD'}
                </span>
                <input
                  inputMode="decimal"
                  value={i.applied ?? ''}
                  placeholder="0,00"
                  onChange={(e) => poner(k, e.target.value)}
                  style={{ textAlign: 'right', fontFamily: 'var(--font-mono)', fontSize: 14 }}
                />
              </div>
            </div>
          );
        })}
      </div>
      <dl className="ar-totals" style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border-default)' }}>
        <div>
          <dt>Aplicado a facturas</dt>
          <dd>
            <Money amount={aplicado} currency={currency} />
          </dd>
        </div>
        <div className="is-total">
          <dt>{aFavor < 0 ? 'Excede el importe recibido' : 'Saldo a favor'}</dt>
          <dd>
            <Money amount={aFavor} currency={currency} strong tone={aFavor < 0 ? 'danger' : aFavor > 0 ? 'success' : 'muted'} />
          </dd>
        </div>
      </dl>
    </div>
  );
}
