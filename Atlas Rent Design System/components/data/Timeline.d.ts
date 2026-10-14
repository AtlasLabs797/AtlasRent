/** Vertical history: contract lifecycle, renovaciones, incrementos de renta, envíos. */
export interface TimelineProps { items: { title: React.ReactNode; body?: React.ReactNode; time?: string; icon?: string; tone?: 'success' | 'warning' | 'danger' | 'info' | 'accent' | 'ai' }[]; style?: React.CSSProperties; }
export declare function Timeline(props: TimelineProps): JSX.Element;