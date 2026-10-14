/** Audit trail rows (§2.4): actor (user/agent/system), acción, registro, valor anterior → nuevo, motivo, aprobación, origen. */
export interface AuditEntry { actor: string; actorType?: 'user' | 'agent' | 'system'; action: string; record?: string; field?: string; before?: React.ReactNode; after?: React.ReactNode; reason?: string; approval?: string; at: string; origin?: 'factusol' | 'manual' | 'auto' | 'agente' | 'excel' | 'api'; }
export interface AuditLogProps { entries: AuditEntry[]; style?: React.CSSProperties; }
export declare function AuditLog(props: AuditLogProps): JSX.Element;