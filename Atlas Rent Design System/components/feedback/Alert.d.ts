/**
 * Inline persistent alert (e.g. reserva >7 días, fianza pendiente).
 */
export interface AlertProps { tone?: 'info' | 'success' | 'warning' | 'danger' | 'ai'; title?: React.ReactNode; children?: React.ReactNode; action?: React.ReactNode; style?: React.CSSProperties; }
export declare function Alert(props: AlertProps): JSX.Element;
