import React from 'react';
import { IconButton } from '../core/IconButton.jsx';
export function Dialog({ open = true, title, description, children, footer, onClose, width = 520, inline }) {
  if (!open) return null;
  const box = <div className="ar-dialog" role="dialog" aria-modal="true" style={{ maxWidth: width }} onClick={e => e.stopPropagation()}>
    <div className="ar-dialog__head"><div><h2 className="ar-dialog__title">{title}</h2>{description && <p className="ar-dialog__desc">{description}</p>}</div>{onClose && <IconButton icon="x" label="Cerrar" size="sm" onClick={onClose} />}</div>
    <div className="ar-dialog__body">{children}</div>
    {footer && <div className="ar-dialog__foot">{footer}</div>}
  </div>;
  return inline ? box : <div className="ar-scrim" onClick={onClose}>{box}</div>;
}
