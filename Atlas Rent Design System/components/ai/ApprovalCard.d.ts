/**
 * Human approval request raised by an AI agent (bandeja de aprobaciones IA).
 */
export interface ApprovalCardProps {
  agent: string; action: string; reason: React.ReactNode; record: string;
  current?: React.ReactNode; proposed?: React.ReactNode; impact?: React.ReactNode;
  risk?: 'bajo' | 'medio' | 'alto'; requestedAt?: string; origin?: string;
  onApprove?: () => void; onReject?: () => void; onEdit?: () => void; style?: React.CSSProperties;
}
export declare function ApprovalCard(props: ApprovalCardProps): JSX.Element;
