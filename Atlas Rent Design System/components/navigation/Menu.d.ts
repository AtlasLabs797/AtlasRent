/** Action menu anchored to a trigger (row "…" actions, "Más" in page headers, AI actions). Keyboard: ↓/↑, ↵, Esc. */
export interface MenuItem { label?: React.ReactNode; value?: string; icon?: string; meta?: string; danger?: boolean; disabled?: boolean; tone?: 'ai'; onClick?: () => void; separator?: boolean; heading?: string; }
export interface MenuProps { /** A Button or IconButton element; Menu wires onClick/aria-expanded onto it */ trigger: React.ReactElement; items: MenuItem[]; align?: 'left' | 'right'; /** Applies to every item; 'ai' tints icons violet */ tone?: 'ai'; onSelect?: (value: string, item: MenuItem) => void; style?: React.CSSProperties; }
export declare function Menu(props: MenuProps): JSX.Element;
