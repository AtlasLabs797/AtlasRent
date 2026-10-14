import React from 'react';
const fmt = (n) => Number(n).toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const SYM = { USD: 'USD', DOP: 'DOP' };
export function Money({ amount, currency = 'USD', rate, tone, strong, style }) {
  const color = tone === 'danger' ? 'var(--danger)' : tone === 'success' ? 'var(--success)' : tone === 'muted' ? 'var(--text-muted)' : 'var(--text-strong)';
  return <span className="ar-money" style={{ color, fontWeight: strong ? 600 : 400, ...style }}>
    {amount < 0 && '−'}{fmt(Math.abs(amount))}<span className="ar-money__cur">{SYM[currency] || currency}</span>
    {rate && currency === 'USD' && <span className="ar-money__alt">≈ {fmt(amount * rate)} DOP · {rate.toFixed(2).replace('.', ',')}</span>}
  </span>;
}
