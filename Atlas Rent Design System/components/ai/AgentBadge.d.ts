/** Identity chip for one of the six agents (§22.3). Always violet — the AI channel is the same in every Atlas app. */
export interface AgentBadgeProps { agent?: 'cobros' | 'contratos' | 'facturacion' | 'operaciones' | 'supervisor' | 'migracion'; label?: string; style?: React.CSSProperties; }
export declare function AgentBadge(props: AgentBadgeProps): JSX.Element;