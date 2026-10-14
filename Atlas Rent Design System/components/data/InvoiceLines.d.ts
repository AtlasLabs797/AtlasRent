/** Editable invoice line items (§10.5): concepto, detalle, cantidad, precio, importe + totals and DOP equivalent at the frozen rate. Descuento lines subtract. */
export interface InvoiceLine { concept: string; detail?: string; qty: number | string; price: number | string; }
export interface InvoiceLinesProps { lines: InvoiceLine[]; currency?: 'USD' | 'DOP'; rate?: number; editable?: boolean; onChange?: (lines: InvoiceLine[]) => void; concepts?: string[]; style?: React.CSSProperties; }
export declare function InvoiceLines(props: InvoiceLinesProps): JSX.Element;