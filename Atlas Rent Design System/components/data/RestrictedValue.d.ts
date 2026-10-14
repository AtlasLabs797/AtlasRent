/** Role gate for aggregated financial totals (§17.2): Empleado sees a lock chip instead of the figure. */
export interface RestrictedValueProps { allowed?: boolean; children?: React.ReactNode; label?: string; style?: React.CSSProperties; }
export declare function RestrictedValue(props: RestrictedValueProps): JSX.Element;