import type { CSSProperties, JSX } from 'react';
import { formatearNumero } from '../../../dominio/dinero';

// DESIGN.md §5: punto de miles, coma decimal, 2 decimales (1.250,00 USD). Un importe en string se
// formatea sin pasar por coma flotante; `number` queda para las muestras del design system.

const SIMBOLO: Record<string, string> = { USD: 'USD', DOP: 'DOP' };

/** Currency amount with tabular numerals; optional frozen DOP equivalent. */
export interface MoneyProps {
  /** String decimal de la API ("1250.50"). `number` solo para muestras. */
  amount: number | string;
  currency?: 'USD' | 'DOP';
  /** DOP per USD applied (frozen at issue) */
  rate?: number;
  tone?: 'default' | 'muted' | 'success' | 'danger';
  strong?: boolean;
  style?: CSSProperties;
}

export function Money({ amount, currency = 'USD', rate, tone, strong, style }: MoneyProps): JSX.Element {
  const color =
    tone === 'danger'
      ? 'var(--danger)'
      : tone === 'success'
        ? 'var(--success)'
        : tone === 'muted'
          ? 'var(--text-muted)'
          : 'var(--text-strong)';
  return (
    <span className="ar-money" style={{ color, fontWeight: strong ? 600 : 400, ...style }}>
      {formatearNumero(amount)}
      <span className="ar-money__cur">{SIMBOLO[currency] ?? currency}</span>
      {rate && currency === 'USD' && (
        <span className="ar-money__alt">
          ≈ {formatearNumero((Number(amount) * rate).toFixed(2))} DOP · {rate.toFixed(2).replace('.', ',')}
        </span>
      )}
    </span>
  );
}