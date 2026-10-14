/**
 * KPI tile for dashboards.
 */
export interface StatCardProps { label: string; value: React.ReactNode; meta?: React.ReactNode; icon?: string; tone?: 'default' | 'success' | 'warning' | 'danger'; /** Dark tile (--tile-1) with 44px mono KPI and accent-on-dark label. Max one per row, usually span 2. */ featured?: boolean; onClick?: () => void; style?: React.CSSProperties; }
export declare function StatCard(props: StatCardProps): JSX.Element;
