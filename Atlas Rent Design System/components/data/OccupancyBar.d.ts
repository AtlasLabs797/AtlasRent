/** Stacked bar + legend of the five unit states (§3.5). Colours come from --unit-* tokens, never the accent. */
export interface OccupancyBarProps { counts: Partial<Record<'disponible' | 'reservado' | 'ocupado' | 'mantenimiento' | 'fuera', number>>; legend?: boolean; style?: React.CSSProperties; }
export declare function OccupancyBar(props: OccupancyBarProps): JSX.Element;