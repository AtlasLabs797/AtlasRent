import type { JSX, ReactNode } from 'react';
import { IconButton } from '../core/IconButton';

/** Modal dialog with scrim; footer holds actions right-aligned. */
export interface DialogProps {
  open?: boolean;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  onClose?: () => void;
  width?: number;
  /** Render without scrim (for previews) */
  inline?: boolean;
}

export function Dialog({ open = true, title, description, children, footer, onClose, width = 520, inline }: DialogProps): JSX.Element | null {
  if (!open) return null;
  const caja = (
    <div className="ar-dialog" role="dialog" aria-modal="true" style={{ maxWidth: width }} onClick={(e) => e.stopPropagation()}>
      <div className="ar-dialog__head">
        <div>
          <h2 className="ar-dialog__title">{title}</h2>
          {description && <p className="ar-dialog__desc">{description}</p>}
        </div>
        {onClose && <IconButton icon="x" label="Cerrar" size="sm" onClick={onClose} />}
      </div>
      <div className="ar-dialog__body">{children}</div>
      {footer && <div className="ar-dialog__foot">{footer}</div>}
    </div>
  );
  return inline ? caja : (
    <div className="ar-scrim" onClick={onClose}>
      {caja}
    </div>
  );
}
