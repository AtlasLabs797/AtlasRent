/** Horizontal progress for multi-step flows: contract lifecycle, migration (§25.3), reserva → contrato. */
export interface StepperProps { steps: { label: string; meta?: string; status?: 'done' | 'current' | 'todo' | 'error' }[]; style?: React.CSSProperties; }
export declare function Stepper(props: StepperProps): JSX.Element;