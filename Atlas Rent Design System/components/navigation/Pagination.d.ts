/** Table footer pagination: "1–25 de 150" + compact page list. */
export interface PaginationProps { page?: number; pageSize?: number; total?: number; onChange?: (page: number) => void; style?: React.CSSProperties; }
export declare function Pagination(props: PaginationProps): JSX.Element;