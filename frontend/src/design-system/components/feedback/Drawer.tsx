import type { CSSProperties, JSX, ReactNode } from 'react';
import { IconButton } from '../core/IconButton';

/** 560px right-side panel for record detail and long forms. Dialog is for short confirmations. */
export interface DrawerProps {
  open?: boolean;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  onClose?: () => void;
  width?: number;
  /** Render in place (no scrim), for cards/specimens. */
  inline?: boolean;
  style?: CSSProperties;
}

export function Drawer({ open = true, title, description, children, footer, onClose, width = 560, inline, style }: DrawerProps): JSX.Element {
  if (!open) return <></>;
  const caja = (
    <aside
      className={'ar-drawer' + (inline ? ' ar-drawer--inline' : '')}
      role="dialog"
      aria-modal={!inline}
      style={{ width, ...style }}
      onClick={(e) => e.stopPropagation()}
    >
      <div className="ar-drawer__head">
        <div style={{ minWidth: 0 }}>
          <h2 className="ar-dialog__title">{title}</h2>
          {description && <p className="ar-dialog__desc">{description}</p>}
        </div>
        {onClose && <IconButton icon="x" label="Cerrar" size="sm" onClick={onClose} />}
      </div>
      <div className="ar-drawer__body">{children}</div>
      {footer && <div className="ar-drawer__foot">{footer}</div>}
    </aside>
  );
  return inline ? caja : (
    <div className="ar-drawer-scrim" onClick={onClose}>
      {caja}
    </div>
  );
}
