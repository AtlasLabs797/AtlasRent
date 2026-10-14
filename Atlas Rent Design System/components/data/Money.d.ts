/** Currency amount with tabular numerals; optional frozen DOP equivalent. */
export interface MoneyProps { amount: number; currency?: 'USD' | 'DOP'; /** DOP per USD applied (frozen at issue) */ rate?: number; tone?: 'default' | 'muted' | 'success' | 'danger'; strong?: boolean; style?: React.CSSProperties; }
export declare function Money(props: MoneyProps): JSX.Element;
