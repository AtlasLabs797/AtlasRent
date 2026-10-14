import React from 'react';
import { IconButton } from '../core/IconButton.jsx';
export function Drawer({ open = true, title, description, children, footer, onClose, width = 560, inline, style }) {
  if (!open) return null;
  const box = <aside className={'ar-drawer' + (inline ? ' ar-drawer--inline' : '')} role="dialog" aria-modal={!inline} style={{ width, ...style }} onClick={e => e.stopPropagation()}>
    <div className="ar-drawer__head"><div style={{ minWidth: 0 }}><h2 className="ar-dialog__title">{title}</h2>{description && <p className="ar-dialog__desc">{description}</p>}</div>{onClose && <IconButton icon="x" label="Cerrar" size="sm" onClick={onClose} />}</div>
    <div className="ar-drawer__body">{children}</div>
    {footer && <div className="ar-drawer__foot">{footer}</div>}
  </aside>;
  return inline ? box : <div className="ar-drawer-scrim" onClick={onClose}>{box}</div>;
}