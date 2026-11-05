import type { JSX, TextareaHTMLAttributes } from 'react';

/** Multi-line field: motivo de rechazo/anulación, observaciones, notas. */
export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  hint?: string;
  error?: string;
  required?: boolean;
}

export function Textarea({ label, hint, error, required, id, rows = 4, style, ...rest }: TextareaProps): JSX.Element {
  const fid = id || (label ? 't-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  return (
    <div className="ar-field" style={style}>
      {label && (
        <label className="ar-label" htmlFor={fid}>
          {label}
          {required && <em> *</em>}
        </label>
      )}
      <textarea id={fid} rows={rows} className={'ar-textarea' + (error ? ' ar-textarea--error' : '')} {...rest} />
      {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
    </div>
  );
}
