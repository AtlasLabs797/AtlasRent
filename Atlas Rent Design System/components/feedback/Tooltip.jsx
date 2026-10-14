import React from 'react';
export function Tooltip({ content, children, open }) {
  return <span className="ar-tip" data-open={open ? 'true' : undefined}>{children}<span className="ar-tip__bubble" role="tooltip">{content}</span></span>;
}
