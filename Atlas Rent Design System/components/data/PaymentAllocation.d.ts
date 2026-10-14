/** Applies one cobro across open invoices of the same contract (§13.5–13.6). Default = oldest first; remainder becomes saldo a favor; over-allocation is flagged. */
export interface AllocationInvoice { id: string; concept?: string; period?: string; pending: number; applied?: number | string; overdue?: boolean; }
export interface PaymentAllocationProps { amount: number; currency?: 'USD' | 'DOP'; invoices: AllocationInvoice[]; onChange?: (invoices: AllocationInvoice[]) => void; style?: React.CSSProperties; }
export declare function PaymentAllocation(props: PaymentAllocationProps): JSX.Element;