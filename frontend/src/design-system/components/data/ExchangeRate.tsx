import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';
import { KeyValueList } from './KeyValueList';

const dosDecimales = (n: number) => n.toFixed(2).replace('.', ',');

/** USD/DOP rate as market + margen → aplicado (§15). Inline for topbar/forms, panel for invoice/cobro detail. frozenIn marks a historical, non-recalculated rate. */
export interface ExchangeRateProps {
  market: number;
  margin?: number;
  final?: number;
  at?: string;
  source?: string;
  /** Record that froze this rate, e.g. FAC-A-2026-00412. Shows a lock. */
  frozenIn?: string;
  variant?: 'inline' | 'panel';
  style?: CSSProperties;
}

export function ExchangeRate({
  market,
  margin = 0,
  final,
  at,
  source,
  frozenIn,
  variant = 'inline',
  style,
}: ExchangeRateProps): JSX.Element {
  const fin = final ?? market + margin;
  if (variant === 'inline') {
    return (
      <span className="ar-rate" style={style}>
        USD/DOP <b>{dosDecimales(market)}</b>
        <span className="mono">
          {margin >= 0 ? '+' : '−'}
          {dosDecimales(Math.abs(margin))}
        </span>
        →<b className="is-final">{dosDecimales(fin)}</b>
        {frozenIn && <Icon name="lock" size={12} />}
      </span>
    );
  }
  return (
    <div className="ar-card" style={{ padding: 20, ...style }}>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 12,
          marginBottom: 17,
        }}
      >
        <span className="ar-overline">Tipo de cambio USD/DOP</span>
        {frozenIn ? (
          <span className="ar-badge ar-badge--square" style={{ gap: 4 }}>
            <Icon name="lock" size={11} />
            Congelado · {frozenIn}
          </span>
        ) : (
          <span
            className="ar-badge ar-badge--square"
            style={{ background: 'var(--success-soft)', color: 'var(--success)' }}
          >
            Hoy
          </span>
        )}
      </div>
      <div
        style={{
          font: 'var(--type-kpi)',
          fontSize: 34,
          color: 'var(--text-strong)',
          marginBottom: 17,
        }}
      >
        {dosDecimales(fin)}
        <span style={{ font: 'var(--type-caption)', color: 'var(--text-subtle)', marginLeft: 8 }}>DOP por USD</span>
      </div>
      <KeyValueList
        columns={3}
        items={[
          { label: 'Mercado', value: dosDecimales(market), mono: true },
          { label: 'Margen', value: (margin >= 0 ? '+' : '−') + dosDecimales(Math.abs(margin)), mono: true },
          { label: 'Fecha', value: at, mono: true },
          ...(source ? [{ label: 'Fuente', value: source, span: true }] : []),
        ]}
      />
    </div>
  );
}