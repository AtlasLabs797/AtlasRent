/** Dense data table: 36px header, 44px rows, hairline separators. */
export interface DataTableColumn { key: string; label: React.ReactNode; align?: 'left' | 'right'; width?: number | string; render?: (row: any) => React.ReactNode; }
export interface DataTableProps { columns: DataTableColumn[]; rows: any[]; rowKey?: string; onRowClick?: (row: any) => void; selectedKey?: any; empty?: React.ReactNode; style?: React.CSSProperties; }
export declare function DataTable(props: DataTableProps): JSX.Element;
