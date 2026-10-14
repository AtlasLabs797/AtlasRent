/** White surface with 1px hairline border and 18px radius, no shadow. */
export interface CardProps { title?: React.ReactNode; actions?: React.ReactNode; padded?: boolean; children?: React.ReactNode; style?: React.CSSProperties; className?: string; }
export declare function Card(props: CardProps): JSX.Element;
