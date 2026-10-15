import type { CSSProperties, JSX } from 'react';

// DESIGN.md §5: punto de miles, coma decimal, 2 decimales (1.250,00 USD).
const formato = (n: number) =>
  n.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const SIMBOLO: Record<string, string> = { USD: 'USD', DOP: 'DOP' };

/** Currency amount with tabular numerals; optional frozen DOP equivalent. */
export interface MoneyProps {
  amount: number;
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
      {amount < 0 && '−'}
      {formato(Math.abs(amount))}
      <span className="ar-money__cur">{SIMBOLO[currency] ?? currency}</span>
      {rate && currency === 'USD' && (
        <span className="ar-money__alt">
          ≈ {formato(amount * rate)} DOP · {rate.toFixed(2).replace('.', ',')}
        </span>
      )}
    </span>
  );
}