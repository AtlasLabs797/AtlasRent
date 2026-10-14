/** Status pill. Pass `status` for domain states (auto label + tone) or `tone` + children for custom. */
export interface BadgeProps {
  tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info' | 'ai';
  status?: 'disponible' | 'reservado' | 'ocupado' | 'mantenimiento' | 'fuera' | 'pendiente' | 'parcial' | 'pagado' | 'vencida' | 'anulada' | 'borrador' | 'revision' | 'activo' | 'baja' | 'finalizado';
  dot?: boolean; square?: boolean; children?: React.ReactNode; style?: React.CSSProperties;
}
export declare function Badge(props: BadgeProps): JSX.Element;
