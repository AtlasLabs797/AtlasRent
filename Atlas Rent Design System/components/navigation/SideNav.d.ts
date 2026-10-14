/** App sidebar: Atlas Rent lockup, grouped nav (Operación · Finanzas · Control), user footer. 264px, 72px rail when collapsed. */
export interface SideNavItem { key: string; icon: string; label: string; count?: number; countTone?: 'ai' | 'danger' | 'accent'; }
export interface SideNavProps { groups: { label: string; items: SideNavItem[] }[]; active?: string; onNavigate?: (key: string) => void; user?: { name: string; role: string }; collapsed?: boolean; caption?: string; style?: React.CSSProperties; }
export declare function SideNav(props: SideNavProps): JSX.Element;