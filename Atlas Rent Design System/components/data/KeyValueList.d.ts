/** Record fields as overline label + value grid. Use on detail screens (cliente, unidad, contrato). */
export interface KeyValueListProps { items: { label: string; value?: React.ReactNode; mono?: boolean; span?: boolean }[]; columns?: 1 | 2 | 3 | 4; style?: React.CSSProperties; }
export declare function KeyValueList(props: KeyValueListProps): JSX.Element;