import React from 'react';
export function Textarea({ label, hint, error, required, id, rows = 4, style, ...rest }) {
  const fid = id || (label ? 't-' + String(label).toLowerCase().replace(/\W+/g, '-') : undefined);
  return <div className="ar-field" style={style}>
    {label && <label className="ar-label" htmlFor={fid}>{label}{required && <em> *</em>}</label>}
    <textarea id={fid} rows={rows} className={'ar-textarea' + (error ? ' ar-textarea--error' : '')} {...rest} />
    {(error || hint) && <span className={'ar-hint' + (error ? ' ar-hint--error' : '')}>{error || hint}</span>}
  </div>;
}