import type { CSSProperties, JSX } from 'react';
import { Badge, type EstadoBadge } from '../core/Badge';
import { KeyValueList } from './KeyValueList';
import { Money } from './Money';

/** Fianza as its own financial entity: exigido/cobrado/pendiente, plus liquidación with descuentos and importe devuelto at contract close. */
export interface DepositSummaryProps {
  required: number;
  count?: number;
  collected?: number;
  currency?: 'USD' | 'DOP';
  contract?: string;
  status?: 'retenida' | 'pendiente' | 'devolucion' | 'devuelta' | 'parcial';
  deductions?: { reason: string; amount: number }[];
  returned?: number;
  returnedAt?: string;
  method?: string;
  style?: CSSProperties;
}

export function DepositSummary({
  required = 0,
  count = 1,
  collected = 0,
  currency = 'USD',
  contract,
  status = 'retenida',
  deductions = [],
  returned,
  returnedAt,
  method,
  style,
}: DepositSummaryProps): JSX.Element {
  const pendiente = Math.max(0, required - collected);
  const descuentos = deductions.reduce((s, d) => s + (Number(d.amount) || 0), 0);
  const aDevolver = collected - descuentos;
  return (
    <div className="ar-card" style={style}>
      <div className="ar-card__head">
        <div>
          <h3 className="ar-card__title">Fianza</h3>
          {contract && <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>{contract}</div>}
        </div>
        <Badge status={status as EstadoBadge} />
      </div>
      <div className="ar-card__body" style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        <KeyValueList
          columns={3}
          items={[
            { label: 'Exigido · ' + count + (count === 1 ? ' fianza' : ' fianzas'), value: <Money amount={required} currency={currency} /> },
            { label: 'Cobrado', value: <Money amount={collected} currency={currency} /> },
            { label: 'Pendiente', value: <Money amount={pendiente} currency={currency} tone={pendiente > 0 ? 'danger' : 'muted'} /> },
          ]}
        />
        {(deductions.length > 0 || returned != null) && (
          <div style={{ paddingTop: 17, borderTop: '1px solid var(--border-subtle)' }}>
            <div className="ar-overline" style={{ marginBottom: 10 }}>
              Liquidación
            </div>
            <dl className="ar-totals">
              {deductions.map((d, i) => (
                <div key={i}>
                  <dt>{d.reason}</dt>
                  <dd>
                    <Money amount={-d.amount} currency={currency} tone="danger" />
                  </dd>
                </div>
              ))}
              <div className="is-total">
                <dt>{returned != null ? 'Devuelto' : 'A devolver'}</dt>
                <dd>
                  <Money amount={returned ?? aDevolver} currency={currency} strong />
                </dd>
              </div>
              {returnedAt && (
                <div>
                  <dt>Fecha · forma</dt>
                  <dd style={{ font: 'var(--type-body-sm)', color: 'var(--text-muted)' }}>
                    {returnedAt}
                    {method ? ' · ' + method : ''}
                  </dd>
                </div>
              )}
            </dl>
          </div>
        )}
      </div>
    </div>
  );
}
