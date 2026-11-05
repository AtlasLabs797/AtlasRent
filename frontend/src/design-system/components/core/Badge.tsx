import type { CSSProperties, JSX, ReactNode } from 'react';

const TONES: Record<string, [string, string, string]> = {
  neutral: ['var(--neutral-soft)', 'var(--neutral)', 'var(--neutral-border)'],
  accent: ['var(--accent-soft)', 'var(--accent-text)', 'var(--accent-soft-border)'],
  success: ['var(--success-soft)', 'var(--success)', 'var(--success-border)'],
  warning: ['var(--warning-soft)', 'var(--warning)', 'var(--warning-border)'],
  danger: ['var(--danger-soft)', 'var(--danger)', 'var(--danger-border)'],
  info: ['var(--info-soft)', 'var(--info)', 'var(--info-border)'],
  ai: ['var(--ai-soft)', 'var(--ai-text)', 'var(--ai-border)'],
};

export type EstadoBadge =
  | 'disponible'
  | 'reservado'
  | 'ocupado'
  | 'mantenimiento'
  | 'fuera'
  | 'pendiente'
  | 'parcial'
  | 'pagado'
  | 'vencida'
  | 'anulada'
  | 'borrador'
  | 'revision'
  | 'activo'
  | 'baja'
  | 'finalizado'
  | 'aprobado'
  | 'rechazado'
  | 'cancelada'
  | 'retenida'
  | 'devolucion'
  | 'devuelta';

const ESTADOS: Record<EstadoBadge, [string, string]> = {
  disponible: ['success', 'Disponible'],
  reservado: ['info', 'Reservado'],
  ocupado: ['neutral', 'Ocupado'],
  mantenimiento: ['warning', 'En mantenimiento'],
  fuera: ['neutral', 'Fuera de servicio'],
  pendiente: ['neutral', 'Pendiente'],
  parcial: ['warning', 'Parcial'],
  pagado: ['success', 'Pagado'],
  vencida: ['danger', 'Vencida'],
  anulada: ['neutral', 'Anulada'],
  borrador: ['neutral', 'Borrador'],
  revision: ['warning', 'En revisión'],
  activo: ['success', 'Activo'],
  baja: ['danger', 'Baja programada'],
  finalizado: ['neutral', 'Finalizado'],
  aprobado: ['success', 'Aprobado'],
  rechazado: ['danger', 'Rechazado'],
  cancelada: ['neutral', 'Cancelada'],
  retenida: ['info', 'Retenida'],
  devolucion: ['warning', 'Pendiente de devolución'],
  devuelta: ['success', 'Devuelta'],
};

export type EstadoUnitad = Extract<EstadoBadge, 'disponible' | 'reservado' | 'ocupado' | 'mantenimiento' | 'fuera'>;

/** Status pill. Pass `status` for domain states (auto label + tone) or `tone` + children for custom. */
export interface BadgeProps {
  tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info' | 'ai';
  status?: EstadoBadge;
  dot?: boolean;
  square?: boolean;
  children?: ReactNode;
  style?: CSSProperties;
}

export function Badge({ tone, status, dot, square, children, style }: BadgeProps): JSX.Element {
  const estado = status ? ESTADOS[status] : undefined;
  const clave = tone ?? (estado ? estado[0] : 'neutral');
  const [bg, fg, bd] = TONES[clave] ?? TONES.neutral;
  return (
    <span
      className={'ar-badge' + (square ? ' ar-badge--square' : '')}
      style={{
        background: bg,
        color: fg,
        borderColor: bd,
        textDecoration: status === 'anulada' ? 'line-through' : undefined,
        ...style,
      }}
    >
      {(dot ?? !!estado) && <span className="ar-badge__dot" />}
      {children ?? (estado ? estado[1] : null)}
    </span>
  );
}