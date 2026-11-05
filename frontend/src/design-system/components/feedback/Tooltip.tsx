import type { JSX, ReactNode } from 'react';

/** Hover hint for icon buttons and truncated values. */
export interface TooltipProps {
  content: ReactNode;
  children: ReactNode;
  open?: boolean;
}

export function Tooltip({ content, children, open }: TooltipProps): JSX.Element {
  return (
    <span className="ar-tip" data-open={open ? 'true' : undefined}>
      {children}
      <span className="ar-tip__bubble" role="tooltip">
        {content}
      </span>
    </span>
  );
}
