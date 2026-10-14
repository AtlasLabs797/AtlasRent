/** Transient dark confirmation, bottom-center, auto-dismiss ~4s. */
export interface ToastProps { tone?: 'success' | 'info' | 'danger' | 'ai'; children?: React.ReactNode; action?: React.ReactNode; onClose?: () => void; style?: React.CSSProperties; }
export declare function Toast(props: ToastProps): JSX.Element;
