/** Multi-line field: motivo de rechazo/anulación, observaciones, notas. */
export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> { label?: string; hint?: string; error?: string; required?: boolean; }
export declare function Textarea(props: TextareaProps): JSX.Element;