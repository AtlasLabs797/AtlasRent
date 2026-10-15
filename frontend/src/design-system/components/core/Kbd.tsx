import type { CSSProperties, JSX, ReactNode } from 'react';

/** Keyboard key hint in mono (⌘ K, ↑, esc). The only place unicode glyphs are allowed as icons. */
export interface KbdProps {
  children: ReactNode;
  style?: CSSProperties;
}

export function Kbd({ children, style }: KbdProps): JSX.Element {
  return (
    <kbd className="ar-kbd" style={style}>
      {children}
    </kbd>
  );
}