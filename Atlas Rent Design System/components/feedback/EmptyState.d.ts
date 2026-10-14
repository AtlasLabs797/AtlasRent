/** Honest empty state: what the surface is for + next useful action. Two lines max. */
export interface EmptyStateProps { icon?: string; title: string; children?: React.ReactNode; action?: React.ReactNode; style?: React.CSSProperties; }
export declare function EmptyState(props: EmptyStateProps): JSX.Element;