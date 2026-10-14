import React from 'react';
const TONES = {
  neutral: ['var(--neutral-soft)', 'var(--neutral)', 'var(--neutral-border)'],
  accent: ['var(--accent-soft)', 'var(--accent-text)', 'var(--accent-soft-border)'],
  success: ['var(--success-soft)', 'var(--success)', 'var(--success-border)'],
  warning: ['var(--warning-soft)', 'var(--warning)', 'var(--warning-border)'],
  danger: ['var(--danger-soft)', 'var(--danger)', 'var(--danger-border)'],
  info: ['var(--info-soft)', 'var(--info)', 'var(--info-border)'],
  ai: ['var(--ai-soft)', 'var(--ai-text)', 'var(--ai-border)'],
};
const STATUS = {
  disponible: ['success', 'Disponible'], reservado: ['info', 'Reservado'], ocupado: ['neutral', 'Ocupado'],
  mantenimiento: ['warning', 'En mantenimiento'], fuera: ['neutral', 'Fuera de servicio'],
  pendiente: ['neutral', 'Pendiente'], parcial: ['warning', 'Parcial'], pagado: ['success', 'Pagado'], vencida: ['danger', 'Vencida'], anulada: ['neutral', 'Anulada'],
  borrador: ['neutral', 'Borrador'], revision: ['warning', 'En revisión'], activo: ['success', 'Activo'], baja: ['danger', 'Baja programada'], finalizado: ['neutral', 'Finalizado'],
  aprobado: ['success', 'Aprobado'], rechazado: ['danger', 'Rechazado'], cancelada: ['neutral', 'Cancelada'],
  retenida: ['info', 'Retenida'], devolucion: ['warning', 'Pendiente de devolución'], devuelta: ['success', 'Devuelta'],
};
export function Badge({ tone, status, dot, square, children, style, ...rest }) {
  const s = status && STATUS[status];
  const t = tone || (s ? s[0] : 'neutral');
  const [bg, fg, bd] = TONES[t] || TONES.neutral;
  return <span className={'ar-badge' + (square ? ' ar-badge--square' : '')} style={{ background: bg, color: fg, borderColor: bd, textDecoration: status === 'anulada' ? 'line-through' : undefined, ...style }} {...rest}>
    {(dot ?? !!s) && <span className="ar-badge__dot" />}{children ?? (s ? s[1] : null)}
  </span>;
}
